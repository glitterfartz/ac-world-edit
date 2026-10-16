using System.Text;

namespace ACWorldEdit.Tests.Support;

/// <summary>
/// Builds a minimal WDBC image in memory for DbcTable consumers. Fields are all uint32; strings
/// added through Str are offsets into the trailing block (offset 0 is the empty string).
/// </summary>
public sealed class DbcBuilder(int fields)
{
    readonly List<uint[]> rows = [];
    readonly List<byte> strings = [0];

    /// <summary>
    /// Add a NUL-terminated string and return its offset for use as a field value.
    /// </summary>
    public uint Str(string text)
    {
        uint offset = (uint)strings.Count;
        strings.AddRange(Encoding.UTF8.GetBytes(text));
        strings.Add(0);
        return offset;
    }

    /// <summary>
    /// Add a row; missing trailing fields are zero.
    /// </summary>
    public DbcBuilder Row(params uint[] values)
    {
        var row = new uint[fields];
        values.CopyTo(row, 0);
        rows.Add(row);
        return this;
    }

    public byte[] Build()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("WDBC"));
        writer.Write(rows.Count);
        writer.Write(fields);
        writer.Write(fields * 4);
        writer.Write(strings.Count);
        foreach (var row in rows)
        {
            foreach (uint value in row)
            {
                writer.Write(value);
            }
        }

        writer.Write(strings.ToArray());
        return stream.ToArray();
    }
}
