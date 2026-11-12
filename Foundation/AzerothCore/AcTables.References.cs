namespace ACWorldEdit.Foundation;

// The tables outside the spawn editors that name a spawn by GUID: what a spawn delete can remove with it.
public sealed partial class AcTables
{
    /// <summary>Pool membership of one spawn (the pool itself lives in pool_template).</summary>
    public static readonly TableSchema PoolCreature = new("pool_creature", ["guid"], [
        ("guid", ColumnKind.UInt, 0),
        ("pool_entry", ColumnKind.UInt, 0),
        ("chance", ColumnKind.Float, 0),
        ("description", ColumnKind.Text, 255)
    ], Nullable: ["description"]);

    public static readonly TableSchema PoolGameObject = new("pool_gameobject", ["guid"], [
        ("guid", ColumnKind.UInt, 0),
        ("pool_entry", ColumnKind.UInt, 0),
        ("chance", ColumnKind.Float, 0),
        ("description", ColumnKind.Text, 255)
    ], Nullable: ["description"]);

    /// <summary>A spawn's membership of a spawn group.</summary>
    public static readonly TableSchema SpawnGroup = new("spawn_group", ["groupId", "spawnType", "spawnId"], [
        ("groupId", ColumnKind.UInt, 0),
        ("spawnType", ColumnKind.UInt, 0),
        ("spawnId", ColumnKind.UInt, 0)
    ]);

    public static readonly TableSchema GameEventCreature = new("game_event_creature", ["eventEntry", "guid"], [
        ("eventEntry", ColumnKind.Int, 0),
        ("guid", ColumnKind.UInt, 0)
    ]);

    public static readonly TableSchema GameEventGameObject = new("game_event_gameobject", ["eventEntry", "guid"], [
        ("eventEntry", ColumnKind.Int, 0),
        ("guid", ColumnKind.UInt, 0)
    ]);

    /// <summary>The model and equipment a creature wears during one event.</summary>
    public static readonly TableSchema GameEventModelEquip = new("game_event_model_equip", ["guid"], [
        ("eventEntry", ColumnKind.UInt, 0),
        ("guid", ColumnKind.UInt, 0),
        ("modelid", ColumnKind.UInt, 0),
        ("equipment_id", ColumnKind.UInt, 0)
    ]);

    public static readonly TableSchema LinkedRespawn = new("linked_respawn", ["guid", "linkType"], [
        ("guid", ColumnKind.UInt, 0),
        ("linkedGuid", ColumnKind.UInt, 0),
        ("linkType", ColumnKind.UInt, 0)
    ]);
}
