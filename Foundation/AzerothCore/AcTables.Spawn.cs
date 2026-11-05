namespace ACWorldEdit.Foundation;

// The spawn tables of AcTables.
public sealed partial class AcTables
{
    /// <summary>
    /// A creature spawn. npcflag, unit_flags and dynamicflags are NOT NULL here, and 0 means "use the
    /// template's" (ObjectMgr::ChooseCreatureFlags), so a spawn cannot override a flag down to 0.
    /// </summary>
    public static readonly TableSchema SpawnCreature = new("creature", ["guid"], [
        ("guid", ColumnKind.UInt, 0),
        ("id", ColumnKind.UInt, 0),
        ("map", ColumnKind.UInt, 0),
        ("zoneId", ColumnKind.UInt, 0),
        ("areaId", ColumnKind.UInt, 0),
        ("spawnMask", ColumnKind.UInt, 0),
        ("phaseMask", ColumnKind.UInt, 0),
        ("equipment_id", ColumnKind.Int, 0),
        ("position_x", ColumnKind.Float, 0),
        ("position_y", ColumnKind.Float, 0),
        ("position_z", ColumnKind.Float, 0),
        ("orientation", ColumnKind.Float, 0),
        ("spawntimesecs", ColumnKind.UInt, 0),
        ("wander_distance", ColumnKind.Float, 0),
        ("currentwaypoint", ColumnKind.UInt, 0),
        ("curhealth", ColumnKind.UInt, 0),
        ("curmana", ColumnKind.UInt, 0),
        ("MovementType", ColumnKind.UInt, 0),
        ("npcflag", ColumnKind.UInt, 0),
        ("unit_flags", ColumnKind.UInt, 0),
        ("dynamicflags", ColumnKind.UInt, 0),
        ("ScriptName", ColumnKind.Text, 64),
        ("Comment", ColumnKind.Text, 0)
    ]);

    public static readonly TableSchema SpawnGameObject = new("gameobject", ["guid"], [
        ("guid", ColumnKind.UInt, 0),
        ("id", ColumnKind.UInt, 0),
        ("map", ColumnKind.UInt, 0),
        ("zoneId", ColumnKind.UInt, 0),
        ("areaId", ColumnKind.UInt, 0),
        ("spawnMask", ColumnKind.UInt, 0),
        ("phaseMask", ColumnKind.UInt, 0),
        ("position_x", ColumnKind.Float, 0),
        ("position_y", ColumnKind.Float, 0),
        ("position_z", ColumnKind.Float, 0),
        ("orientation", ColumnKind.Float, 0),
        ("rotation0", ColumnKind.Float, 0),
        ("rotation1", ColumnKind.Float, 0),
        ("rotation2", ColumnKind.Float, 0),
        ("rotation3", ColumnKind.Float, 0),
        ("spawntimesecs", ColumnKind.Int, 0),
        ("animprogress", ColumnKind.UInt, 0),
        ("state", ColumnKind.UInt, 0),
        ("ScriptName", ColumnKind.Text, 64),
        ("Comment", ColumnKind.Text, 0)
    ]);

    /// <summary>Per-spawn creature extras; bytes1/bytes2 pack stand state, sheath and PvP flags as the 3.3.5 client reads them.</summary>
    public static readonly TableSchema SpawnCreatureAddon = new("creature_addon", ["guid"], [
        ("guid", ColumnKind.UInt, 0),
        ("path_id", ColumnKind.UInt, 0),
        ("mount", ColumnKind.UInt, 0),
        ("bytes1", ColumnKind.UInt, 0),
        ("bytes2", ColumnKind.UInt, 0),
        ("emote", ColumnKind.UInt, 0),
        ("visibilityDistanceType", ColumnKind.UInt, 0),
        ("auras", ColumnKind.Text, 0)
    ]);

    /// <summary>What every spawn of a creature template looks like (mount, stance, weapons, emote, auras) unless the spawn has its own `creature_addon` row, which replaces this one entirely.</summary>
    public static readonly TableSchema CreatureTemplateAddon = new("creature_template_addon", ["entry"], [
        ("entry", ColumnKind.UInt, 0),
        ("path_id", ColumnKind.UInt, 0),
        ("mount", ColumnKind.UInt, 0),
        ("bytes1", ColumnKind.UInt, 0),
        ("bytes2", ColumnKind.UInt, 0),
        ("emote", ColumnKind.UInt, 0),
        ("visibilityDistanceType", ColumnKind.UInt, 0),
        ("auras", ColumnKind.Text, 0)
    ]);

    public static readonly TableSchema SpawnGameObjectAddon = new("gameobject_addon", ["guid"], [
        ("guid", ColumnKind.UInt, 0),
        ("parent_rotation0", ColumnKind.Float, 0),
        ("parent_rotation1", ColumnKind.Float, 0),
        ("parent_rotation2", ColumnKind.Float, 0),
        ("parent_rotation3", ColumnKind.Float, 0),
        ("invisibilityType", ColumnKind.UInt, 0),
        ("invisibilityValue", ColumnKind.UInt, 0)
    ]);
}
