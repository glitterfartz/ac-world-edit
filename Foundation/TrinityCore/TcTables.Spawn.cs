namespace ACWorldEdit.Foundation;

// The spawn tables of TcTables.
public sealed partial class TcTables
{
    /// <summary>
    /// A creature spawn. The flag and health columns are nullable: NULL inherits the template's value
    /// (a spawn only overrides what it sets), which is the prefab/instance relationship the spawn
    /// inspector shows. zoneId/areaId are a cache the server can recompute and are written as 0.
    /// </summary>
    public static readonly TableSchema SpawnCreature = new("creature", ["guid"],
    [
        ("guid", ColumnKind.ULong, 0),
        ("id", ColumnKind.UInt, 0),
        ("map", ColumnKind.UInt, 0),
        ("zoneId", ColumnKind.UInt, 0),
        ("areaId", ColumnKind.UInt, 0),
        ("spawnDifficulties", ColumnKind.Text, 100),
        ("phaseUseFlags", ColumnKind.UInt, 0),
        ("PhaseId", ColumnKind.Int, 0),
        ("PhaseGroup", ColumnKind.Int, 0),
        ("terrainSwapMap", ColumnKind.Int, 0),
        ("modelid", ColumnKind.UInt, 0),
        ("equipment_id", ColumnKind.Int, 0),
        ("position_x", ColumnKind.Float, 0),
        ("position_y", ColumnKind.Float, 0),
        ("position_z", ColumnKind.Float, 0),
        ("orientation", ColumnKind.Float, 0),
        ("spawntimesecs", ColumnKind.UInt, 0),
        ("wander_distance", ColumnKind.Float, 0),
        ("currentwaypoint", ColumnKind.UInt, 0),
        ("curHealthPct", ColumnKind.UInt, 0),
        ("MovementType", ColumnKind.UInt, 0),
        ("npcflag", ColumnKind.ULong, 0),
        ("unit_flags", ColumnKind.UInt, 0),
        ("unit_flags2", ColumnKind.UInt, 0),
        ("unit_flags3", ColumnKind.UInt, 0),
        ("ScriptName", ColumnKind.Text, 64),
        ("StringId", ColumnKind.Text, 64)
    ], Nullable: ["PhaseId", "PhaseGroup", "curHealthPct", "npcflag", "unit_flags", "unit_flags2", "unit_flags3", "StringId"]);

    /// <summary>A GameObject spawn; the rotation is the stored quaternion.</summary>
    public static readonly TableSchema SpawnGameObject = new("gameobject", ["guid"],
    [
        ("guid", ColumnKind.ULong, 0),
        ("id", ColumnKind.UInt, 0),
        ("map", ColumnKind.UInt, 0),
        ("zoneId", ColumnKind.UInt, 0),
        ("areaId", ColumnKind.UInt, 0),
        ("spawnDifficulties", ColumnKind.Text, 100),
        ("phaseUseFlags", ColumnKind.UInt, 0),
        ("PhaseId", ColumnKind.Int, 0),
        ("PhaseGroup", ColumnKind.Int, 0),
        ("terrainSwapMap", ColumnKind.Int, 0),
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
        ("StringId", ColumnKind.Text, 64)
    ], Nullable: ["PhaseId", "PhaseGroup", "StringId"]);

    /// <summary>Per-spawn creature extras (path, mount, stance, auras); overrides creature_template_addon.</summary>
    public static readonly TableSchema SpawnCreatureAddon = new("creature_addon", ["guid"],
    [
        ("guid", ColumnKind.ULong, 0),
        ("PathId", ColumnKind.UInt, 0),
        ("mount", ColumnKind.UInt, 0),
        ("MountCreatureID", ColumnKind.UInt, 0),
        ("StandState", ColumnKind.UInt, 0),
        ("AnimTier", ColumnKind.UInt, 0),
        ("VisFlags", ColumnKind.UInt, 0),
        ("SheathState", ColumnKind.UInt, 0),
        ("PvPFlags", ColumnKind.UInt, 0),
        ("emote", ColumnKind.UInt, 0),
        ("aiAnimKit", ColumnKind.UInt, 0),
        ("movementAnimKit", ColumnKind.UInt, 0),
        ("meleeAnimKit", ColumnKind.UInt, 0),
        ("visibilityDistanceType", ColumnKind.UInt, 0),
        ("auras", ColumnKind.Text, 0)
    ], Nullable: ["auras"]);

    /// <summary>What every spawn of a creature template looks like (mount, stance, weapons, emote, auras) unless the spawn has its own `creature_addon` row, which replaces this one entirely.</summary>
    public static readonly TableSchema CreatureTemplateAddon = new("creature_template_addon", ["entry"],
    [
        ("entry", ColumnKind.UInt, 0),
        ("PathId", ColumnKind.UInt, 0),
        ("mount", ColumnKind.UInt, 0),
        ("MountCreatureID", ColumnKind.UInt, 0),
        ("StandState", ColumnKind.UInt, 0),
        ("AnimTier", ColumnKind.UInt, 0),
        ("VisFlags", ColumnKind.UInt, 0),
        ("SheathState", ColumnKind.UInt, 0),
        ("PvPFlags", ColumnKind.UInt, 0),
        ("emote", ColumnKind.UInt, 0),
        ("aiAnimKit", ColumnKind.UInt, 0),
        ("movementAnimKit", ColumnKind.UInt, 0),
        ("meleeAnimKit", ColumnKind.UInt, 0),
        ("visibilityDistanceType", ColumnKind.UInt, 0),
        ("auras", ColumnKind.Text, 0)
    ], Nullable: ["auras"]);

    public static readonly TableSchema SpawnGameObjectAddon = new("gameobject_addon", ["guid"],
    [
        ("guid", ColumnKind.ULong, 0),
        ("parent_rotation0", ColumnKind.Float, 0),
        ("parent_rotation1", ColumnKind.Float, 0),
        ("parent_rotation2", ColumnKind.Float, 0),
        ("parent_rotation3", ColumnKind.Float, 0),
        ("invisibilityType", ColumnKind.UInt, 0),
        ("invisibilityValue", ColumnKind.UInt, 0),
        ("WorldEffectID", ColumnKind.UInt, 0),
        ("AIAnimKitID", ColumnKind.UInt, 0)
    ]);
}
