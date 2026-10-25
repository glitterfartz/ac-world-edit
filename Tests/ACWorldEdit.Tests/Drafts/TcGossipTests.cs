using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class TcGossipTests
{
    static readonly GossipPageSpec Page = TcSchema.Instance.GossipPage;
    static string TempPath() => Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "gossip.json");
    static Row Greeting(uint id = 100) => Row.Of(TcTables.NpcText.Columns.Select(c => (c.Name, (object)"0")).ToArray())
        .With("ID", id).With("BroadcastTextID0", 123u).With("Probability0", 1f);
    static RowSet Rows()
    {
        var rows = new RowSet();
        rows.Set(TcTables.NpcText, Greeting());
        rows.Set(TcTables.GossipMenu, Row.Of(("MenuID", 42u), ("TextID", 100u)));
        rows.Set(Page.Options.Table, Page.Options.Add(rows, 42).After!);
        return rows;
    }
    static IEnumerable<InteractionIssue> Check(RowSet rows) => TcGossipRules.Check(rows);

    [Theory]
    [InlineData("OptionNpc", "255", "service")]
    [InlineData("BoxCoded", "2", "code")]
    [InlineData("BoxMoney", "4294967296", "cost")]
    public void InvalidOptionsLeaveHistoryUntouched(string column, string value, string message)
    {
        var rows = Rows();
        var option = Page.Options.Select(rows, 42).Single();
        using var drafts = new ContentDrafts(TempPath(), _ => TcTables.Instance);
        var result = drafts.Commit(null, "tc", rows, rows, "Invalid option",
            ItemListSpec.Change(Page.Options.Table, option, option.With(column, value)), Check);
        Assert.False(result.Ok);
        Assert.Contains(message, result.Message);
        Assert.Empty(drafts.Draft!.NetEdits());
    }

    [Theory]
    [InlineData(0, 0f, "at least one")]
    [InlineData(0, 1f, "needs a broadcast")]
    [InlineData(123, -1f, "nonnegative")]
    public void InvalidGreetingEditsAreAtomic(uint broadcast, float weight, string message)
    {
        var rows = Rows();
        var before = rows.Get(TcTables.NpcText, "100")!;
        using var drafts = new ContentDrafts(TempPath(), _ => TcTables.Instance);
        var result = drafts.Commit(null, "tc", rows, rows, "Invalid greeting", TcGossipRules.SetVariant(before, 0, broadcast, weight), Check);
        Assert.False(result.Ok);
        Assert.Contains(message, result.Message);
        Assert.Empty(drafts.Draft!.NetEdits());
        Assert.Throws<ArgumentOutOfRangeException>(() => TcGossipRules.SetVariant(before, 8, 1, 1));
    }

    [Fact]
    public void AllEightVariantsTextOverridesAndOptionLifecyclePersistAndExport()
    {
        var rows = Rows();
        string path = TempPath();
        using (var drafts = new ContentDrafts(path, _ => TcTables.Instance))
        {
            RowSet Effective() => drafts.Draft!.Effective("tc", rows);
            void Commit(string label, params RowEdit[] edits) => Assert.True(drafts.Commit(null, "tc", rows, Effective(), label, edits, Check).Ok);
            var before = rows.Get(TcTables.NpcText, "100")!;
            var after = before;
            for (int i = 0; i < 8; i++) after = after.With($"BroadcastTextID{i}", (uint)(123 + i)).With($"Probability{i}", .125f);
            Commit("Set greetings", ItemListSpec.Change(TcTables.NpcText, before, after));
            var option = Page.Options.Select(Effective(), 42).Single();
            Commit("Broadcasts", ItemListSpec.Change(Page.Options.Table, option, option.With("OptionBroadcastTextID", 123u).With("BoxBroadcastTextID", 124u)));
            option = Page.Options.Select(Effective(), 42).Single();
            Commit("Literal option", Page.ChangeText(option, "Hello $N"));
            option = Page.Options.Select(Effective(), 42).Single();
            Commit("Literal confirmation", Page.ChangeConfirmation(option, "Proceed?"));
            option = Page.Options.Select(Effective(), 42).Single();
            Assert.Equal(0u, option.U("OptionBroadcastTextID"));
            Assert.Equal(0u, option.U("BoxBroadcastTextID"));
            Commit("Add", Page.Options.Add(Effective(), 42, ("SpellID", -1), ("ActionMenuID", 90u), ("BoxMoney", uint.MaxValue)));
            Assert.Equal(2, Page.Options.Select(Effective(), 42).Count());
            var added = Page.Options.Select(Effective(), 42).Last();
            Commit("Remove", Page.Options.Remove(added));
            Assert.Single(Page.Options.Select(Effective(), 42));
            drafts.History(null, false);
            Assert.Equal(2, Page.Options.Select(Effective(), 42).Count());
            drafts.History(null, true);
            Assert.Single(Page.Options.Select(Effective(), 42));
            var sql = SqlExport.Build("tc", [], [], drafts.Draft!.NetEdits().Select(e => e.Edit).ToArray(), DateTime.UnixEpoch, schema: TcSchema.Instance);
            Assert.Contains("`Probability7` = 0.125", sql.Apply);
            Assert.Contains("`BroadcastTextID7` = 130", sql.Apply);
            Assert.Contains("`BroadcastTextID7` = 0", sql.Revert);
            Assert.Contains("Hello $N", sql.Apply);
            Assert.DoesNotContain("text0_0", sql.Apply);
            Assert.DoesNotContain("UPDATE `broadcast_text`", sql.Apply);
        }
        using var reopened = new ContentDrafts(path, _ => TcTables.Instance);
        var effective = reopened.Draft!.Effective("tc", rows);
        Assert.Equal(130u, effective.Get(TcTables.NpcText, "100")!.U("BroadcastTextID7"));
        Assert.Equal("Proceed?", Page.Options.Select(effective, 42).Single().T("BoxText"));
    }

    [Fact]
    public void RemovingAnExistingOptionExportsItsOriginalRowForRevert()
    {
        var rows = Rows();
        var before = Page.Options.Select(rows, 42).Single();
        using var drafts = new ContentDrafts(TempPath(), _ => TcTables.Instance);
        Assert.True(drafts.Commit(null, "tc", rows, rows, "Remove", [Page.Options.Remove(before)], Check).Ok);
        var sql = SqlExport.Build("tc", [], [], drafts.Draft!.NetEdits().Select(e => e.Edit).ToArray(), DateTime.UnixEpoch, schema: TcSchema.Instance);
        Assert.Contains("DELETE FROM `gossip_menu_option`", sql.Apply);
        Assert.Contains("INSERT INTO `gossip_menu_option`", sql.Revert);
        Assert.Contains("`MenuID` = 42", sql.Apply);
        Assert.Contains("`OptionID` = 0", sql.Apply);
        drafts.History(null, false);
        Assert.Equal(before, Page.Options.Select(drafts.Draft.Effective("tc", rows), 42).Single());
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void NonfiniteWeightsAreRejected(float weight)
    {
        var rows = Rows();
        var greeting = rows.Get(TcTables.NpcText, "100")!;
        using var drafts = new ContentDrafts(TempPath(), _ => TcTables.Instance);
        Assert.False(drafts.Commit(null, "tc", rows, rows, "Invalid weight", TcGossipRules.SetVariant(greeting, 0, 123, weight), Check).Ok);
        Assert.Empty(drafts.Draft!.NetEdits());
    }

    [Fact]
    public void ExistingErrorsAllowUnrelatedEditsAndCorrections()
    {
        var rows = Rows();
        var option = Page.Options.Select(rows, 42).Single().With("BoxCoded", 2u);
        rows.Set(Page.Options.Table, option);
        using var drafts = new ContentDrafts(TempPath(), _ => TcTables.Instance);
        Assert.True(drafts.Commit(null, "tc", rows, rows, "Text", Page.ChangeText(option, "Hi"), Check).Ok);
        var current = Page.Options.Select(drafts.Draft!.Effective("tc", rows), 42).Single();
        Assert.True(drafts.Commit(null, "tc", rows, drafts.Draft.Effective("tc", rows), "Fix code",
            ItemListSpec.Change(Page.Options.Table, current, current.With("BoxCoded", 1u)), Check).Ok);
        Assert.Empty(Check(drafts.Draft.Effective("tc", rows)));
    }
}
