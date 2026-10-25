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

    // World points
    Task<GraveyardRegionResult> ReadGraveyardsAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token);
    Task<AreaTriggerRegionResult> ReadAreaTriggersAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token);
    Task<TeleportLocation[]> ReadTeleportsAsync(CancellationToken token);
    Task<WaypointRegionResult> ReadWaypointsAsync(CreatureRegion region, SpawnQueryPoint? point, CancellationToken token);

    // Content (quest and SmartAI editors)
    Task<ContentSnapshot> ReadQuestAsync(uint quest, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token);
    Task<ContentSnapshot> ReadBehaviorAsync(uint entry, ulong guid, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token);
    Task<ContentSnapshot> ReadAreaTriggerBehaviorAsync(uint trigger, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token);
    Task<IReadOnlyList<(uint Id, string Name)>> SearchAsync(SmartParamKind kind, string term, CancellationToken token);
    Task<IReadOnlyList<(uint Id, string Name)>> SearchBroadcastAsync(string term, CancellationToken token);
    Task<HashSet<int>> UsedActionListsAsync(int from, int to, CancellationToken token);

    // Interaction (creature property editor)
    Task<InteractionSnapshot> ReadInteractionAsync(uint entry, ulong guid, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token);
    Task<bool> MenuExistsAsync(uint menuId, CancellationToken token);
    Task<bool> TrainerExistsAsync(uint trainerId, CancellationToken token);
    Task<IReadOnlyList<ItemDetails>> SearchItemDetailsAsync(string term, CancellationToken token);
    Task<IReadOnlyList<(uint Id, string Title, int Level)>> SearchQuestsAsync(string term, CancellationToken token);
}

/// <summary>Opens the <see cref="IDBSource"/> for a connection's dialect.</summary>
public static class DBSource
{
    /// <summary>
    /// Fails with <see cref="InvalidDataException"/> when the connection settings are incomplete or
    /// the dialect has no implementation yet. Call it where the read starts so the error reaches the
    /// caller's status handling.
    /// </summary>
    public static IDBSource Open(WorldConnection connection) => connection.Dialect switch
    {
        WorldDialect.AzerothCore => new AcDBSource(connection),
        _ => throw new InvalidDataException($"The {connection.Dialect} world database is not supported yet.")
    };
}
