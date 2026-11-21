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

    /// <summary>
    /// Read field <paramref name="field"/> of the row with ID <paramref name="id"/> as a uint32.
    /// </summary>
    public uint U(uint id, int field) => data.U(Field(id, field));
    /// <summary>
    /// Read field <paramref name="field"/> of the row with ID <paramref name="id"/> as a finite float.
    /// </summary>
    public float F(uint id, int field) => data.F(Field(id, field));
    /// <summary>
    /// Read a string field: the field holds an offset into the trailing string block, and the
    /// string there must be NUL-terminated.
    /// </summary>
    public string Str(uint id, int field) => data.Str(checked(strings + (int)U(id, field)));

    /// <summary>
    /// File-order row position for an ID, independent of its ID value. LightIntBand/LightFloatBand
    /// associate with a LightParams row by file position (18 contiguous rows per entry), not by ID;
    /// both tables carry small ID gaps from deleted dev rows, confirmed against the Wrath 3.3.5a
    /// client (15300 LightIntBand rows == 850 LightParams rows * 18).
    /// </summary>
    public int Position(uint id) => rows.TryGetValue(id, out int p) ? (p - 20) / stride : throw new InvalidDataException("Missing DBC record.");
    /// <summary>
    /// Field access by raw file-order row position rather than ID lookup.
    /// </summary>
    public uint UAt(int row, int field)
    {
        if (row < 0 || row >= count || field < 0 || field >= fields)
        {
            throw new InvalidDataException("DBC row/field out of range.");
        }

        return data.U(20 + row * stride + field * 4);
    }
}
