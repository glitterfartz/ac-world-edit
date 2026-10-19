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
/// One editable world table: primary key columns plus the whitelisted columns the content editors
/// may read and change. Columns outside the list are never captured or edited, so an eventual
/// export updates only these. Name is the logical draft table; SqlName the physical table (they
/// differ when one physical table is split into independent column groups, e.g. creature_ai).
/// </summary>
public sealed record TableSchema(string Name, string[] Key, (string Name, ColumnKind Kind, int MaxLength)[] Columns, string? Sql = null)
{
    public string SqlName => Sql ?? Name;

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

    /// <summary>
    /// creature_template.AIName as its own column group: SmartAI scripts only run when it is "SmartAI".
    /// </summary>
    public static readonly TableSchema CreatureAI = new("creature_ai", ["entry"], [("entry", ColumnKind.UInt, 0), ("AIName", ColumnKind.Text, 64)], "creature_template");

    /// <summary>
    /// creature_template level, class and stat scaling as its own column group, so editing stats only
    /// updates these columns. The base values they scale come from creature_classlevelstats.
    /// </summary>
    public static readonly TableSchema CreatureStats = new("creature_stats", ["entry"], [("entry", ColumnKind.UInt, 0), ("minlevel", ColumnKind.UInt, 0), ("maxlevel", ColumnKind.UInt, 0), ("exp", ColumnKind.UInt, 0), ("unit_class", ColumnKind.UInt, 0), ("HealthModifier", ColumnKind.Float, 0), ("ManaModifier", ColumnKind.Float, 0), ("ArmorModifier", ColumnKind.Float, 0), ("DamageModifier", ColumnKind.Float, 0)], "creature_template");

