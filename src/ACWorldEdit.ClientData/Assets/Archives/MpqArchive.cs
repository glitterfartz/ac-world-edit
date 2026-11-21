using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using ICSharpCode.SharpZipLib.BZip2;

namespace ACWorldEdit.Assets;

/// <summary>
/// Classic MPQ v0/v1 reader with no write API. It supports stock Wrath compression/encryption,
/// bounded decompression and explicit rejection of unsupported incremental patches. Reads use
/// positional I/O and keep no per-read state, so they are safe from any number of threads until
/// the archive is disposed.
/// </summary>
public sealed class MpqArchive : IDisposable
{
    static readonly uint[] Crypt = BuildCrypt();
    readonly FileStream file;
    readonly Microsoft.Win32.SafeHandles.SafeFileHandle handle;
    readonly long origin;
    readonly byte[] hashes, blocks;
    readonly int sectorSize;
    public string Path { get; }

    /// <summary>
    /// Find the header on a 512-byte boundary, load/decrypt hash and block tables, and reject
    /// unsupported extended layouts.
    /// </summary>
    public MpqArchive(string path)
    {
        Path = path;
        file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        handle = file.SafeFileHandle;
        try
        {
            byte[] h = new byte[32];
            for (origin = 0; ; origin += 512)
            {
                if (origin > file.Length - 32)
                {
                    throw new InvalidDataException("MPQ header not found: " + path);
                }

                file.Position = origin;
                file.ReadExactly(h);
                if (LittleEndian.U32(h, 0) == 0x1A51504D)
                {
                    break;
                }
            }

            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(12));
            if (version > 1)
            {
                throw new NotSupportedException("Only classic MPQ v0/v1 is supported: " + path);
            }

            sectorSize = 512 << BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(14));
            long hashPos = LittleEndian.U32(h, 16), blockPos = LittleEndian.U32(h, 20);
            if (version == 1)
            {
                byte[] ext = new byte[12];
                file.ReadExactly(ext);
                if (BinaryPrimitives.ReadUInt64LittleEndian(ext) != 0)
                {
                    throw new NotSupportedException("MPQ extended block table");
                }

                hashPos |= (long)BinaryPrimitives.ReadUInt16LittleEndian(ext.AsSpan(8)) << 32;
                blockPos |= (long)BinaryPrimitives.ReadUInt16LittleEndian(ext.AsSpan(10)) << 32;
            }

            hashes = ReadAt(origin + hashPos, checked((int)LittleEndian.U32(h, 24) * 16));
            blocks = ReadAt(origin + blockPos, checked((int)LittleEndian.U32(h, 28) * 16));
            Decrypt(hashes, Hash("(hash table)", 3));
            Decrypt(blocks, Hash("(block table)", 3));
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public bool Contains(string name) => Find(new MpqPath(name)) >= 0;
    public bool Contains(MpqPath name) => Find(name) >= 0;
    /// <summary>
    /// Probe Blizzard path hashes. Empty entries terminate the probe; deleted entries are skipped,
    /// and a neutral-locale match wins over locale fallback.
    /// </summary>
    int Find(MpqPath name)
    {
        int n = hashes.Length / 16;
        if (n == 0)
        {
            return -1;
        }

        uint a = name.NameA, b = name.NameB;
        int start = (int)(name.Start % n), fallback = -1;
        for (int i = 0; i < n; i++)
        {
            int p = ((start + i) % n) * 16;
            uint index = LittleEndian.U32(hashes, p + 12);
            if (index == uint.MaxValue)
            {
                break;
            }

            if (index == 0xFFFFFFFE || LittleEndian.U32(hashes, p) != a || LittleEndian.U32(hashes, p + 4) != b)
            {
                continue;
            }

            if (index >= blocks.Length / 16)
            {
                throw new InvalidDataException("Bad MPQ block index");
            }

            if (BinaryPrimitives.ReadUInt16LittleEndian(hashes.AsSpan(p + 8)) == 0)
            {
                return (int)index;
            }

            fallback = (int)index;
        }

        return fallback;
    }

