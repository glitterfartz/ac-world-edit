using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>
/// How a column's value is validated and written. Long and ULong are 64-bit (TrinityCore's bigint
/// guids, npcflag and money columns); new kinds are appended so existing values keep their numbers.
/// </summary>
public enum ColumnKind
{
    Int,
    UInt,
    Float,
    Text,
    Long,
    ULong
}

/// <summary>
/// One editable world table: primary key columns plus the whitelisted columns the content editors
/// may read and change. Columns outside the list are never captured or edited, so an eventual
/// export updates only these. Name is the logical draft table; SqlName the physical table (they
/// differ when one physical table is split into independent column groups, e.g. creature_ai).
/// </summary>
public sealed record TableSchema(string Name, string[] Key, (string Name, ColumnKind Kind, int MaxLength)[] Columns, string? Sql = null, (string Column, string Default)[]? Added = null)
{
    public string SqlName => Sql ?? Name;

    /// <summary>A saved row that predates <see cref="Added"/> columns, with those columns set to their defaults; other rows are returned as they are.</summary>
    public Row Upgrade(Row row) => Added == null ? row : Added.Where(a => !row.Values.ContainsKey(a.Column)).Aggregate(row, (current, a) => current.With(a.Column, a.Default));

    public bool Has(string column) => Columns.Any(c => c.Name == column);
    public string KeyOf(Row row) => string.Join('|', Key.Select(k => row[k]));
}

/// <summary>
/// One table row as invariant-culture strings keyed by column. Value equality, sorted columns and
/// string storage keep drafts stable in JSON and comparable with database rows.
/// </summary>
public sealed class Row : IEquatable<Row>
{
    public SortedDictionary<string, string> Values { get; init; } = new(StringComparer.Ordinal);

    public Row()
    {
    }

    public Row(IEnumerable<KeyValuePair<string, string>> values)
    {
        foreach (var (key, value) in values)
        {
            Values[key] = value;
        }
    }

    public string this[string column] => Values.TryGetValue(column, out var value) ? value : throw new InvalidDataException($"Missing column {column}.");
    public uint U(string column) => uint.Parse(this[column], CultureInfo.InvariantCulture);
    public int I(string column) => int.Parse(this[column], CultureInfo.InvariantCulture);
    public long L(string column) => long.Parse(this[column], CultureInfo.InvariantCulture);
    public ulong UL(string column) => ulong.Parse(this[column], CultureInfo.InvariantCulture);
    public float F(string column) => float.Parse(this[column], CultureInfo.InvariantCulture);
    public string T(string column) => this[column];

    /// <summary>
    /// Copy with one column replaced; numbers are stored in invariant form.
    /// </summary>
    public Row With(string column, object value)
    {
        var copy = new Row(Values);
        copy.Values[column] = Format(value);
        return copy;
    }

    public static string Format(object value) => value switch
    {
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    public static Row Of(params (string Column, object Value)[] values) => new(values.Select(v => new KeyValuePair<string, string>(v.Column, Format(v.Value))));

    /// <summary>
    /// Require exactly the schema's columns with parseable values and text within its limit.
    /// </summary>
    public void Validate(TableSchema schema)
    {
        if (Values.Count != schema.Columns.Length || Values.Keys.Any(k => !schema.Has(k)))
        {
            throw new InvalidDataException($"{schema.Name}: row columns must be exactly {string.Join(", ", schema.Columns.Select(c => c.Name))}.");
        }

        foreach (var (name, kind, max) in schema.Columns)
        {
            string value = this[name];
            bool ok = kind switch
            {
                ColumnKind.Int => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
                ColumnKind.UInt => uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _),
                ColumnKind.Long => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
                ColumnKind.ULong => ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _),
                ColumnKind.Float => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && float.IsFinite(f),
                _ => max == 0 || value.Length <= max
            };
            if (!ok)
            {
                throw new InvalidDataException(kind == ColumnKind.Text ? $"{schema.Name}.{name} must be at most {max} characters." : $"{schema.Name}.{name} must be a valid {kind.ToString().ToLowerInvariant()} number.");
            }
        }
    }

    public bool Equals(Row? other) => other is not null && Values.Count == other.Values.Count && Values.All(v => other.Values.TryGetValue(v.Key, out var o) && o == v.Value);
    public override bool Equals(object? obj) => Equals(obj as Row);
    public override int GetHashCode() => Values.Aggregate(17, (h, v) => HashCode.Combine(h, v.Key, v.Value));
    public static bool operator ==(Row? a, Row? b) => a is null ? b is null : a.Equals(b);
    public static bool operator !=(Row? a, Row? b) => !(a == b);
}

/// <summary>
/// Rows grouped by table and primary key. Used for the database snapshot and for the effective
/// (snapshot + draft) view; copies are cheap enough for one NPC's interaction data.
/// </summary>
public sealed class RowSet
{
    readonly Dictionary<string, Dictionary<string, Row>> tables = [];

    public IReadOnlyCollection<Row> All(TableSchema table) => tables.TryGetValue(table.Name, out var rows) ? rows.Values : [];
    public Row? Get(TableSchema table, string key) => tables.TryGetValue(table.Name, out var rows) && rows.TryGetValue(key, out var row) ? row : null;

    /// <summary>
    /// Layer one edit on top: delete when After is null, otherwise insert or replace.
    /// </summary>
    public void Apply(RowEdit edit, ITableCatalog? tables = null)
    {
        var table = (tables ?? AcTables.Instance).Find(edit.Table);
        if (edit.After == null)
        {
            Remove(table, edit.Key);
        }
        else
        {
            Set(table, edit.After);
        }
    }

    public void Set(TableSchema table, Row row)
    {
        if (!tables.TryGetValue(table.Name, out var rows))
        {
            tables[table.Name] = rows = [];
        }

        rows[table.KeyOf(row)] = row;
    }

    public void Remove(TableSchema table, string key)
    {
        if (tables.TryGetValue(table.Name, out var rows))
        {
            rows.Remove(key);
        }
    }

    public RowSet Clone()
    {
        var copy = new RowSet();
        foreach (var (name, rows) in tables)
        {
            copy.tables[name] = new Dictionary<string, Row>(rows);
        }

        return copy;
    }
}
