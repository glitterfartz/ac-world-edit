namespace ACWorldEdit.Foundation;

// The tables outside the spawn editors that name a spawn by GUID: what a spawn delete can remove with it.
public sealed partial class TcTables
{
    /// <summary>Pool membership (type 0 is a creature, 1 a GameObject); guids are bigint here.</summary>
    public static readonly TableSchema PoolMembers = new("pool_members", ["type", "spawnId"], [
        ("type", ColumnKind.UInt, 0),
        ("spawnId", ColumnKind.ULong, 0),
        ("poolSpawnId", ColumnKind.UInt, 0),
        ("chance", ColumnKind.Float, 0),
        ("description", ColumnKind.Text, 255)
    ], Nullable: ["description"]);

    public static readonly TableSchema SpawnGroup = new("spawn_group", ["groupId", "spawnType", "spawnId"], [
        ("groupId", ColumnKind.UInt, 0),
        ("spawnType", ColumnKind.UInt, 0),
        ("spawnId", ColumnKind.ULong, 0)
    ]);

    public static readonly TableSchema GameEventCreature = new("game_event_creature", ["eventEntry", "guid"], [
        ("eventEntry", ColumnKind.Int, 0),
        ("guid", ColumnKind.ULong, 0)
    ]);

    public static readonly TableSchema GameEventGameObject = new("game_event_gameobject", ["eventEntry", "guid"], [
        ("eventEntry", ColumnKind.Int, 0),
        ("guid", ColumnKind.ULong, 0)
    ]);

    /// <summary>TrinityCore master keeps this table too, so a creature's event model swap is removable as well.</summary>
    public static readonly TableSchema GameEventModelEquip = new("game_event_model_equip", ["guid"], [
        ("eventEntry", ColumnKind.Int, 0),
        ("guid", ColumnKind.ULong, 0),
        ("modelid", ColumnKind.UInt, 0),
        ("equipment_id", ColumnKind.UInt, 0)
    ]);

    public static readonly TableSchema LinkedRespawn = new("linked_respawn", ["guid", "linkType"], [
        ("guid", ColumnKind.ULong, 0),
        ("linkedGuid", ColumnKind.ULong, 0),
        ("linkType", ColumnKind.UInt, 0)
    ]);
}
