using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>The AzerothCore 3.3.5 world database: one connection shared by the per-area sources.</summary>
public sealed class AcDBSource : IDBSource
{
    readonly WorldDatabase database;
    readonly AcCreatureSource creatures;
    readonly AcGameObjectSource gameObjects;
    readonly AcGraveyardSource graveyards;
    readonly AcAreaTriggerSource areaTriggers;
    readonly AcTeleportSource teleports;
    readonly AcWaypointSource waypoints;
    readonly AcContentSource content;
    readonly AcInteractionSource interaction;

    public AcDBSource(WorldConnection connection)
    {
        database = new WorldDatabase(connection);
        creatures = new(database);
        gameObjects = new(database);
        graveyards = new(database);
        areaTriggers = new(database);
        teleports = new(database);
        waypoints = new(database);
        content = new(database);
        interaction = new(database);
    }

    public IWorldSchema Schema => AcSchema.Instance;
    public string SourceKey => database.SourceKey;

    public Task<CreatureRegionResult> ReadCreaturesAsync(CreatureRegion region, SpawnPreview preview, SpawnQueryPoint? point, string search, CancellationToken token) => creatures.ReadAsync(region, preview, point, search, token);
    public Task<GameObjectRegionResult> ReadGameObjectsAsync(CreatureRegion region, SpawnPreview preview, SpawnQueryPoint? point, string search, CancellationToken token) => gameObjects.ReadAsync(region, preview, point, search, token);
    public Task<IReadOnlyList<(ulong Guid, uint Map, double X, double Y, double Z)>> FindSpawnsAsync(uint entry, CancellationToken token) => content.FindSpawnsAsync(entry, token);
    public Task<RowSet> ReadSpawnRowsAsync(SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token) => DbSpawnRows.ReadAsync(database, AcTables.Instance, kind, guids, token);
    public Task<IReadOnlyDictionary<uint, CreatureLook>> ReadCreatureLooksAsync(IReadOnlyList<uint> entries, CancellationToken token) => DbCreatureLooks.ReadAsync(database, WorldDialect.AzerothCore, entries, token);
    public Task<IReadOnlyList<(uint Id, string Name)>> SearchCreatureDisplaysAsync(string term, CancellationToken token) => DbCreatureDisplays.SearchAsync(database, term, token);
    public Task<IReadOnlyList<(uint Id, uint First, uint Second, uint Third)>> ReadEquipmentSetsAsync(uint entry, CancellationToken token) => DbCreatureEquipment.ReadAsync(database, entry, token);
    public Task<IReadOnlyDictionary<uint, GameObjectLook>> ReadGameObjectLooksAsync(IReadOnlyList<uint> entries, CancellationToken token) => DbGameObjectLooks.ReadAsync(database, entries, token);
    public Task<IReadOnlyList<SpawnImpactItem>> CountSpawnReferencesAsync(SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token) => DbSpawnImpact.CountAsync(database, WorldDialect.AzerothCore, kind, guids, token);
    public Task<IReadOnlyList<SpawnPathUse>> ReadSpawnPathsAsync(IReadOnlyList<ulong> guids, CancellationToken token) => DbSpawnPaths.ReadAsync(database, WorldDialect.AzerothCore, guids, token);
    public Task<IReadOnlyList<PathUsers>> ReadPathUsersAsync(IReadOnlyList<uint> pathIds, CancellationToken token) => DbPathUsers.ReadAsync(database, WorldDialect.AzerothCore, pathIds, token);
    public Task<RowSet> ReadWaypointPathRowsAsync(uint pathId, CancellationToken token) => DbWaypointRows.ReadPathAsync(database, AcTables.Instance, WorldDialect.AzerothCore, pathId, token);
    public Task<RowSet> ReadFormationsAsync(IReadOnlyList<ulong> guids, CancellationToken token) => DbFormations.ReadAsync(database, AcTables.Instance, guids, token);
    public Task<IReadOnlyDictionary<string, string>> ReadTemplateColumnsAsync(SpawnKind kind, uint entry, IReadOnlyList<string> columns, CancellationToken token) => DbTemplateColumns.ReadAsync(database, kind, entry, columns, token);
    public Task<GameObjectSnapshot> ReadGameObjectTemplateAsync(uint entry, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, CancellationToken token) => DbGameObjectTemplate.ReadAsync(database, AcTables.Instance, entry, draftKeys, effective, token);
    public Task<Row?> ReadTemplateAddonAsync(uint entry, CancellationToken token) => DbTemplateAddon.ReadAsync(database, AcTables.Instance, entry, token);
    public Task<TemplateUse> CountTemplateUseAsync(uint entry, CancellationToken token) => DbTemplateAddon.CountAsync(database, entry, token);
    public Task<IReadOnlyList<TemplateHit>> SearchTemplatesAsync(SpawnKind kind, string term, int? type, CancellationToken token) => DbTemplateSearch.SearchAsync(database, kind, term, type, token);
    public Task<ulong> MaxIdAsync(TableSchema table, string column, CancellationToken token) => DbIds.MaxAsync(database, table, column, token);
    public Task<GraveyardRegionResult> ReadGraveyardsAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token) => graveyards.ReadAsync(region, point, search, token);
    public Task<AreaTriggerRegionResult> ReadAreaTriggersAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token) => areaTriggers.ReadAsync(region, point, search, token);
    public Task<TeleportLocation[]> ReadTeleportsAsync(CancellationToken token) => teleports.ReadAsync(token);
    public Task<WaypointRegionResult> ReadWaypointsAsync(CreatureRegion region, SpawnQueryPoint? point, CancellationToken token) => waypoints.ReadAsync(region, point, token);
    public Task<ContentSnapshot> ReadQuestAsync(uint quest, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => content.ReadQuestAsync(quest, draftKeys, effective, catalog, token);
    public Task<ContentSnapshot> ReadBehaviorAsync(uint entry, ulong guid, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => content.ReadBehaviorAsync(entry, guid, draftKeys, effective, catalog, token);
    public Task<ContentSnapshot> ReadAreaTriggerBehaviorAsync(AreaTriggerOwner trigger, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => content.ReadAreaTriggerBehaviorAsync(trigger, draftKeys, effective, catalog, token);
    public Task<IReadOnlyList<(uint Id, string Name)>> SearchAsync(SmartParamKind kind, string term, CancellationToken token) => content.SearchAsync(kind, term, token);
    public Task<IReadOnlyList<(uint Id, string Name)>> SearchBroadcastAsync(string term, CancellationToken token) => content.SearchBroadcastAsync(term, token);
    public Task<HashSet<int>> UsedActionListsAsync(int from, int to, CancellationToken token) => content.UsedActionListsAsync(from, to, token);
    public Task<InteractionSnapshot> ReadInteractionAsync(uint entry, ulong guid, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => interaction.ReadAsync(entry, guid, draftKeys, effective, catalog, token);
    public Task<bool> MenuExistsAsync(uint menuId, CancellationToken token) => interaction.MenuExistsAsync(menuId, token);
    public Task<IReadOnlyList<(uint Id, string Name)>> SearchMenusAsync(string term, CancellationToken token) => interaction.SearchMenusAsync(term, token);
    public Task<bool> PoiExistsAsync(uint poiId, CancellationToken token) => interaction.PoiExistsAsync(poiId, token);
    public Task<bool> TrainerExistsAsync(uint trainerId, CancellationToken token) => interaction.TrainerExistsAsync(trainerId, token);
    public Task<IReadOnlyList<ItemDetails>> SearchItemDetailsAsync(string term, CancellationToken token) => interaction.SearchItemDetailsAsync(term, token);
    public Task<IReadOnlyList<(uint Id, string Title, int Level)>> SearchQuestsAsync(string term, CancellationToken token) => interaction.SearchQuestsAsync(term, token);
}
