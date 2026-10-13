using System.Globalization;

namespace ACWorldEdit.Foundation;

public enum ColumnKind
{
    Int,
    UInt,
    Float,
    Text
}

/// <summary>
/// One editable world table: primary key columns plus the whitelisted columns the interaction
/// editor may read and change. Columns outside the list are never captured or edited, so an
/// eventual export updates only these.
/// </summary>
public sealed record TableSchema(string Name, string[] Key, (string Name, ColumnKind Kind, int MaxLength)[] Columns)
{
    public ColumnKind KindOf(string column) => Columns.First(c => c.Name == column).Kind;
    public bool Has(string column) => Columns.Any(c => c.Name == column);
    public string KeyOf(Row row) => string.Join('|', Key.Select(k => row[k]));
}

/// <summary>
/// The interaction tables, with primary keys from the AzerothCore base schema. Text limits follow
/// the column types (varchar/text); 0 means unbounded text.
/// </summary>
public static class InteractionTables
{
    public static readonly TableSchema CreatureTemplate = new("creature_template", ["entry"], [("entry", ColumnKind.UInt, 0), ("npcflag", ColumnKind.UInt, 0), ("gossip_menu_id", ColumnKind.UInt, 0), ("subname", ColumnKind.Text, 100)]);
    public static readonly TableSchema GossipMenu = new("gossip_menu", ["MenuID", "TextID"], [("MenuID", ColumnKind.UInt, 0), ("TextID", ColumnKind.UInt, 0)]);
    public static readonly TableSchema NpcText = new("npc_text", ["ID"], [("ID", ColumnKind.UInt, 0), ("text0_0", ColumnKind.Text, 0), ("text0_1", ColumnKind.Text, 0), ("BroadcastTextID0", ColumnKind.Int, 0), ("Probability0", ColumnKind.Float, 0)]);
    public static readonly TableSchema GossipOption = new("gossip_menu_option", ["MenuID", "OptionID"], [("MenuID", ColumnKind.UInt, 0), ("OptionID", ColumnKind.UInt, 0), ("OptionIcon", ColumnKind.UInt, 0), ("OptionText", ColumnKind.Text, 0), ("OptionBroadcastTextID", ColumnKind.Int, 0), ("OptionType", ColumnKind.UInt, 0), ("OptionNpcFlag", ColumnKind.UInt, 0), ("ActionMenuID", ColumnKind.UInt, 0), ("ActionPoiID", ColumnKind.UInt, 0), ("BoxCoded", ColumnKind.UInt, 0), ("BoxMoney", ColumnKind.UInt, 0), ("BoxText", ColumnKind.Text, 0)]);
    public static readonly TableSchema QuestStarter = new("creature_queststarter", ["id", "quest"], [("id", ColumnKind.UInt, 0), ("quest", ColumnKind.UInt, 0)]);
    public static readonly TableSchema QuestEnder = new("creature_questender", ["id", "quest"], [("id", ColumnKind.UInt, 0), ("quest", ColumnKind.UInt, 0)]);
    public static readonly TableSchema Vendor = new("npc_vendor", ["entry", "item", "ExtendedCost"], [("entry", ColumnKind.UInt, 0), ("item", ColumnKind.Int, 0), ("ExtendedCost", ColumnKind.UInt, 0), ("slot", ColumnKind.Int, 0), ("maxcount", ColumnKind.UInt, 0), ("incrtime", ColumnKind.UInt, 0)]);
    public static readonly TableSchema DefaultTrainer = new("creature_default_trainer", ["CreatureId"], [("CreatureId", ColumnKind.UInt, 0), ("TrainerId", ColumnKind.UInt, 0)]);
    public static readonly TableSchema Trainer = new("trainer", ["Id"], [("Id", ColumnKind.UInt, 0), ("Type", ColumnKind.UInt, 0), ("Requirement", ColumnKind.UInt, 0), ("Greeting", ColumnKind.Text, 0)]);
    public static readonly TableSchema TrainerSpell = new("trainer_spell", ["TrainerId", "SpellId"], [("TrainerId", ColumnKind.UInt, 0), ("SpellId", ColumnKind.UInt, 0), ("MoneyCost", ColumnKind.UInt, 0), ("ReqSkillLine", ColumnKind.UInt, 0), ("ReqSkillRank", ColumnKind.UInt, 0), ("ReqLevel", ColumnKind.UInt, 0)]);
    public static readonly TableSchema SpellClick = new("npc_spellclick_spells", ["npc_entry", "spell_id"], [("npc_entry", ColumnKind.UInt, 0), ("spell_id", ColumnKind.UInt, 0), ("cast_flags", ColumnKind.UInt, 0), ("user_type", ColumnKind.UInt, 0)]);

    public static readonly TableSchema[] All = [CreatureTemplate, GossipMenu, NpcText, GossipOption, QuestStarter, QuestEnder, Vendor, DefaultTrainer, Trainer, TrainerSpell, SpellClick];

    public static TableSchema Find(string name) => All.FirstOrDefault(t => t.Name == name) ?? throw new InvalidDataException($"Table {name} is not editable.");
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
