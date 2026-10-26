using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class TcTrainerTests
{
    static readonly TrainerPageSpec Page = TcSchema.Instance.TrainerPage;
    static string TempPath() => Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "trainer.json");
    static RowSet Rows()
    {
        var rows = new RowSet();
        rows.Set(TcTables.Trainer, Row.Of(("Id", 7u), ("Type", 0u), ("Greeting", "Hello")));
        return rows;
    }
    static IEnumerable<InteractionIssue> Check(RowSet rows) => TcTrainerRules.Check(rows, id => id is 100 or 101 or 102);

    [Theory]
    [InlineData("SpellId", "0", "spell ID")]
    [InlineData("SpellId", "999", "does not exist")]
    [InlineData("ReqLevel", "256", "unsigned byte")]
    [InlineData("ReqAbility2", "999", "required ability 2")]
    public void InvalidSpellEditsAreRefusedWithoutChangingHistory(string column, string value, string message)
    {
        var rows = Rows();
        var spell = Page.Spells.Add(rows, 7, ("SpellId", 100u)).After!;
        rows.Set(Page.Spells.Table, spell);
        using var drafts = new ContentDrafts(TempPath(), _ => TcTables.Instance);
        var result = drafts.Commit(null, "tc", rows, rows, "Edit spell", ItemListSpec.Change(Page.Spells.Table, spell, spell.With(column, value)), Check);
        Assert.False(result.Ok);
        Assert.Contains(message, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(drafts.Draft!.NetEdits());
    }

    [Fact]
    public void TrainerTypeIsLimitedToKnownKinds()
    {
        var rows = Rows();
        var trainer = rows.Get(TcTables.Trainer, "7")!;
        using var drafts = new ContentDrafts(TempPath(), _ => TcTables.Instance);
        Assert.False(drafts.Commit(null, "tc", rows, rows, "Type", ItemListSpec.Change(TcTables.Trainer, trainer, trainer.With("Type", 3u)), Check).Ok);
        Assert.True(drafts.Commit(null, "tc", rows, rows, "Type", ItemListSpec.Change(TcTables.Trainer, trainer, trainer.With("Type", 1u)), Check).Ok);
    }

    [Fact]
    public void SpellLifecyclePersistsUndoesAndExports()
    {
        var rows = Rows();
        string path = TempPath();
        using (var drafts = new ContentDrafts(path, _ => TcTables.Instance))
        {
            Assert.True(drafts.Commit(null, "tc", rows, rows, "Add", [Page.Spells.Add(rows, 7, ("SpellId", 100u))], Check).Ok);
            var effective = drafts.Draft!.Effective("tc", rows);
            var spell = Page.Spells.Select(effective, 7).Single();
            var changed = spell.With("MoneyCost", 500u).With("ReqLevel", 20u).With("ReqAbility1", 101u);
            Assert.True(drafts.Commit(null, "tc", rows, effective, "Edit", ItemListSpec.Change(Page.Spells.Table, spell, changed), Check).Ok);
            // A second spell with the same key is a collision, not a silent overwrite.
            Assert.False(drafts.Commit(null, "tc", rows, drafts.Draft.Effective("tc", rows), "Duplicate", [Page.Spells.Add(effective, 7, ("SpellId", 100u))], Check).Ok);
            var sql = SqlExport.Build("tc", [], [], drafts.Draft.NetEdits().Select(e => e.Edit).ToArray(), DateTime.UnixEpoch, schema: TcSchema.Instance);
            Assert.Contains("`ReqAbility1`", sql.Apply);
            Assert.Contains("`trainer_spell`", sql.Revert);
            drafts.History(null, false);
            Assert.Equal(0u, Page.Spells.Select(drafts.Draft.Effective("tc", rows), 7).Single().U("MoneyCost"));
            drafts.History(null, true);
        }
        using var reopened = new ContentDrafts(path, _ => TcTables.Instance);
        var restored = reopened.Draft!.Effective("tc", rows);
        var saved = Page.Spells.Select(restored, 7).Single();
        Assert.Equal(500u, saved.U("MoneyCost"));
        Assert.Equal(101u, saved.U("ReqAbility1"));
        Assert.True(reopened.Commit(null, "tc", rows, restored, "Remove", [Page.Spells.Remove(saved)], Check).Ok);
        Assert.Empty(Page.Spells.Select(reopened.Draft.Effective("tc", rows), 7));
    }
}
