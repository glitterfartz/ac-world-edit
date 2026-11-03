namespace ACWorldEdit.Assets;

/// <summary>
/// Detached decoded BLP2 mip in RGBA byte order. Terrain/liquids can choose smaller native mips
/// while model textures retain full source resolution; no GPU resources are created here.
/// </summary>
public sealed record BlpTexture(int Width, int Height, byte[] Rgba)
{
    /// <summary>
    /// Decode palette/raw/DXT1/DXT3/DXT5/BC5 storage with native alpha packing. Select the first
    /// available mip within the requested target; zero requests full resolution. Reject unsupported
    /// encodings and truncated blocks.
    /// </summary>
    public static BlpTexture Decode(byte[] b, int maximumDimension = 512)
    {
        if (b.Length < 148 || System.Text.Encoding.ASCII.GetString(b, 0, 4) != "BLP2")
        {
            throw new NotSupportedException("Expected BLP2 texture");
        }

        int encoding = b[8], depth = b[9], alphaEncoding = b[10];
        int w = checked((int)LittleEndian.U32(b, 12)), h = checked((int)LittleEndian.U32(b, 16));
        if (w is < 1 or > 8192 || h is < 1 or > 8192)
        {
            throw new InvalidDataException("Invalid BLP dimensions");
        }

        // Select a smaller native mip when requested; do not resample source pixels.
        // Zero keeps full model resolution, and missing smaller mips stop the search.
        int mip = 0;
        while (maximumDimension > 0 && Math.Max(w, h) > maximumDimension && mip < 15 && LittleEndian.U32(b, 20 + (mip + 1) * 4) != 0)
        {
            mip++;
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }

        int offset = checked((int)LittleEndian.U32(b, 20 + mip * 4)), length = checked((int)LittleEndian.U32(b, 84 + mip * 4));
        ReadOnlySpan<byte> input = b.AsSpan(offset, length);
        byte[] pixels = new byte[checked(w * h * 4)];
        // Palette RGB is BGRA in the header; alpha follows the index plane and can be
        // absent or packed at 1, 4 or 8 bits per pixel.
        if (encoding == 1)
        {
            int n = w * h;
            if (depth is not (0 or 1 or 4 or 8) || input.Length < n + (n * depth + 7) / 8 || b.Length < 1172)
            {
                throw new InvalidDataException("Invalid palettized BLP");
            }

            for (int i = 0; i < n; i++)
            {
                int p = 148 + input[i] * 4;
                pixels[i * 4] = b[p + 2];
                pixels[i * 4 + 1] = b[p + 1];
                pixels[i * 4 + 2] = b[p];
                pixels[i * 4 + 3] = depth switch
                {
                    0 => (byte)255,
                    1 => (byte)(((input[n + i / 8] >> (i % 8)) & 1) * 255),
                    4 => (byte)(((input[n + i / 2] >> ((i % 2) * 4)) & 15) * 17),
                    _ => input[n + i]
                };
            }
        }
        else if (encoding == 3)
        {
            if (input.Length < pixels.Length)
            {
                throw new InvalidDataException("Short raw BLP");
            }

            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = input[i + 2];
                pixels[i + 1] = input[i + 1];
                pixels[i + 2] = input[i];
                pixels[i + 3] = input[i + 3];
            }
        }
        else if (encoding == 2 && alphaEncoding == 11)
        {
            // Retail BLP preferred format 11 is BC5_UNORM: two BC4 blocks, red then green.
            // Keep the two channels raw; the consuming shader reconstructs its own normal.
            Span<byte> red = stackalloc byte[16];
            Span<byte> green = stackalloc byte[16];
            int pos = 0;
            for (int y = 0; y < h; y += 4)
            {
                for (int x = 0; x < w; x += 4)
                {
                    if (pos + 16 > input.Length) throw new InvalidDataException("Short BC5 block");
                    DecodeBc4(input.Slice(pos, 8), red);
                    DecodeBc4(input.Slice(pos + 8, 8), green);
                    for (int i = 0; i < 16; i++)
                    {
                        int px = x + i % 4, py = y + i / 4;
                        if (px >= w || py >= h) continue;
                        int p = (py * w + px) * 4;
                        pixels[p] = red[i];
                        pixels[p + 1] = green[i];
                        pixels[p + 3] = 255;
                    }
                    pos += 16;
                }
            }
        }
        else if (encoding == 2)
        {
            // DXT blocks cover 4-by-4 pixels. DXT1 uses endpoint transparency; DXT3
            // stores explicit alpha nibbles, while DXT5 interpolates an alpha table.
            int blockSize = depth > 1 ? 16 : 8;
            if (depth > 1 && alphaEncoding is not (1 or 7))
            {
                throw new NotSupportedException("Unsupported BLP DXT alpha encoding");
            }

            // Scratch for one block, reused across the loop (a stackalloc inside it would accumulate).
            Span<byte> colors = stackalloc byte[16];
            Span<byte> alpha = stackalloc byte[16];
            Span<byte> table = stackalloc byte[8];
            int pos = 0;
            for (int y = 0; y < h; y += 4)
            {
                for (int x = 0; x < w; x += 4)
                {
                    if (pos + blockSize > input.Length)
                    {
                        throw new InvalidDataException("Short DXT block");
                    }

                    int colorPos = pos + blockSize - 8;
                    ushort c0 = LittleEndian.U16(input, colorPos), c1 = LittleEndian.U16(input, colorPos + 2);
                    // Four RGBA palette entries, flat: entry n occupies colors[n * 4 .. n * 4 + 3].
                    Color565(c0, colors[..4]);
                    Color565(c1, colors[4..8]);
                    bool four = c0 > c1 || blockSize == 16 || depth == 0;
                    for (int ch = 0; ch < 3; ch++)
                    {
                        colors[8 + ch] = (byte)(four ? (2 * colors[ch] + colors[4 + ch]) / 3 : (colors[ch] + colors[4 + ch]) / 2);
                        colors[12 + ch] = (byte)(four ? (colors[ch] + 2 * colors[4 + ch]) / 3 : 0);
                    }

                    colors[11] = 255;
                    colors[15] = four ? (byte)255 : (byte)0;
                    uint indices = LittleEndian.U32(input, colorPos + 4);
                    alpha.Fill(255);
                    if (blockSize == 16 && alphaEncoding == 1)
                    {
                        for (int i = 0; i < 16; i++)
                        {
                            alpha[i] = (byte)(((input[pos + i / 2] >> ((i & 1) * 4)) & 15) * 17);
                        }
                    }
                    else if (blockSize == 16)
                    {
                        table[0] = input[pos];
                        table[1] = input[pos + 1];
                        if (table[0] > table[1])
                        {
                            for (int i = 2; i < 8; i++)
                            {
                                table[i] = (byte)(((8 - i) * table[0] + (i - 1) * table[1]) / 7);
                            }
                        }
                        else
                        {
                            for (int i = 2; i < 6; i++)
                            {
                                table[i] = (byte)(((6 - i) * table[0] + (i - 1) * table[1]) / 5);
                            }

                            table[6] = 0;
                            table[7] = 255;
                        }

                        ulong bits = 0;
                        for (int i = 0; i < 6; i++)
                        {
                            bits |= (ulong)input[pos + 2 + i] << (8 * i);
                        }

                        for (int i = 0; i < 16; i++)
                        {
                            alpha[i] = table[(int)((bits >> (i * 3)) & 7)];
                        }
                    }

                    for (int i = 0; i < 16; i++)
                    {
                        int px = x + i % 4, py = y + i / 4;
                        if (px >= w || py >= h)
                        {
                            continue;
                        }

                        int p = (py * w + px) * 4;
                        int color = (int)((indices >> (i * 2)) & 3) * 4;
                        for (int ch = 0; ch < 3; ch++)
                        {
                            pixels[p + ch] = colors[color + ch];
                        }

                        pixels[p + 3] = blockSize == 16 ? alpha[i] : colors[color + 3];
                    }

                    pos += blockSize;
                }
            }
        }
        else
        {
            throw new NotSupportedException("Unsupported BLP encoding " + encoding);
        }

