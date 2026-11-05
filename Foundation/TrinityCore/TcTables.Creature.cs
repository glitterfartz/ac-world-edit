namespace ACWorldEdit.Foundation;

// The creature tables of TcTables.
public sealed partial class TcTables
{
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
        ("BaseAttackTime", ColumnKind.UInt, 0),
        ("RangeAttackTime", ColumnKind.UInt, 0),
        ("BaseVariance", ColumnKind.Float, 0),
        ("RangeVariance", ColumnKind.Float, 0),
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
    ], Added: [("BaseAttackTime", "0"), ("RangeAttackTime", "0"), ("BaseVariance", "1"), ("RangeVariance", "1")]);

    public static readonly TableSchema CreatureDifficulty = new("creature_template_difficulty", ["Entry", "DifficultyID"],
    [
        ("Entry", ColumnKind.UInt, 0),
        ("DifficultyID", ColumnKind.Int, 0),
        ("LevelScalingDeltaMin", ColumnKind.Int, 0),
        ("LevelScalingDeltaMax", ColumnKind.Int, 0),
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
    ], Added: [("LevelScalingDeltaMin", "0"), ("LevelScalingDeltaMax", "0")]);

    /// <summary>A creature's gossip menus: one row per menu, so a creature can have several.</summary>
    public static readonly TableSchema CreatureGossip = new("creature_template_gossip", ["CreatureID", "MenuID"], [("CreatureID", ColumnKind.UInt, 0), ("MenuID", ColumnKind.UInt, 0)]);

    /// <summary>A trainer is reached through a gossip option (MenuID, OptionID); 0/0 is the creature's default trainer.</summary>
    public static readonly TableSchema CreatureTrainer = new("creature_trainer", ["CreatureID", "MenuID", "OptionID"], [("CreatureID", ColumnKind.UInt, 0), ("MenuID", ColumnKind.UInt, 0), ("OptionID", ColumnKind.UInt, 0), ("TrainerID", ColumnKind.UInt, 0)]);

    /// <summary>The creature's AI name, a column of creature_template edited on its own so a SmartAI change never rewrites the whole template.</summary>
    public static readonly TableSchema CreatureAI = new("creature_ai", ["entry"], [("entry", ColumnKind.UInt, 0), ("AIName", ColumnKind.Text, 64)], "creature_template");
}