    // Quest tables: every column except VerifiedBuild (build stamp, never edited).
    public static readonly TableSchema QuestTemplate = new("quest_template", ["ID"], [
        ("ID", ColumnKind.UInt, 0),
        ("QuestType", ColumnKind.UInt, 0),
        ("QuestLevel", ColumnKind.Int, 0),
        ("MinLevel", ColumnKind.UInt, 0),
        ("QuestSortID", ColumnKind.Int, 0),
        ("QuestInfoID", ColumnKind.UInt, 0),
        ("SuggestedGroupNum", ColumnKind.UInt, 0),
        ("RequiredFactionId1", ColumnKind.UInt, 0),
        ("RequiredFactionId2", ColumnKind.UInt, 0),
        ("RequiredFactionValue1", ColumnKind.Int, 0),
        ("RequiredFactionValue2", ColumnKind.Int, 0),
        ("RewardNextQuest", ColumnKind.UInt, 0),
        ("RewardXPDifficulty", ColumnKind.UInt, 0),
        ("RewardMoney", ColumnKind.Int, 0),
        ("RewardMoneyDifficulty", ColumnKind.UInt, 0),
        ("RewardDisplaySpell", ColumnKind.UInt, 0),
        ("RewardSpell", ColumnKind.Int, 0),
        ("RewardHonor", ColumnKind.Int, 0),
        ("RewardKillHonor", ColumnKind.Float, 0),
        ("StartItem", ColumnKind.UInt, 0),
        ("Flags", ColumnKind.UInt, 0),
        ("RequiredPlayerKills", ColumnKind.UInt, 0),
        ("RewardItem1", ColumnKind.UInt, 0),
        ("RewardAmount1", ColumnKind.UInt, 0),
        ("RewardItem2", ColumnKind.UInt, 0),
        ("RewardAmount2", ColumnKind.UInt, 0),
        ("RewardItem3", ColumnKind.UInt, 0),
        ("RewardAmount3", ColumnKind.UInt, 0),
        ("RewardItem4", ColumnKind.UInt, 0),
        ("RewardAmount4", ColumnKind.UInt, 0),
        ("ItemDrop1", ColumnKind.UInt, 0),
        ("ItemDropQuantity1", ColumnKind.UInt, 0),
        ("ItemDrop2", ColumnKind.UInt, 0),
        ("ItemDropQuantity2", ColumnKind.UInt, 0),
        ("ItemDrop3", ColumnKind.UInt, 0),
        ("ItemDropQuantity3", ColumnKind.UInt, 0),
        ("ItemDrop4", ColumnKind.UInt, 0),
        ("ItemDropQuantity4", ColumnKind.UInt, 0),
        ("RewardChoiceItemID1", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity1", ColumnKind.UInt, 0),
        ("RewardChoiceItemID2", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity2", ColumnKind.UInt, 0),
        ("RewardChoiceItemID3", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity3", ColumnKind.UInt, 0),
        ("RewardChoiceItemID4", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity4", ColumnKind.UInt, 0),
        ("RewardChoiceItemID5", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity5", ColumnKind.UInt, 0),
        ("RewardChoiceItemID6", ColumnKind.UInt, 0),
        ("RewardChoiceItemQuantity6", ColumnKind.UInt, 0),
        ("POIContinent", ColumnKind.UInt, 0),
        ("POIx", ColumnKind.Float, 0),
        ("POIy", ColumnKind.Float, 0),
        ("POIPriority", ColumnKind.UInt, 0),
        ("RewardTitle", ColumnKind.UInt, 0),
        ("RewardTalents", ColumnKind.UInt, 0),
        ("RewardArenaPoints", ColumnKind.UInt, 0),
        ("RewardFactionID1", ColumnKind.UInt, 0),
        ("RewardFactionValue1", ColumnKind.Int, 0),
        ("RewardFactionOverride1", ColumnKind.Int, 0),
        ("RewardFactionID2", ColumnKind.UInt, 0),
        ("RewardFactionValue2", ColumnKind.Int, 0),
        ("RewardFactionOverride2", ColumnKind.Int, 0),
        ("RewardFactionID3", ColumnKind.UInt, 0),
        ("RewardFactionValue3", ColumnKind.Int, 0),
        ("RewardFactionOverride3", ColumnKind.Int, 0),
        ("RewardFactionID4", ColumnKind.UInt, 0),
        ("RewardFactionValue4", ColumnKind.Int, 0),
        ("RewardFactionOverride4", ColumnKind.Int, 0),
        ("RewardFactionID5", ColumnKind.UInt, 0),
        ("RewardFactionValue5", ColumnKind.Int, 0),
        ("RewardFactionOverride5", ColumnKind.Int, 0),
        ("TimeAllowed", ColumnKind.UInt, 0),
        ("AllowableRaces", ColumnKind.UInt, 0),
        ("LogTitle", ColumnKind.Text, 0),
        ("LogDescription", ColumnKind.Text, 0),
        ("QuestDescription", ColumnKind.Text, 0),
        ("AreaDescription", ColumnKind.Text, 0),
        ("QuestCompletionLog", ColumnKind.Text, 0),
        ("RequiredNpcOrGo1", ColumnKind.Int, 0),
        ("RequiredNpcOrGo2", ColumnKind.Int, 0),
        ("RequiredNpcOrGo3", ColumnKind.Int, 0),
        ("RequiredNpcOrGo4", ColumnKind.Int, 0),
        ("RequiredNpcOrGoCount1", ColumnKind.UInt, 0),
        ("RequiredNpcOrGoCount2", ColumnKind.UInt, 0),
        ("RequiredNpcOrGoCount3", ColumnKind.UInt, 0),
        ("RequiredNpcOrGoCount4", ColumnKind.UInt, 0),
        ("RequiredItemId1", ColumnKind.UInt, 0),
        ("RequiredItemId2", ColumnKind.UInt, 0),
        ("RequiredItemId3", ColumnKind.UInt, 0),
        ("RequiredItemId4", ColumnKind.UInt, 0),
        ("RequiredItemId5", ColumnKind.UInt, 0),
        ("RequiredItemId6", ColumnKind.UInt, 0),
        ("RequiredItemCount1", ColumnKind.UInt, 0),
        ("RequiredItemCount2", ColumnKind.UInt, 0),
        ("RequiredItemCount3", ColumnKind.UInt, 0),
        ("RequiredItemCount4", ColumnKind.UInt, 0),
        ("RequiredItemCount5", ColumnKind.UInt, 0),
        ("RequiredItemCount6", ColumnKind.UInt, 0),
        ("Unknown0", ColumnKind.UInt, 0),
        ("ObjectiveText1", ColumnKind.Text, 0),
        ("ObjectiveText2", ColumnKind.Text, 0),
        ("ObjectiveText3", ColumnKind.Text, 0),
        ("ObjectiveText4", ColumnKind.Text, 0)
    ]);
    public static readonly TableSchema QuestAddon = new("quest_template_addon", ["ID"], [
        ("ID", ColumnKind.UInt, 0),
        ("MaxLevel", ColumnKind.UInt, 0),
        ("AllowableClasses", ColumnKind.UInt, 0),
        ("SourceSpellID", ColumnKind.UInt, 0),
        ("PrevQuestID", ColumnKind.Int, 0),
        ("NextQuestID", ColumnKind.UInt, 0),
        ("ExclusiveGroup", ColumnKind.Int, 0),
        ("BreadcrumbForQuestId", ColumnKind.UInt, 0),
        ("RewardMailTemplateID", ColumnKind.UInt, 0),
        ("RewardMailDelay", ColumnKind.UInt, 0),
        ("RequiredSkillID", ColumnKind.UInt, 0),
        ("RequiredSkillPoints", ColumnKind.UInt, 0),
        ("RequiredMinRepFaction", ColumnKind.UInt, 0),
        ("RequiredMaxRepFaction", ColumnKind.UInt, 0),
        ("RequiredMinRepValue", ColumnKind.Int, 0),
        ("RequiredMaxRepValue", ColumnKind.Int, 0),
        ("ProvidedItemCount", ColumnKind.UInt, 0),
        ("SpecialFlags", ColumnKind.UInt, 0)
    ]);
    public static readonly TableSchema QuestRequestItems = new("quest_request_items", ["ID"], [
        ("ID", ColumnKind.UInt, 0),
        ("EmoteOnComplete", ColumnKind.UInt, 0),
        ("EmoteOnIncomplete", ColumnKind.UInt, 0),
        ("CompletionText", ColumnKind.Text, 0)
    ]);
    public static readonly TableSchema QuestOfferReward = new("quest_offer_reward", ["ID"], [
        ("ID", ColumnKind.UInt, 0),
        ("Emote1", ColumnKind.UInt, 0),
        ("Emote2", ColumnKind.UInt, 0),
        ("Emote3", ColumnKind.UInt, 0),
        ("Emote4", ColumnKind.UInt, 0),
        ("EmoteDelay1", ColumnKind.UInt, 0),
        ("EmoteDelay2", ColumnKind.UInt, 0),
        ("EmoteDelay3", ColumnKind.UInt, 0),
        ("EmoteDelay4", ColumnKind.UInt, 0),
        ("RewardText", ColumnKind.Text, 0)
    ]);
    public static readonly TableSchema SmartScripts = new("smart_scripts", ["entryorguid", "source_type", "id", "link"], [
        ("entryorguid", ColumnKind.Int, 0),
        ("source_type", ColumnKind.UInt, 0),
        ("id", ColumnKind.UInt, 0),
        ("link", ColumnKind.UInt, 0),
        ("event_type", ColumnKind.UInt, 0),
        ("event_phase_mask", ColumnKind.UInt, 0),
        ("event_chance", ColumnKind.UInt, 0),
        ("event_flags", ColumnKind.UInt, 0),
        ("event_param1", ColumnKind.UInt, 0),
        ("event_param2", ColumnKind.UInt, 0),
        ("event_param3", ColumnKind.UInt, 0),
        ("event_param4", ColumnKind.UInt, 0),
        ("event_param5", ColumnKind.UInt, 0),
        ("event_param6", ColumnKind.UInt, 0),
        ("action_type", ColumnKind.UInt, 0),
        ("action_param1", ColumnKind.UInt, 0),
        ("action_param2", ColumnKind.UInt, 0),
        ("action_param3", ColumnKind.UInt, 0),
        ("action_param4", ColumnKind.UInt, 0),
        ("action_param5", ColumnKind.UInt, 0),
        ("action_param6", ColumnKind.UInt, 0),
        ("target_type", ColumnKind.UInt, 0),
        ("target_param1", ColumnKind.UInt, 0),
        ("target_param2", ColumnKind.UInt, 0),
        ("target_param3", ColumnKind.UInt, 0),
        ("target_param4", ColumnKind.UInt, 0),
        ("target_x", ColumnKind.Float, 0),
        ("target_y", ColumnKind.Float, 0),
        ("target_z", ColumnKind.Float, 0),
        ("target_o", ColumnKind.Float, 0),
        ("comment", ColumnKind.Text, 0)
    ]);

    /// <summary>
    /// Lines a creature can say through SmartAI "Talk" (grouped by GroupID; the server picks one
    /// line of a group by Probability).
    /// </summary>
    public static readonly TableSchema CreatureText = new("creature_text", ["CreatureID", "GroupID", "ID"], [
        ("CreatureID", ColumnKind.UInt, 0),
        ("GroupID", ColumnKind.UInt, 0),
        ("ID", ColumnKind.UInt, 0),
        ("Text", ColumnKind.Text, 0),
        ("Type", ColumnKind.UInt, 0),
        ("Language", ColumnKind.Int, 0),
        ("Probability", ColumnKind.Float, 0),
        ("Emote", ColumnKind.UInt, 0),
        ("Duration", ColumnKind.UInt, 0),
        ("Sound", ColumnKind.UInt, 0),
        ("BroadcastTextId", ColumnKind.Int, 0),
        ("TextRange", ColumnKind.UInt, 0),
        ("comment", ColumnKind.Text, 255)
    ]);

    /// <summary>
    /// The script an AreaTrigger runs (ObjectMgr::LoadAreaTriggerScripts): a C++ AreaTriggerScript
    /// name, or "SmartTrigger" for its smart_scripts rows (source_type 2). One name per trigger.
    /// </summary>
    public static readonly TableSchema AreaTriggerScripts = new("areatrigger_scripts", ["entry"], [
        ("entry", ColumnKind.Int, 0),
        ("ScriptName", ColumnKind.Text, 64)
    ]);

    public static readonly TableSchema[] All = [CreatureTemplate, GossipMenu, NpcText, GossipOption, QuestStarter, QuestEnder, Vendor, DefaultTrainer, Trainer, TrainerSpell, SpellClick, CreatureAI, CreatureStats, QuestTemplate, QuestAddon, QuestRequestItems, QuestOfferReward, SmartScripts, CreatureText, AreaTriggerScripts];

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
    public long L(string column) => long.Parse(this[column], CultureInfo.InvariantCulture);
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

    /// <summary>
    /// Layer one edit on top: delete when After is null, otherwise insert or replace.
    /// </summary>
    public void Apply(RowEdit edit)
    {
        var table = InteractionTables.Find(edit.Table);
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
