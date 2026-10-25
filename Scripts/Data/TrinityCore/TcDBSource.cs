using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// The TrinityCore world database (with its hotfixes database): one connection shared by the per-area
/// sources. Spawns, graveyards, teleports, area triggers and creature properties are read here; the
/// editors that depend on TrinityCore's restructured quest, SmartAI and waypoint tables are not
/// available yet and say so.
/// </summary>
public sealed class TcDBSource : IDBSource
{
    readonly WorldDatabase database;
    readonly TcSpawnSource spawns;
    readonly TcGraveyardSource graveyards;
    readonly TcAreaTriggerSource areaTriggers;
    readonly AcTeleportSource teleports;
    readonly TcInteractionSource interaction;

    public TcDBSource(WorldConnection connection, IClientLookup? lookup)
    {
        database = new WorldDatabase(connection);
        spawns = new(database);
        graveyards = new(database);
        var hotfixes = new TcHotfixSource(connection.HotfixesSettings);
        areaTriggers = new(database, lookup, hotfixes);
        interaction = new(database, lookup, hotfixes);
        // game_tele has the same schema in both servers.
        teleports = new(database);
    }

    public IWorldSchema Schema => TcSchema.Instance;
    public string SourceKey => database.SourceKey;

    static DatabaseFeatureException Unsupported(string what) => new($"{what} is not available for the TrinityCore world database yet.");

    public Task<CreatureRegionResult> ReadCreaturesAsync(CreatureRegion region, SpawnPreview preview, SpawnQueryPoint? point, string search, CancellationToken token) => spawns.ReadCreaturesAsync(region, preview, point, search, token);
    public Task<GameObjectRegionResult> ReadGameObjectsAsync(CreatureRegion region, SpawnPreview preview, SpawnQueryPoint? point, string search, CancellationToken token) => spawns.ReadGameObjectsAsync(region, preview, point, search, token);
    public Task<IReadOnlyList<(ulong Guid, uint Map, double X, double Y, double Z)>> FindSpawnsAsync(uint entry, CancellationToken token) => spawns.FindSpawnsAsync(entry, token);
    public Task<GraveyardRegionResult> ReadGraveyardsAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token) => graveyards.ReadAsync(region, point, search, token);
    public Task<AreaTriggerRegionResult> ReadAreaTriggersAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token) => areaTriggers.ReadAsync(region, point, search, token);
    public Task<TeleportLocation[]> ReadTeleportsAsync(CancellationToken token) => teleports.ReadAsync(token);
    public Task<WaypointRegionResult> ReadWaypointsAsync(CreatureRegion region, SpawnQueryPoint? point, CancellationToken token) => throw Unsupported("Waypoints");
    public Task<ContentSnapshot> ReadQuestAsync(uint quest, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => throw Unsupported("The quest editor");
    public Task<ContentSnapshot> ReadBehaviorAsync(uint entry, ulong guid, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => throw Unsupported("The behavior editor");
    public Task<ContentSnapshot> ReadAreaTriggerBehaviorAsync(uint trigger, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => throw Unsupported("The behavior editor");
    public Task<IReadOnlyList<(uint Id, string Name)>> SearchAsync(SmartParamKind kind, string term, CancellationToken token) => throw Unsupported("Search");
    public Task<IReadOnlyList<(uint Id, string Name)>> SearchBroadcastAsync(string term, CancellationToken token) => throw Unsupported("Broadcast text search");
    public Task<HashSet<int>> UsedActionListsAsync(int from, int to, CancellationToken token) => throw Unsupported("The behavior editor");
    public Task<InteractionSnapshot> ReadInteractionAsync(uint entry, ulong guid, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => interaction.ReadAsync(entry, guid, draftKeys, effective, catalog, token);
    public Task<bool> MenuExistsAsync(uint menuId, CancellationToken token) => throw Unsupported("The creature property editor");
    public Task<bool> TrainerExistsAsync(uint trainerId, CancellationToken token) => throw Unsupported("The creature property editor");
    public Task<IReadOnlyList<ItemDetails>> SearchItemDetailsAsync(string term, CancellationToken token) => throw Unsupported("Item search");
    public Task<IReadOnlyList<(uint Id, string Title, int Level)>> SearchQuestsAsync(string term, CancellationToken token) => throw Unsupported("Quest search");
}