    /// <summary>
    /// Validate file flags/size and decode single-unit or sector storage with the appropriate
    /// encryption keys. Missing entries return null so mount lookup can continue.
    /// </summary>
    public byte[]? Read(string name) => Read(new MpqPath(name));

    public byte[]? Read(MpqPath path)
    {
        string name = path.Name;
        int index = Find(path);
        if (index < 0)
        {
            return null;
        }

        int p = index * 16;
        uint pos = LittleEndian.U32(blocks, p), packed = LittleEndian.U32(blocks, p + 4), size = LittleEndian.U32(blocks, p + 8), flags = LittleEndian.U32(blocks, p + 12);
        if ((flags & 0x80000000) == 0)
        {
            return null;
        }

        if ((flags & 0x02000000) != 0)
        {
            throw new InvalidDataException("MPQ deletion marker: " + name);
        }

        if ((flags & 0x00100000) != 0)
        {
            throw new NotSupportedException("Incremental MPQ patch: " + name);
        }

        if (size > 256 * 1024 * 1024 || packed > 256 * 1024 * 1024)
        {
            throw new InvalidDataException("Asset too large: " + name);
        }

        byte[] raw = ReadAt(origin + pos, (int)packed);
        if (size == 0)
        {
            return [];
        }

        bool encrypted = (flags & 0x10000) != 0;
        uint key = path.FileKey;
        if ((flags & 0x20000) != 0)
        {
            key = unchecked((key + pos) ^ size);
        }

        if ((flags & 0x01000000) != 0)
        {
            if (encrypted)
            {
                Decrypt(raw, key);
            }

            return Unpack(raw, 0, raw.Length, (int)size, flags);
        }

        int count = ((int)size + sectorSize - 1) / sectorSize;
        byte[] output = new byte[size];
        if ((flags & 0x300) == 0)
        {
            for (int i = 0; i < count; i++)
            {
                int len = Math.Min(sectorSize, (int)size - i * sectorSize);
                var sector = raw.AsSpan(i * sectorSize, len);
                if (encrypted)
                {
                    Decrypt(sector, key + (uint)i);
                }

                sector.CopyTo(output.AsSpan(i * sectorSize));
            }
        }
        else
        {
            int tableSize = (count + 1 + ((flags & 0x04000000) != 0 ? 1 : 0)) * 4;
            var table = raw.AsSpan(0, tableSize);
            if (encrypted)
            {
                Decrypt(table, key - 1);
            }

            for (int i = 0; i < count; i++)
            {
                int begin = checked((int)LittleEndian.U32(table, i * 4)), end = checked((int)LittleEndian.U32(table, (i + 1) * 4));
                if (begin < tableSize || end < begin || end > raw.Length)
                {
                    throw new InvalidDataException("Invalid MPQ sector offsets");
                }

                if (encrypted)
                {
                    Decrypt(raw.AsSpan(begin, end - begin), key + (uint)i);
                }

                Unpack(raw, begin, end - begin, Math.Min(sectorSize, (int)size - i * sectorSize), flags).CopyTo(output, i * sectorSize);
            }
        }

        return output;
    }

    /// <summary>
    /// Apply supported decompressors in reverse packing order and require the exact output length.
    /// Uncompressed sectors need no compression mask.
    /// </summary>
    static byte[] Unpack(byte[] raw, int offset, int length, int expected, uint flags)
    {
        if (length == expected)
        {
            return offset == 0 && length == raw.Length ? raw : raw.AsSpan(offset, length).ToArray();
        }

        if ((flags & 0x200) == 0 || length == 0)
        {
            throw new NotSupportedException("MPQ implode compression is not supported");
        }

        byte mask = raw[offset];
        byte[] result = raw[(offset + 1)..(offset + length)];
        if ((mask & ~0x12) != 0 || mask == 0)
        {
            throw new NotSupportedException($"MPQ compression mask 0x{mask:X2}");
        }

        // MPQ applies bzip2 after zlib when both bits are set: decode in reverse order.
        if ((mask & 0x10) != 0)
        {
            using var s = new BZip2InputStream(new MemoryStream(result));
            result = Inflate(s, expected);
        }

        if ((mask & 2) != 0)
        {
            using var s = new ZLibStream(new MemoryStream(result), CompressionMode.Decompress);
            result = Inflate(s, expected);
        }

        if (result.Length != expected)
        {
            throw new InvalidDataException("MPQ decompressed size mismatch");
        }

        return result;
    }

