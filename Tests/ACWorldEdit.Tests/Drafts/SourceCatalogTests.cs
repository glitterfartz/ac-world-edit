using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>
/// The content draft validates each source's rows against that source's table catalog, and 64-bit
/// columns (TrinityCore bigints) validate and export as unquoted numbers.
/// </summary>
public class SourceCatalogTests
{
    static string TempPath() => Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "interactions.json");

    // creature_default_trainer is an AzerothCore table; TrinityCore's catalog has no such table.
    static readonly Row Before = Row.Of(("CreatureId", 100), ("TrainerId", 0));
    static readonly RowEdit Rename = new("creature_default_trainer", "100", Before, Before.With("TrainerId", 17));

    static RowSet Snapshot()
    {
        var rows = new RowSet();
        rows.Set(AcTables.DefaultTrainer, Before);
        return rows;
    }

    static ITableCatalog CatalogFor(string source) => source == "tc" ? TcSchema.Instance.Tables : AcTables.Instance;

    [Fact]
    public void EachSourceUsesItsOwnTables()
    {
        using var draft = new InteractionDraft(TempPath(), CatalogFor);
        draft.Execute("ac", Snapshot(), "rename", [Rename]);
        Assert.Single(draft.NetEdits());
        var error = Assert.Throws<InvalidDataException>(() => draft.Execute("tc", Snapshot(), "rename", [Rename]));
        Assert.Contains("TrinityCore", error.Message);
        Assert.Same(TcSchema.Instance.Tables, draft.Tables("tc"));
    }

    [Fact]
    public void ContentDraftsCommitThroughTheSourceCatalog()
    {
        using var drafts = new ContentDrafts(TempPath(), CatalogFor);
        var (ok, message) = drafts.Commit(null, "tc", Snapshot(), Snapshot(), "rename", [Rename], _ => []);
        Assert.False(ok);
        Assert.Contains("TrinityCore", message);
        Assert.True(drafts.Commit(null, "ac", Snapshot(), Snapshot(), "rename", [Rename], _ => []).Ok);
    }

    [Fact]
    public void AReopenedDraftValidatesEachCommandWithItsSourceCatalog()
    {
        string path = TempPath();
        using (var draft = new InteractionDraft(path, CatalogFor))
        {
            draft.Execute("ac", Snapshot(), "rename", [Rename]);
        }

        // Reopened with a catalog that no longer knows the AzerothCore source's table, the history is refused.
        Assert.ThrowsAny<Exception>(() => new InteractionDraft(path, _ => TcSchema.Instance.Tables));
        using var reopened = new InteractionDraft(path, CatalogFor);
        Assert.Single(reopened.NetEdits());
    }

    static readonly TableSchema Wide = new("wide", ["guid"], [("guid", ColumnKind.ULong, 0), ("money", ColumnKind.Long, 0)]);

    [Fact]
    public void SixtyFourBitColumnsValidateAtTheirFullRange()
    {
        Row.Of(("guid", ulong.MaxValue), ("money", long.MinValue)).Validate(Wide);
        Assert.Throws<InvalidDataException>(() => Row.Of(("guid", -1L), ("money", 0L)).Validate(Wide));
        Assert.Throws<InvalidDataException>(() => Row.Of(("guid", 1u), ("money", "1.5")).Validate(Wide));
        Assert.Equal(ulong.MaxValue, Row.Of(("guid", ulong.MaxValue)).UL("guid"));
    }

    sealed class WideSchema : IWorldSchema, ITableCatalog
    {
        public WorldDialect Dialect => WorldDialect.TrinityCore;
        public ITableCatalog Tables => this;
        public ISqlDialect Sql => TcSqlDialect.Instance;
        public EntityPage TemplatePage => TcCreaturePage.Page;
        public EntityPage StatsPage => TcStatsPage.Page;
        public ItemListSpec VendorPage => Foundation.VendorPage.TrinityCore;
        public GossipPageSpec GossipPage => Foundation.GossipPage.TrinityCore;
        public TrainerPageSpec TrainerPage => AcSchema.Instance.TrainerPage;
        public QuestLinksPageSpec QuestLinksPage => AcSchema.Instance.QuestLinksPage;
        public SmartCatalog Smart => SmartCatalog.Trinity;
        public ConditionCatalog Conditions => ConditionCatalog.Trinity;
        public IReadOnlyList<TableSchema> All => [Wide];
        public TableSchema Find(string name) => name == Wide.Name ? Wide : throw new InvalidDataException(name);
    }

    [Fact]
    public void SixtyFourBitColumnsExportAsPlainNumbers()
    {
        var before = Row.Of(("guid", 5_000_000_000ul), ("money", -5_000_000_000L));
        var edit = new RowEdit("wide", "5000000000", before, before.With("money", 7_000_000_000L));
        var scripts = SqlExport.Build("src", [], [edit], new DateTime(2026, 1, 1), null, new WideSchema());
        Assert.Contains("UPDATE `wide` SET `money` = 7000000000 WHERE `guid` = 5000000000 AND `money` = -5000000000;", scripts.Apply);
    }
}
