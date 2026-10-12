using System.Numerics;
using System.Text;

namespace ACWorldEdit.Assets;

/// <summary>
/// Engine-independent WDBC table indexed by its first uint field. Supported schemas use
/// four-byte fields; strings are offsets into the trailing UTF-8 block.
/// </summary>
public sealed class DbcTable
{
    readonly BinaryAsset data;
    readonly int count, fields, stride, strings;
    readonly Dictionary<uint, int> rows = [];
    /// <summary>
    /// Validate header, record stride and string block, then index rows by ID for repeated
    /// appearance/liquid lookup.
    /// </summary>
    public DbcTable(byte[] bytes)
    {
        data = new(bytes);
        data.Range(0, 20);
        if (Encoding.ASCII.GetString(bytes, 0, 4) != "WDBC")
        {
            throw new InvalidDataException("Expected WDBC.");
        }

        count = data.I(4);
        fields = data.I(8);
        stride = data.I(12);
        if (fields < 1 || stride != fields * 4)
        {
            throw new InvalidDataException("Invalid DBC stride.");
        }

        data.Range(20, count, stride);
        strings = checked(20 + count * stride);
        data.Range(strings, data.I(16));
        for (int i = 0; i < count; i++)
        {
            rows[data.U(20 + i * stride)] = 20 + i * stride;
        }
    }

    /// <summary>
    /// Resolve a field only when both the row and field number exist. Parser callers provide
    /// build-specific field numbers.
    /// </summary>
    int Field(uint id, int field)
    {
        if (field < 0 || field >= fields || !rows.TryGetValue(id, out int p))
        {
            throw new InvalidDataException("Missing DBC record/field.");
        }

        return p + field * 4;
    }

    public bool Contains(uint id) => rows.ContainsKey(id);
    public IEnumerable<uint> Ids => rows.Keys;

    public uint U(uint id, int field) => data.U(Field(id, field));
    public float F(uint id, int field) => data.F(Field(id, field));
    /// <summary>
    /// Resolve a string-block-relative offset and require a valid terminated string.
    /// </summary>
    public string Str(uint id, int field) => data.Str(checked(strings + (int)U(id, field)));
}
