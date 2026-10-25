using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>
/// The content draft validates each source's rows against that source's table catalog.
/// </summary>
public class SourceCatalogTests
{
    static string TempPath() => Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "interactions.json");

    static readonly Row Before = Row.Of(("entry", 100u), ("npcflag", 1u), ("gossip_menu_id", 0u), ("subname", ""));
    static readonly RowEdit Rename = new("creature_template", "100", Before, Before.With("subname", "Innkeeper"));

    static RowSet Snapshot()
    {
        var rows = new RowSet();
        rows.Set(AcTables.CreatureTemplate, Before);
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
}
