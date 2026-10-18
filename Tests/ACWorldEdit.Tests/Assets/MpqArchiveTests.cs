using System.Buffers.Binary;
using System.IO.Compression;
using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>Reads files from a tiny synthetic v0 MPQ covering every storage layout the stock client uses.</summary>
public sealed class MpqArchiveTests : IDisposable
{
    const int SectorSize = 512;
    const uint Exists = 0x80000000, SingleUnit = 0x01000000, Zlib = 0x200, Encrypted = 0x10000, FixKey = 0x20000;

    static readonly uint[] Crypt = BuildCrypt();
    readonly string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "acw-" + Guid.NewGuid().ToString("N") + ".mpq");
    readonly Dictionary<string, byte[]> expected = [];

    public void Dispose() => File.Delete(path);

    static byte[] Compressible(int length, int seed) => Enumerable.Range(0, length).Select(i => (byte)((i / 7 + seed) % 5)).ToArray();

    static byte[] Noise(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

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

    static uint Hash(string name, int type)
    {
        uint a = 0x7FED7FED, b = 0xEEEEEEEE;
        foreach (char c in name.Replace('/', '\\').ToUpperInvariant())
        {
            a = unchecked(Crypt[type * 256 + c] ^ (a + b));
            b = unchecked(c + a + b + (b << 5) + 3);
        }

        return a;
    }

    static void Encrypt(Span<byte> bytes, uint key)
    {
        uint seed = 0xEEEEEEEE;
        for (int i = 0; i + 4 <= bytes.Length; i += 4)
        {
            seed = unchecked(seed + Crypt[0x400 + (key & 255)]);
            uint plain = BinaryPrimitives.ReadUInt32LittleEndian(bytes[i..]);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[i..], plain ^ unchecked(key + seed));
            key = unchecked(((~key << 21) + 0x11111111) | (key >> 11));
            seed = unchecked(plain + seed + (seed << 5) + 3);
        }
    }

    static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>Pack a file the way the archive stores it: a sector table (when compressed) plus sectors.</summary>
    static (byte[] Stored, uint Flags) Pack(byte[] data, bool single, bool compress, bool encrypt, bool fix, string name, uint filePos)
    {
        uint flags = Exists | (single ? SingleUnit : 0) | (compress ? Zlib : 0) | (encrypt ? Encrypted : 0) | (fix ? FixKey : 0);
        uint key = Hash(name.Replace('/', '\\').Split('\\')[^1], 3);
        if (fix)
        {
            key = unchecked((key + filePos) ^ (uint)data.Length);
        }

        byte[] Seal(byte[] unit)
        {
            if (!compress)
            {
                return unit;
            }

            byte[] packed = [2, .. Deflate(unit)];
            return packed.Length < unit.Length ? packed : unit;
        }

        if (single)
        {
            var one = Seal(data);
            if (encrypt)
            {
                Encrypt(one, key);
            }

            return (one, flags);
        }

        int count = (data.Length + SectorSize - 1) / SectorSize;
        var sectors = Enumerable.Range(0, count).Select(i => Seal(data[(i * SectorSize)..Math.Min(data.Length, (i + 1) * SectorSize)])).ToList();
        if (!compress)
        {
            if (encrypt)
            {
                for (int i = 0; i < count; i++)
                {
                    Encrypt(sectors[i], key + (uint)i);
                }
            }

            return (sectors.SelectMany(s => s).ToArray(), flags);
        }

        var table = new byte[(count + 1) * 4];
        uint offset = (uint)table.Length;
        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(i * 4), offset);
            offset += (uint)sectors[i].Length;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(count * 4), offset);
        if (encrypt)
        {
            Encrypt(table, key - 1);
            for (int i = 0; i < count; i++)
            {
                Encrypt(sectors[i], key + (uint)i);
            }
        }

        return ([.. table, .. sectors.SelectMany(s => s)], flags);
    }

    void Build((string Name, byte[] Data, bool Single, bool Compress, bool Encrypt, bool Fix)[] files)
    {
        var body = new List<byte>();
        var blocks = new byte[files.Length * 16];
        const int headerSize = 32;
        for (int i = 0; i < files.Length; i++)
        {
            var f = files[i];
            uint pos = (uint)(headerSize + body.Count);
            var (stored, flags) = Pack(f.Data, f.Single, f.Compress, f.Encrypt, f.Fix, f.Name, pos);
            body.AddRange(stored);
            BinaryPrimitives.WriteUInt32LittleEndian(blocks.AsSpan(i * 16), pos);
            BinaryPrimitives.WriteUInt32LittleEndian(blocks.AsSpan(i * 16 + 4), (uint)stored.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(blocks.AsSpan(i * 16 + 8), (uint)f.Data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(blocks.AsSpan(i * 16 + 12), flags);
            expected[f.Name] = f.Data;
        }

        const int hashEntries = 16;
        var hashes = new byte[hashEntries * 16];
        Array.Fill(hashes, (byte)0xFF);
        for (int i = 0; i < files.Length; i++)
        {
            int slot = (int)(Hash(files[i].Name, 0) % hashEntries);
            while (BinaryPrimitives.ReadUInt32LittleEndian(hashes.AsSpan(slot * 16 + 12)) != uint.MaxValue)
            {
                slot = (slot + 1) % hashEntries;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(hashes.AsSpan(slot * 16), Hash(files[i].Name, 1));
            BinaryPrimitives.WriteUInt32LittleEndian(hashes.AsSpan(slot * 16 + 4), Hash(files[i].Name, 2));
            BinaryPrimitives.WriteUInt16LittleEndian(hashes.AsSpan(slot * 16 + 8), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(hashes.AsSpan(slot * 16 + 10), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(hashes.AsSpan(slot * 16 + 12), (uint)i);
        }

        Encrypt(hashes, Hash("(hash table)", 3));
        Encrypt(blocks, Hash("(block table)", 3));
        uint hashPos = (uint)(headerSize + body.Count), blockPos = hashPos + (uint)hashes.Length;
        var header = new byte[headerSize];
        header[0] = (byte)'M';
        header[1] = (byte)'P';
        header[2] = (byte)'Q';
        header[3] = 0x1A;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), headerSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), blockPos + (uint)blocks.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), hashPos);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), blockPos);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), hashEntries);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), (uint)files.Length);
        File.WriteAllBytes(path, [.. header, .. body, .. hashes, .. blocks]);
    }

    [Fact]
    public void ReadsEveryStorageLayout()
    {
        Build(
        [
            (@"Data\single.bin", Noise(100, 1), true, false, false, false),
            (@"Data\sectors.bin", Noise(1300, 2), false, false, false, false),
            (@"Data\zsectors.bin", Compressible(1300, 3), false, true, false, false),
            (@"Data\zmixed.bin", [.. Compressible(512, 4), .. Noise(512, 5), .. Compressible(200, 6)], false, true, false, false),
            (@"Data\esectors.bin", Noise(1300, 7), false, false, true, false),
            (@"Data\ezsectors.bin", Compressible(1300, 8), false, true, true, true),
            (@"Data\zsingle.bin", Compressible(900, 9), true, true, false, false),
            (@"Data\ezsingle.bin", Compressible(900, 10), true, true, true, true),
            (@"Data\empty.bin", [], true, false, false, false),
        ]);

        using var archive = new MpqArchive(path);
        foreach (var (name, data) in expected)
        {
            Assert.True(archive.Contains(name), name);
            Assert.Equal(data, archive.Read(name));
            Assert.Equal(data, archive.Read(name.Replace('\\', '/').ToLowerInvariant()));
        }

        Assert.False(archive.Contains(@"Data\missing.bin"));
        Assert.Null(archive.Read(@"Data\missing.bin"));
    }

    [Fact]
    public void ReadsConcurrently()
    {
        Build(Enumerable.Range(0, 6).Select(i => ($@"Data\f{i}.bin", Compressible(3000 + i, i), false, true, i % 2 == 0, i % 2 == 0)).ToArray());
        using var archive = new MpqArchive(path);
        Parallel.For(0, 200, i =>
        {
            string name = $@"Data\f{i % 6}.bin";
            Assert.Equal(expected[name], archive.Read(name));
        });
    }
}
