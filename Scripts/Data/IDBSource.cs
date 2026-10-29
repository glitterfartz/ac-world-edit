using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Read-only access to one dialect's world database, grouped by area. Every call opens its own
/// read-only snapshot transaction. Create one with <see cref="DBSource.Open"/>.
/// </summary>
public interface IDBSource
{
    IWorldSchema Schema { get; }

    /// <summary>Hash of host/port/database (no credentials) identifying drafts made against this source.</summary>
    string SourceKey { get; }

    // Spawns
    Task<CreatureRegionResult> ReadCreaturesAsync(CreatureRegion region, SpawnPreview preview, SpawnQueryPoint? point, string search, CancellationToken token);
    Task<GameObjectRegionResult> ReadGameObjectsAsync(CreatureRegion region, SpawnPreview preview, SpawnQueryPoint? point, string search, CancellationToken token);

    /// <summary>Spawns of a creature entry (guid, map, server position), bounded.</summary>
    Task<IReadOnlyList<(ulong Guid, uint Map, double X, double Y, double Z)>> FindSpawnsAsync(uint entry, CancellationToken token);

    /// <summary>The `creature` or `gameobject` rows of these GUIDs (bounded): the baseline of a spawn edit.</summary>
    Task<RowSet> ReadSpawnRowsAsync(SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token);

    /// <summary>The name, model and scale of these creature templates (bounded); a template the database lacks is absent.</summary>
    Task<IReadOnlyDictionary<uint, CreatureLook>> ReadCreatureLooksAsync(IReadOnlyList<uint> entries, CancellationToken token);

    /// <summary>The name, model, size and type of these GameObject templates (bounded); a template the database lacks is absent.</summary>
    Task<IReadOnlyDictionary<uint, GameObjectLook>> ReadGameObjectLooksAsync(IReadOnlyList<uint> entries, CancellationToken token);

    /// <summary>The largest value of a whole-number column (0 for an empty table): the database side of <see cref="IdAllocator"/>.</summary>
    Task<ulong> MaxIdAsync(TableSchema table, string column, CancellationToken token);

    // World points
    Task<GraveyardRegionResult> ReadGraveyardsAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token);
    Task<AreaTriggerRegionResult> ReadAreaTriggersAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token);
    Task<TeleportLocation[]> ReadTeleportsAsync(CancellationToken token);
    Task<WaypointRegionResult> ReadWaypointsAsync(CreatureRegion region, SpawnQueryPoint? point, CancellationToken token);

    // Content (quest and SmartAI editors)
    Task<ContentSnapshot> ReadQuestAsync(uint quest, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token);
    Task<ContentSnapshot> ReadBehaviorAsync(uint entry, ulong guid, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token);
    Task<ContentSnapshot> ReadAreaTriggerBehaviorAsync(AreaTriggerOwner trigger, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token);
    Task<IReadOnlyList<(uint Id, string Name)>> SearchAsync(SmartParamKind kind, string term, CancellationToken token);
    Task<IReadOnlyList<(uint Id, string Name)>> SearchBroadcastAsync(string term, CancellationToken token);
    Task<HashSet<int>> UsedActionListsAsync(int from, int to, CancellationToken token);

    // Interaction (creature property editor)
    Task<InteractionSnapshot> ReadInteractionAsync(uint entry, ulong guid, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token);
    Task<bool> MenuExistsAsync(uint menuId, CancellationToken token);

    /// <summary>Gossip menus by ID or by the greeting text they show (bounded); the name is the greeting's first text.</summary>
    Task<IReadOnlyList<(uint Id, string Name)>> SearchMenusAsync(string term, CancellationToken token);
    Task<bool> TrainerExistsAsync(uint trainerId, CancellationToken token);
    Task<bool> PoiExistsAsync(uint poiId, CancellationToken token);
    Task<IReadOnlyList<ItemDetails>> SearchItemDetailsAsync(string term, CancellationToken token);
    Task<IReadOnlyList<(uint Id, string Title, int Level)>> SearchQuestsAsync(string term, CancellationToken token);
}

/// <summary>Opens the <see cref="IDBSource"/> for a connection's dialect.</summary>
public static class DBSource
{
    /// <summary>
    /// The client lookup serves what the TrinityCore database lacks (area trigger volumes); AzerothCore ignores it.
    /// Fails with <see cref="DatabaseFeatureException"/> when the connection settings are incomplete or
    /// the dialect has no implementation yet. Call it where the read starts so the error reaches the
    /// caller's status handling.
    /// </summary>
    public static IDBSource Open(WorldConnection connection, IClientLookup? lookup = null) => connection.Dialect switch
    {
        WorldDialect.AzerothCore => new AcDBSource(connection),
        WorldDialect.TrinityCore => new TcDBSource(connection, lookup),
        _ => throw new DatabaseFeatureException($"The {connection.Dialect} world database is not supported yet.")
    };
}