        return new(w, h, pixels);
    }

    /// <summary>
    /// Expand DXT RGB565 endpoints to byte channels before block interpolation; alpha initially
    /// remains opaque.
    /// </summary>
    static void Color565(ushort c, Span<byte> rgba)
    {
        rgba[0] = (byte)(((c >> 11) & 31) * 255 / 31);
        rgba[1] = (byte)(((c >> 5) & 63) * 255 / 63);
        rgba[2] = (byte)((c & 31) * 255 / 31);
        rgba[3] = 255;
    }

    static void DecodeBc4(ReadOnlySpan<byte> block, Span<byte> output)
    {
        Span<byte> table = stackalloc byte[8];
        table[0] = block[0];
        table[1] = block[1];
        if (table[0] > table[1])
        {
            for (int i = 2; i < 8; i++) table[i] = (byte)(((8 - i) * table[0] + (i - 1) * table[1]) / 7);
        }
        else
        {
            for (int i = 2; i < 6; i++) table[i] = (byte)(((6 - i) * table[0] + (i - 1) * table[1]) / 5);
            table[6] = 0;
            table[7] = 255;
        }
        ulong bits = 0;
        for (int i = 0; i < 6; i++) bits |= (ulong)block[i + 2] << (8 * i);
        for (int i = 0; i < 16; i++) output[i] = table[(int)((bits >> (i * 3)) & 7)];
    }
}
