using ACWorldEdit;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// The Used by page, headless: both windows list it, the page says what to do without a selection, and the
/// card reads the counts once per entry, names them, and keeps a failed read apart from an unused template.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class UsedBySectionTests
{
    static SceneTree Tree => (SceneTree)Engine.GetMainLoop();

    static async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    /// <summary>A source that answers only the template count, on the frame after it is asked.</summary>
    sealed class Counting(SpawnKind kind, IReadOnlyList<SpawnImpactItem> found) : IDBSource
    {
        public IWorldSchema Schema => AcSchema.Instance;
        public string SourceKey => "test";

        public async Task<IReadOnlyList<SpawnImpactItem>> CountTemplateReferencesAsync(SpawnKind asked, uint entry, CancellationToken token)
        {
            await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
            return asked == kind ? found : [];
        }

        public Task<CreatureRegionResult> ReadCreaturesAsync(CreatureRegion region, SpawnPreview preview, SpawnQueryPoint? point, string search, CancellationToken token) => throw new NotSupportedException();
        public Task<GameObjectRegionResult> ReadGameObjectsAsync(CreatureRegion region, SpawnPreview preview, SpawnQueryPoint? point, string search, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<(ulong Guid, uint Map, double X, double Y, double Z)>> FindSpawnsAsync(uint entry, CancellationToken token) => throw new NotSupportedException();
        public Task<RowSet> ReadSpawnRowsAsync(SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<uint, CreatureLook>> ReadCreatureLooksAsync(IReadOnlyList<uint> entries, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<(uint Id, string Name)>> SearchCreatureDisplaysAsync(string term, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<(uint Id, uint First, uint Second, uint Third)>> ReadEquipmentSetsAsync(uint entry, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<uint, GameObjectLook>> ReadGameObjectLooksAsync(IReadOnlyList<uint> entries, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<SpawnImpactItem>> CountSpawnReferencesAsync(SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token) => throw new NotSupportedException();
        public Task<SpawnReferences> ReadSpawnReferencesAsync(SpawnKind kind, IReadOnlyList<ulong> guids, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<SpawnPathUse>> ReadSpawnPathsAsync(IReadOnlyList<ulong> guids, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<PathUsers>> ReadPathUsersAsync(IReadOnlyList<uint> pathIds, CancellationToken token) => throw new NotSupportedException();
        public Task<RowSet> ReadWaypointPathRowsAsync(uint pathId, CancellationToken token) => throw new NotSupportedException();
        public Task<RowSet> ReadFormationsAsync(IReadOnlyList<ulong> guids, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, string>> ReadTemplateColumnsAsync(SpawnKind kind, uint entry, IReadOnlyList<string> columns, CancellationToken token) => throw new NotSupportedException();
        public Task<GameObjectSnapshot> ReadGameObjectTemplateAsync(uint entry, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, CancellationToken token) => throw new NotSupportedException();
        public Task<Row?> ReadTemplateAddonAsync(uint entry, CancellationToken token) => throw new NotSupportedException();
        public Task<TemplateUse> CountTemplateUseAsync(uint entry, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<TemplateHit>> SearchTemplatesAsync(SpawnKind kind, string term, int? type, CancellationToken token) => throw new NotSupportedException();
        public Task<ulong> MaxIdAsync(TableSchema table, string column, CancellationToken token) => throw new NotSupportedException();
        public Task<GraveyardRegionResult> ReadGraveyardsAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token) => throw new NotSupportedException();
        public Task<AreaTriggerRegionResult> ReadAreaTriggersAsync(CreatureRegion region, SpawnQueryPoint? point, string search, CancellationToken token) => throw new NotSupportedException();
        public Task<TeleportLocation[]> ReadTeleportsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<WaypointRegionResult> ReadWaypointsAsync(CreatureRegion region, SpawnQueryPoint? point, CancellationToken token) => throw new NotSupportedException();
        public Task<ContentSnapshot> ReadQuestAsync(uint quest, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => throw new NotSupportedException();
        public Task<ContentSnapshot> ReadBehaviorAsync(uint entry, ulong guid, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => throw new NotSupportedException();
        public Task<ContentSnapshot> ReadAreaTriggerBehaviorAsync(AreaTriggerOwner trigger, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<(uint Id, string Name)>> SearchAsync(SmartParamKind kind, string term, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<(uint Id, string Name)>> SearchBroadcastAsync(string term, CancellationToken token) => throw new NotSupportedException();
        public Task<HashSet<int>> UsedActionListsAsync(int from, int to, CancellationToken token) => throw new NotSupportedException();
        public Task<InteractionSnapshot> ReadInteractionAsync(uint entry, ulong guid, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> MenuExistsAsync(uint menuId, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<(uint Id, string Name)>> SearchMenusAsync(string term, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> TrainerExistsAsync(uint trainerId, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> PoiExistsAsync(uint poiId, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ItemDetails>> SearchItemDetailsAsync(string term, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<(uint Id, string Title, int Level)>> SearchQuestsAsync(string term, CancellationToken token) => throw new NotSupportedException();
        public Task<PlayerChoiceSnapshot> ReadPlayerChoiceAsync(uint choiceId, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<Row>> ReadCinematicConditionsAsync(int sourceType, int sourceGroup, int? sourceEntry, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<(uint Id, string Name)>> SearchPlayerChoicesAsync(string term, CancellationToken token) => throw new NotSupportedException();
        public Task<ConversationSnapshot> ReadConversationAsync(uint id, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<(uint Id, string Name)>> SearchConversationsAsync(string term, CancellationToken token) => throw new NotSupportedException();
    }

    static CreaturePropertiesWindow CreaturePage(WorldDialect dialect = WorldDialect.AzerothCore)
    {
        string path = Path.Combine(Path.GetTempPath(), "acwe-gdtest-" + Guid.NewGuid().ToString("N") + ".json");
        var window = new CreaturePropertiesWindow
        {
            Drafts = new ContentDrafts(path),
            Connection = () => WorldConnection.DefaultFor(dialect)
        };
        Tree.Root.AddChild(window);
        AutoFree(window);
        return window;
    }

    static GameObjectPropertiesWindow GameObjectPage(WorldDialect dialect = WorldDialect.AzerothCore)
    {
        string path = Path.Combine(Path.GetTempPath(), "acwe-gdtest-" + Guid.NewGuid().ToString("N") + ".json");
        var window = new GameObjectPropertiesWindow
        {
            Drafts = new ContentDrafts(path),
            Connection = () => WorldConnection.DefaultFor(dialect)
        };
        Tree.Root.AddChild(window);
        AutoFree(window);
        return window;
    }

    static ReferenceCountCard Card(SpawnKind kind, IDBSource source)
    {
        var card = AutoFree(new ReferenceCountCard(kind))!;
        Tree.Root.AddChild(card);
        card.Bind(() => source, () => { });
        return card;
    }

    [TestCase]
    public void BothWindowsListThePageForBothDialects()
    {
        foreach (var dialect in new[] { WorldDialect.AzerothCore, WorldDialect.TrinityCore })
        {
            var creature = CreaturePage(dialect).Section("usedby");
            AssertThat(creature).IsNotNull();
            AssertThat(creature!.Title).IsEqual("Used by");
            AssertThat(creature.Supports(dialect)).IsTrue();

            var gameObject = GameObjectPage(dialect).Section("usedby");
            AssertThat(gameObject).IsNotNull();
            AssertThat(gameObject!.Supports(dialect)).IsTrue();
        }
    }

    [TestCase]
    public void WithoutASelectionThePageSaysSo()
    {
        var window = CreaturePage();
        window.ShowSection("usedby", null);
        var section = (UsedBySection)window.Section("usedby")!;
        section.Refresh();

        AssertThat(section.GetChildCount() >= 1).IsTrue();
        AssertThat(section.GetChild(0) is Label).IsTrue();
        AssertString(((Label)section.GetChild(0)).Text).Contains("Select a creature in the world.");
    }

    [TestCase]
    public void BothPagesReadWithTheirOwnSource()
    {
        // A section that forgets Bind leaves the card on its loading line forever.
        var creature = (UsedBySection)CreaturePage().Section("usedby")!;
        var gameObject = (GameObjectUsedBySection)GameObjectPage().Section("usedby")!;
        foreach (var card in new[] { creature.SmokeCard, gameObject.SmokeCard })
        {
            card.Show(3);
            AssertString(card.SmokeText).Contains("this template");
        }
    }

    [TestCase]
    public async Task TheCardCountsThenNamesWhatUsesTheTemplate()
    {
        var item = new SpawnImpactItem(new SpawnReference("creature", "id", "spawns"), 3);
        var card = Card(SpawnKind.Creature, new Counting(SpawnKind.Creature, [item]));
        card.Show(42);

        // The read has not answered on the frame it starts, so the card says it is counting.
        AssertString(card.SmokeText).Contains("Counting what else names this template");
        await Frames(2);

        AssertString(card.SmokeText).Contains("These rows name this template");
        AssertThat(card.SmokeCounts!.Count).IsEqual(1);
        var row = (Label)card.GetChild(1).GetChild(0);
        AssertThat(row.Text).IsEqual("• 3 spawns (creature)");
    }

    [TestCase]
    public async Task ATemplateNothingNamesSaysSo()
    {
        var card = Card(SpawnKind.GameObject, new Counting(SpawnKind.GameObject, []));
        card.Show(7);
        await Frames(2);

        AssertString(card.SmokeText).Contains("Nothing else in the database names this template.");
        AssertThat(card.SmokeCounts!.Count).IsEqual(0);
    }

    [TestCase]
    public async Task AFailedReadSaysSoInsteadOfClaimingTheTemplateIsUnused()
    {
        var card = AutoFree(new ReferenceCountCard(SpawnKind.Creature))!;
        Tree.Root.AddChild(card);
        card.Bind(() => throw new DatabaseFeatureException("Enter the world database host, user and database name."), () => { });
        card.Show(5);
        await Frames(2);

        AssertString(card.SmokeText).Contains("Could not read what uses this template");
        AssertString(card.SmokeText).Contains("Enter the world database host");
        AssertObject(card.SmokeCounts).IsNull();
    }
}
