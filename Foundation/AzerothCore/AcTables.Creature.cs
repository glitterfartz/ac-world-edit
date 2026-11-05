namespace ACWorldEdit.Foundation;

// The creature tables of AcTables.
public sealed partial class AcTables
{
    public static readonly TableSchema CreatureTemplate = new("creature_template", ["entry"], [("entry", ColumnKind.UInt, 0), ("npcflag", ColumnKind.UInt, 0), ("gossip_menu_id", ColumnKind.UInt, 0), ("subname", ColumnKind.Text, 100)]);

    /// <summary>
    /// creature_template.AIName as its own column group: SmartAI scripts only run when it is "SmartAI".
    /// </summary>
    public static readonly TableSchema CreatureAI = new("creature_ai", ["entry"], [("entry", ColumnKind.UInt, 0), ("AIName", ColumnKind.Text, 64)], "creature_template");

    /// <summary>
    /// creature_template level, class and stat scaling as its own column group, so editing stats only
    /// updates these columns. The base values they scale come from creature_classlevelstats.
    /// </summary>
    public static readonly TableSchema CreatureStats = new("creature_stats", ["entry"], [("entry", ColumnKind.UInt, 0), ("minlevel", ColumnKind.UInt, 0), ("maxlevel", ColumnKind.UInt, 0), ("exp", ColumnKind.UInt, 0), ("unit_class", ColumnKind.UInt, 0), ("HealthModifier", ColumnKind.Float, 0), ("ManaModifier", ColumnKind.Float, 0), ("ArmorModifier", ColumnKind.Float, 0), ("DamageModifier", ColumnKind.Float, 0)], "creature_template");

    /// <summary>
    /// Every other editable creature_template column (identity, combat, movement, loot, flags) as one
    /// column group. Excludes the key, VerifiedBuild and the columns owned by the groups above.
    /// </summary>
    public static readonly TableSchema CreatureDetails = new("creature_details", ["entry"], [
        ("entry", ColumnKind.UInt, 0),
        ("name", ColumnKind.Text, 100),
        ("IconName", ColumnKind.Text, 100),
        ("difficulty_entry_1", ColumnKind.UInt, 0),
        ("difficulty_entry_2", ColumnKind.UInt, 0),
        ("difficulty_entry_3", ColumnKind.UInt, 0),
        ("KillCredit1", ColumnKind.UInt, 0),
        ("KillCredit2", ColumnKind.UInt, 0),
        ("faction", ColumnKind.UInt, 0),
        ("speed_walk", ColumnKind.Float, 0),
        ("speed_run", ColumnKind.Float, 0),
        ("speed_swim", ColumnKind.Float, 0),
        ("speed_flight", ColumnKind.Float, 0),
        ("detection_range", ColumnKind.Float, 0),
        ("rank", ColumnKind.UInt, 0),
        ("dmgschool", ColumnKind.Int, 0),
        ("BaseAttackTime", ColumnKind.UInt, 0),
        ("RangeAttackTime", ColumnKind.UInt, 0),
        ("BaseVariance", ColumnKind.Float, 0),
        ("RangeVariance", ColumnKind.Float, 0),
        ("unit_flags", ColumnKind.UInt, 0),
        ("unit_flags2", ColumnKind.UInt, 0),
        ("dynamicflags", ColumnKind.UInt, 0),
        ("family", ColumnKind.Int, 0),
        ("type", ColumnKind.UInt, 0),
        ("type_flags", ColumnKind.UInt, 0),
        ("lootid", ColumnKind.UInt, 0),
        ("pickpocketloot", ColumnKind.UInt, 0),
        ("skinloot", ColumnKind.UInt, 0),
        ("PetSpellDataId", ColumnKind.UInt, 0),
        ("VehicleId", ColumnKind.UInt, 0),
        ("mingold", ColumnKind.UInt, 0),
        ("maxgold", ColumnKind.UInt, 0),
        ("MovementType", ColumnKind.UInt, 0),
        ("HoverHeight", ColumnKind.Float, 0),
        ("ExperienceModifier", ColumnKind.Float, 0),
        ("RacialLeader", ColumnKind.UInt, 0),
        ("movementId", ColumnKind.UInt, 0),
        ("RegenHealth", ColumnKind.UInt, 0),
        ("CreatureImmunitiesId", ColumnKind.Int, 0),
        ("flags_extra", ColumnKind.UInt, 0),
        ("ScriptName", ColumnKind.Text, 64)
    ], "creature_template");
}
