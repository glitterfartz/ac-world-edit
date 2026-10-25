namespace ACWorldEdit.Foundation;

/// <summary>
/// The TrinityCore master creature tables the creature editor reads, with primary keys and column
/// types from TDB 1210.26091. The editor is read-only for TrinityCore so far, so no drafts are made
/// against these yet; quest, SmartAI and waypoint tables are not listed and stay uneditable. Text
/// limits follow the column types (varchar); 0 means unbounded (mediumtext).
/// </summary>
public sealed class TcTables : ITableCatalog
{
    public static readonly TcTables Instance = new();

    /// <summary>The template's descriptive and behavioural columns; per-difficulty data lives in <see cref="CreatureDifficulty"/>.</summary>
    public static readonly TableSchema CreatureTemplate = new("creature_template", ["entry"],
    [
        ("entry", ColumnKind.UInt, 0),
        ("name", ColumnKind.Text, 0),
        ("subname", ColumnKind.Text, 0),
        ("IconName", ColumnKind.Text, 64),
        ("npcflag", ColumnKind.ULong, 0),
        ("faction", ColumnKind.UInt, 0),
        ("Classification", ColumnKind.UInt, 0),
        ("unit_class", ColumnKind.UInt, 0),
        ("type", ColumnKind.UInt, 0),
        ("family", ColumnKind.Int, 0),
        ("trainer_class", ColumnKind.UInt, 0),
        ("unit_flags", ColumnKind.UInt, 0),
        ("unit_flags2", ColumnKind.UInt, 0),
        ("unit_flags3", ColumnKind.UInt, 0),
        ("speed_walk", ColumnKind.Float, 0),
        ("speed_run", ColumnKind.Float, 0),
        ("scale", ColumnKind.Float, 0),
        ("RequiredExpansion", ColumnKind.Int, 0),
        ("VehicleId", ColumnKind.UInt, 0),
        ("MovementType", ColumnKind.UInt, 0),
        ("AIName", ColumnKind.Text, 64),
        ("ScriptName", ColumnKind.Text, 64),
        ("flags_extra", ColumnKind.UInt, 0)
    ]);

    public static readonly TableSchema CreatureDifficulty = new("creature_template_difficulty", ["Entry", "DifficultyID"],
    [
        ("Entry", ColumnKind.UInt, 0),
        ("DifficultyID", ColumnKind.Int, 0),
        ("ContentTuningID", ColumnKind.Int, 0),
        ("HealthScalingExpansion", ColumnKind.Int, 0),
        ("HealthModifier", ColumnKind.Float, 0),
        ("ManaModifier", ColumnKind.Float, 0),
        ("ArmorModifier", ColumnKind.Float, 0),
        ("DamageModifier", ColumnKind.Float, 0),
        ("CreatureDifficultyID", ColumnKind.Int, 0),
        ("TypeFlags", ColumnKind.UInt, 0),
        ("TypeFlags2", ColumnKind.UInt, 0),
        ("TypeFlags3", ColumnKind.UInt, 0),
        ("LootID", ColumnKind.UInt, 0),
        ("PickPocketLootID", ColumnKind.UInt, 0),
        ("SkinLootID", ColumnKind.UInt, 0),
        ("GoldMin", ColumnKind.UInt, 0),
        ("GoldMax", ColumnKind.UInt, 0),
        ("StaticFlags1", ColumnKind.UInt, 0),
        ("StaticFlags2", ColumnKind.UInt, 0),
        ("StaticFlags3", ColumnKind.UInt, 0),
        ("StaticFlags4", ColumnKind.UInt, 0),
        ("StaticFlags5", ColumnKind.UInt, 0),
        ("StaticFlags6", ColumnKind.UInt, 0),
        ("StaticFlags7", ColumnKind.UInt, 0),
        ("StaticFlags8", ColumnKind.UInt, 0)
    ]);

    /// <summary>A creature's gossip menus: one row per menu, so a creature can have several.</summary>
    public static readonly TableSchema CreatureGossip = new("creature_template_gossip", ["CreatureID", "MenuID"], [("CreatureID", ColumnKind.UInt, 0), ("MenuID", ColumnKind.UInt, 0)]);
    public static readonly TableSchema GossipMenu = new("gossip_menu", ["MenuID", "TextID"], [("MenuID", ColumnKind.UInt, 0), ("TextID", ColumnKind.UInt, 0)]);

    /// <summary>npc_text holds only broadcast text IDs and their weights; the text itself is BroadcastText.</summary>
    public static readonly TableSchema NpcText = new("npc_text", ["ID"],
        [("ID", ColumnKind.UInt, 0), .. Enumerable.Range(0, 8).SelectMany(i => new[] { ($"BroadcastTextID{i}", ColumnKind.UInt, 0), ($"Probability{i}", ColumnKind.Float, 0) })]);

