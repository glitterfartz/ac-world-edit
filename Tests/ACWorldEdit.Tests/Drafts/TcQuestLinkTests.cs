using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class TcQuestLinkTests
{
    static readonly QuestLinksPageSpec Page = TcSchema.Instance.QuestLinksPage;
    static string TempPath() => Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "quests.json");
    static IEnumerable<InteractionIssue> Check(RowSet rows) => TcQuestLinkRules.Check(rows, id => id is 100 or 101);

    [Theory]
    [InlineData(0u, "enter a quest ID")]
    [InlineData(999u, "does not exist")]
    public void UnknownQuestLinksAreRefused(uint quest, string message)
    {
        var rows = new RowSet();
        using var drafts = new ContentDrafts(TempPath(), _ => TcTables.Instance);
        var result = drafts.Commit(null, "tc", rows, rows, "Link", Page.SetRoles(rows, 5, quest, true, false), Check);
        Assert.False(result.Ok);
        Assert.Contains(message, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(drafts.Draft!.NetEdits());
    }

    [Fact]
    public void LinkLifecyclePersistsUndoesAndExports()
    {
        var rows = new RowSet();
        string path = TempPath();
        using (var drafts = new ContentDrafts(path, _ => TcTables.Instance))
        {
            Assert.True(drafts.Commit(null, "tc", rows, rows, "Link", Page.SetRoles(rows, 5, 100, true, true), Check).Ok);
            var effective = drafts.Draft!.Effective("tc", rows);
            Assert.Single(Page.Starts.Select(effective, 5));
            Assert.Single(Page.Ends.Select(effective, 5));
            // Keeping both roles changes nothing; dropping one removes only that row.
            Assert.Empty(Page.SetRoles(effective, 5, 100, true, true));
            Assert.True(drafts.Commit(null, "tc", rows, effective, "Unlink end", Page.SetRoles(effective, 5, 100, true, false), Check).Ok);
            Assert.Empty(Page.Ends.Select(drafts.Draft.Effective("tc", rows), 5));
            var sql = SqlExport.Build("tc", [], [], drafts.Draft.NetEdits().Select(e => e.Edit).ToArray(), DateTime.UnixEpoch, schema: TcSchema.Instance);
            Assert.Contains("`creature_queststarter`", sql.Apply);
            drafts.History(null, false);
            Assert.Single(Page.Ends.Select(drafts.Draft.Effective("tc", rows), 5));
            drafts.History(null, true);
        }

        using var reopened = new ContentDrafts(path, _ => TcTables.Instance);
        var restored = reopened.Draft!.Effective("tc", rows);
        Assert.Single(Page.Starts.Select(restored, 5));
        Assert.Empty(Page.Ends.Select(restored, 5));
    }
}