    /// <summary>
    /// Bound decompressor writes to the declared sector length instead of trusting compressed input.
    /// </summary>
    static byte[] Inflate(Stream stream, int limit)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int n;
        while ((n = stream.Read(buffer)) > 0)
        {
            if (output.Length + n > limit)
            {
                throw new InvalidDataException("MPQ decompression exceeds sector size");
            }

            output.Write(buffer, 0, n);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Read exactly at an absolute offset without touching the stream position.
    /// </summary>
    byte[] ReadAt(long pos, int size)
    {
        byte[] data = new byte[size];
        for (int done = 0; done < size;)
        {
            int n = RandomAccess.Read(handle, data.AsSpan(done), pos + done);
            if (n == 0)
            {
                throw new EndOfStreamException();
            }

            done += n;
        }

        return data;
    }

    /// <summary>
    /// Build the deterministic MPQ lookup tables used for path hashes and rolling-key decryption.
    /// </summary>
    static uint[] BuildCrypt()
    {
        uint seed = 0x100001;
        uint[] t = new uint[1280];
        for (int i = 0; i < 256; i++)
        {
            for (int j = 0; j < 5; j++)
            {
                seed = (seed * 125 + 3) % 0x2AAAAB;
                uint high = (seed & 0xFFFF) << 16;
                seed = (seed * 125 + 3) % 0x2AAAAB;
                t[i + j * 256] = high | (seed & 0xFFFF);
            }
        }

        return t;
    }

    /// <summary>
    /// Hash uppercase backslash paths using a selected MPQ variant. File encryption hashes the
    /// filename; archive lookup hashes the complete path.
    /// </summary>
    internal static uint Hash(string name, int type)
    {
        uint a = 0x7FED7FED, b = 0xEEEEEEEE;
        foreach (char c in name.Replace('/', '\\').ToUpperInvariant())
        {
            if (c > 255)
            {
                throw new ArgumentException("MPQ names must be ASCII");
            }

            a = unchecked(Crypt[type * 256 + c] ^ (a + b));
            b = unchecked(c + a + b + (b << 5) + 3);
        }

        return a;
    }

    /// <summary>
    /// Decrypt little-endian words with native rolling-key arithmetic. Trailing bytes smaller than a
    /// word are retained by the format.
    /// </summary>
    static void Decrypt(Span<byte> bytes, uint key)
    {
        uint seed = 0xEEEEEEEE;
        for (int i = 0; i + 4 <= bytes.Length; i += 4)
        {
            seed = unchecked(seed + Crypt[0x400 + (key & 255)]);
            uint value = LittleEndian.U32(bytes, i) ^ unchecked(key + seed);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[i..], value);
            key = unchecked(((~key << 21) + 0x11111111) | (key >> 11));
            seed = unchecked(value + seed + (seed << 5) + 3);
        }
    }

    public void Dispose() => file.Dispose();
}

/// <summary>
/// An asset path with its MPQ hashes computed once, so a lookup across every mounted archive
/// normalizes and hashes the name a single time. The file-key hash is deferred to the archive that
/// actually holds the file.
/// </summary>
public readonly struct MpqPath
{
    public string Name { get; }
    internal uint Start { get; }
    internal uint NameA { get; }
    internal uint NameB { get; }

    public MpqPath(string name)
    {
        Name = name;
        Start = MpqArchive.Hash(name, 0);
        NameA = MpqArchive.Hash(name, 1);
        NameB = MpqArchive.Hash(name, 2);
    }

    /// <summary>Encryption keys derive from the file name alone, without its directories.</summary>
    internal uint FileKey => MpqArchive.Hash(Name.Replace('/', '\\').Split('\\')[^1], 3);
}
