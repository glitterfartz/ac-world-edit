using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// The TrinityCore world database (with its hotfixes database): one connection shared by the per-area
/// sources. Spawns, graveyards, teleports, area triggers, creature properties, quests, creature
/// behavior (SmartAI and speech, also of client and spawned AreaTriggers) and waypoints are read here.
/// </summary>
public sealed class TcDBSource : IDBSource
{
    readonly WorldDatabase database;
    readonly TcSpawnSource spawns;
    readonly TcGraveyardSource graveyards;
    readonly TcAreaTriggerSource areaTriggers;
    readonly AcTeleportSource teleports;
    readonly TcInteractionSource interaction;
    readonly TcContentSource content;
    readonly TcBehaviorSource behavior;
    readonly TcWaypointSource waypoints;

    public TcDBSource(WorldConnection connection, IClientLookup? lookup)
    {
        database = new WorldDatabase(connection);
        spawns = new(database);
        graveyards = new(database);
        SqlExport.HotfixesDatabase = connection.Hotfixes?.Trim() is { Length: > 0 } hotfixesName ? hotfixesName : WorldConnection.DefaultHotfixes;
        var hotfixes = new TcHotfixSource(connection.HotfixesSettings);
        areaTriggers = new(database, lookup, hotfixes);
        interaction = new(database, lookup, hotfixes);
        content = new(database, lookup);
        behavior = new(database, lookup, hotfixes);
        waypoints = new(database);
        // game_tele has the same schema in both servers.
        teleports = new(database);
    }

    public IWorldSchema Schema => TcSchema.Instance;
    public string SourceKey => database.SourceKey;

    static DatabaseFeatureException Unsupported(string what) => new($"{what} is not available for the TrinityCore world database yet.");

    public Task<CreatureRegionResult> ReadCreaturesAsync(CreatureRegion region, SpawnPreview preview, SpawnQueryPoint? point, string search, CancellationToken token) => spawns.ReadCreaturesAsync(region, preview, point, search, token);
    public Task<GameObjectRegionResult> ReadGameObjectsAsync(CreatureRegion region, SpawnPreview preview, SpawnQueryPoint? point, string search, CancellationToken token) => spawns.ReadGameObjectsAsync(region, preview, point, search, token);
    public Task<IReadOnlyList<(ulong Guid, uint Map, double X, double Y, double Z)>> FindSpawnsAsync(uint entry, CancellationToken token) => spawns.FindSpawnsAsync(entry, token);
    public Task<RowSet> ReadSpawnRowsAsync(SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token) => DbSpawnRows.ReadAsync(database, TcTables.Instance, kind, guids, token);
    public Task<IReadOnlyDictionary<uint, CreatureLook>> ReadCreatureLooksAsync(IReadOnlyList<uint> entries, CancellationToken token) => DbCreatureLooks.ReadAsync(database, WorldDialect.TrinityCore, entries, token);
    public Task<IReadOnlyDictionary<uint, GameObjectLook>> ReadGameObjectLooksAsync(IReadOnlyList<uint> entries, CancellationToken token) => DbGameObjectLooks.ReadAsync(database, entries, token);
    public Task<IReadOnlyList<SpawnImpactItem>> CountSpawnReferencesAsync(SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token) => DbSpawnImpact.CountAsync(database, WorldDialect.TrinityCore, kind, guids, token);
    public Task<IReadOnlyList<SpawnPathUse>> ReadSpawnPathsAsync(IReadOnlyList<ulong> guids, CancellationToken token) => DbSpawnPaths.ReadAsync(database, WorldDialect.TrinityCore, guids, token);
    public Task<IReadOnlyDictionary<string, string>> ReadTemplateColumnsAsync(SpawnKind kind, uint entry, IReadOnlyList<string> columns, CancellationToken token) => DbTemplateColumns.ReadAsync(database, kind, entry, columns, token);
    public Task<Row?> ReadTemplateAddonAsync(uint entry, CancellationToken token) => DbTemplateAddon.ReadAsync(database, TcTables.Instance, entry, token);
    public Task<TemplateUse> CountTemplateUseAsync(uint entry, CancellationToken token) => DbTemplateAddon.CountAsync(database, entry, token);
    public Task<IReadOnlyList<TemplateHit>> SearchTemplatesAsync(SpawnKind kind, string term, int? type, CancellationToken token) => DbTemplateSearch.SearchAsync(database, kind, term, type, token);
    public Task<ulong> MaxIdAsync(TableSchema table, string column, CancellationToken token) => DbIds.MaxAsync(database, table, column, token);
    public Task<GraveyardRegionResult> ReadGraveyardsAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token) => graveyards.ReadAsync(region, point, search, token);
    public Task<AreaTriggerRegionResult> ReadAreaTriggersAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token) => areaTriggers.ReadAsync(region, point, search, token);
    public Task<TeleportLocation[]> ReadTeleportsAsync(CancellationToken token) => teleports.ReadAsync(token);
    public Task<WaypointRegionResult> ReadWaypointsAsync(CreatureRegion region, SpawnQueryPoint? point, CancellationToken token) => waypoints.ReadAsync(region, point, token);
    public Task<ContentSnapshot> ReadQuestAsync(uint quest, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => content.ReadQuestAsync(quest, draftKeys, effective, catalog, token);
    public Task<ContentSnapshot> ReadBehaviorAsync(uint entry, ulong guid, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => behavior.ReadAsync(entry, guid, draftKeys, effective, catalog, token);
    public Task<ContentSnapshot> ReadAreaTriggerBehaviorAsync(AreaTriggerOwner trigger, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => behavior.ReadAreaTriggerAsync(trigger, areaTriggers, draftKeys, effective, catalog, token);
    public Task<IReadOnlyList<(uint Id, string Name)>> SearchAsync(SmartParamKind kind, string term, CancellationToken token) => content.SearchAsync(kind, term, token);
    public Task<IReadOnlyList<(uint Id, string Name)>> SearchBroadcastAsync(string term, CancellationToken token) => interaction.FindBroadcastAsync(term, token);
    public Task<HashSet<int>> UsedActionListsAsync(int from, int to, CancellationToken token) => behavior.UsedActionListsAsync(from, to, token);
    public Task<InteractionSnapshot> ReadInteractionAsync(uint entry, ulong guid, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => interaction.ReadAsync(entry, guid, draftKeys, effective, catalog, token);
    public Task<bool> MenuExistsAsync(uint menuId, CancellationToken token) => interaction.MenuExistsAsync(menuId, token);
    public Task<IReadOnlyList<(uint Id, string Name)>> SearchMenusAsync(string term, CancellationToken token) => interaction.SearchMenusAsync(term, token);
    public Task<bool> PoiExistsAsync(uint poiId, CancellationToken token) => interaction.PoiExistsAsync(poiId, token);
    public Task<bool> TrainerExistsAsync(uint trainerId, CancellationToken token) => throw Unsupported("The creature property editor");
    public Task<IReadOnlyList<ItemDetails>> SearchItemDetailsAsync(string term, CancellationToken token) => throw Unsupported("Item search");
    public Task<IReadOnlyList<(uint Id, string Title, int Level)>> SearchQuestsAsync(string term, CancellationToken token) => interaction.SearchQuestsAsync(term, token);
}