    public static readonly TableSchema GossipOption = new("gossip_menu_option", ["MenuID", "OptionID"],
    [
        ("MenuID", ColumnKind.UInt, 0),
        ("OptionID", ColumnKind.UInt, 0),
        ("GossipOptionID", ColumnKind.Int, 0),
        ("OptionNpc", ColumnKind.UInt, 0),
        ("OptionText", ColumnKind.Text, 0),
        ("OptionBroadcastTextID", ColumnKind.UInt, 0),
        ("Language", ColumnKind.UInt, 0),
        ("Flags", ColumnKind.Int, 0),
        ("ActionMenuID", ColumnKind.UInt, 0),
        ("ActionPoiID", ColumnKind.UInt, 0),
        ("GossipNpcOptionID", ColumnKind.Int, 0),
        ("BoxCoded", ColumnKind.UInt, 0),
        ("BoxMoney", ColumnKind.ULong, 0),
        ("BoxText", ColumnKind.Text, 0),
        ("BoxBroadcastTextID", ColumnKind.UInt, 0),
        ("SpellID", ColumnKind.Int, 0),
        ("OverrideIconID", ColumnKind.Int, 0)
    ]);

    /// <summary>A trainer is reached through a gossip option (MenuID, OptionID); 0/0 is the creature's default trainer.</summary>
    public static readonly TableSchema CreatureTrainer = new("creature_trainer", ["CreatureID", "MenuID", "OptionID"], [("CreatureID", ColumnKind.UInt, 0), ("MenuID", ColumnKind.UInt, 0), ("OptionID", ColumnKind.UInt, 0), ("TrainerID", ColumnKind.UInt, 0)]);
    public static readonly TableSchema Trainer = new("trainer", ["Id"], [("Id", ColumnKind.UInt, 0), ("Type", ColumnKind.UInt, 0), ("Greeting", ColumnKind.Text, 0)]);
    public static readonly TableSchema TrainerSpell = new("trainer_spell", ["TrainerId", "SpellId"],
    [
        ("TrainerId", ColumnKind.UInt, 0),
        ("SpellId", ColumnKind.UInt, 0),
        ("MoneyCost", ColumnKind.UInt, 0),
        ("ReqSkillLine", ColumnKind.UInt, 0),
        ("ReqSkillRank", ColumnKind.UInt, 0),
        ("ReqAbility1", ColumnKind.UInt, 0),
        ("ReqAbility2", ColumnKind.UInt, 0),
        ("ReqAbility3", ColumnKind.UInt, 0),
        ("ReqLevel", ColumnKind.UInt, 0)
    ]);

    /// <summary>The item type (1 item, 2 currency) is part of the key.</summary>
    public static readonly TableSchema Vendor = new("npc_vendor", ["entry", "item", "ExtendedCost", "type"],
    [
        ("entry", ColumnKind.UInt, 0),
        ("item", ColumnKind.Int, 0),
        ("ExtendedCost", ColumnKind.UInt, 0),
        ("type", ColumnKind.UInt, 0),
        ("slot", ColumnKind.Int, 0),
        ("maxcount", ColumnKind.UInt, 0),
        ("incrtime", ColumnKind.UInt, 0),
        ("BonusListIDs", ColumnKind.Text, 0),
        ("PlayerConditionID", ColumnKind.UInt, 0),
        ("IgnoreFiltering", ColumnKind.UInt, 0)
    ]);

    public static readonly TableSchema QuestStarter = new("creature_queststarter", ["id", "quest"], [("id", ColumnKind.UInt, 0), ("quest", ColumnKind.UInt, 0)]);
    public static readonly TableSchema QuestEnder = new("creature_questender", ["id", "quest"], [("id", ColumnKind.UInt, 0), ("quest", ColumnKind.UInt, 0)]);
    public static readonly TableSchema SpellClick = new("npc_spellclick_spells", ["npc_entry", "spell_id"], [("npc_entry", ColumnKind.UInt, 0), ("spell_id", ColumnKind.UInt, 0), ("cast_flags", ColumnKind.UInt, 0), ("user_type", ColumnKind.UInt, 0)]);

    public static readonly TableSchema[] Tables = [CreatureTemplate, CreatureDifficulty, CreatureGossip, GossipMenu, NpcText, GossipOption, CreatureTrainer, Trainer, TrainerSpell, Vendor, QuestStarter, QuestEnder, SpellClick];

    public IReadOnlyList<TableSchema> All => Tables;

    public TableSchema Find(string name) => Tables.FirstOrDefault(t => t.Name == name) ?? throw new InvalidDataException($"Table {name} is not editable for the TrinityCore world database yet.");
}
