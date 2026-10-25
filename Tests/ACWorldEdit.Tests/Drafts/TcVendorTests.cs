using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class TcVendorTests
{
    static readonly ItemListSpec Spec = TcSchema.Instance.VendorPage;
    static RowSet Rows(ulong flags = 0x80)
    {
        var rows = new RowSet();
        rows.Set(TcTables.CreatureTemplate, Row.Of(("entry", 42u), ("npcflag", flags)));
        return rows;
    }
    static IEnumerable<InteractionIssue> Check(RowSet rows) => TcVendorRules.Check(42, rows, id => id is 100 or 101);
    static string TempPath() => Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "vendor.json");

    [Theory]
    [InlineData("slot", "-32769", "position")]
    [InlineData("slot", "32768", "position")]
    [InlineData("type", "3", "type")]
    [InlineData("item", "0", "ID")]
    [InlineData("item", "999", "ItemSparse")]
    [InlineData("maxcount", "5", "restock")]
    [InlineData("incrtime", "60", "restock")]
    [InlineData("BonusListIDs", "1 nope", "bonus")]
    [InlineData("BonusListIDs", "2147483648", "bonus")]
    [InlineData("IgnoreFiltering", "2", "filtering")]
    public void InvalidVendorEditsAreRefusedWithoutChangingHistory(string column, string value, string message)
    {
        var rows = Rows();
        var before = Spec.Add(rows, 42, ("item", 100)).After!;
        rows.Set(Spec.Table, before);
        using var drafts = new ContentDrafts(TempPath(), _ => TcTables.Instance);
        var result = drafts.Commit(null, "tc", rows, rows, "Edit vendor", ItemListSpec.Change(Spec.Table, before, before.With(column, value)), Check);
        Assert.False(result.Ok);
        Assert.Contains(message, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(drafts.Draft!.NetEdits());
    }

    [Fact]
    public void StockAndRetailFieldsPersistUndoRedoAndExportWithTypeKey()
    {
        var rows = Rows(0x100000080ul);
        var item = Spec.Add(rows, 42, ("item", 100)).After!;
        rows.Set(Spec.Table, item);
        string path = TempPath();
        using (var drafts = new ContentDrafts(path, _ => TcTables.Instance))
        {
            var after = item.With("maxcount", 300u).With("incrtime", 60u).With("BonusListIDs", "1 -2 3").With("PlayerConditionID", uint.MaxValue).With("IgnoreFiltering", 1u).With("slot", short.MinValue);
            Assert.True(drafts.Commit(null, "tc", rows, rows, "Set stock", ItemListSpec.Change(Spec.Table, item, after), Check).Ok);
            var effective = drafts.Draft!.Effective("tc", rows);
            var currency = Spec.Add(effective, 42, ("item", 100), ("type", 2u), ("maxcount", 1u));
            Assert.True(drafts.Commit(null, "tc", rows, effective, "Add currency", [currency], Check).Ok);
            Assert.Equal(2, Spec.Select(drafts.Draft.Effective("tc", rows), 42).Count());
            var sql = SqlExport.Build("tc", [], [], drafts.Draft.NetEdits().Select(e => e.Edit).ToArray(), DateTime.UnixEpoch, schema: TcSchema.Instance);
            Assert.Contains("`type` = 1", sql.Apply);
            Assert.Contains("`BonusListIDs`", sql.Apply);
            Assert.Contains("`PlayerConditionID`", sql.Revert);
            drafts.History(null, false);
            Assert.Single(Spec.Select(drafts.Draft.Effective("tc", rows), 42));
            drafts.History(null, false);
            Assert.Empty(drafts.Draft.NetEdits());
            drafts.History(null, true);
            drafts.History(null, true);
        }
        using var reopened = new ContentDrafts(path, _ => TcTables.Instance);
        var restored = reopened.Draft!.Effective("tc", rows);
        Assert.Equal(2, Spec.Select(restored, 42).Count());
        var saved = restored.Get(Spec.Table, Spec.Table.KeyOf(item))!;
        Assert.Equal(300u, saved.U("maxcount"));
        Assert.Equal("1 -2 3", saved.T("BonusListIDs"));
        Assert.True(reopened.Commit(null, "tc", rows, restored, "Remove", [Spec.Remove(saved)], Check).Ok);
        Assert.Equal(2u, Assert.Single(Spec.Select(reopened.Draft.Effective("tc", rows), 42)).U("type"));
    }

    [Fact]
    public void CurrencyQuantityAndVendorFlagAreRequiredButCurrencyRestockIsIgnored()
    {
        var rows = Rows(0x100);
        var currency = Spec.Add(rows, 42, ("item", 999), ("type", 2u)).After!;
        rows.Set(Spec.Table, currency);
        Assert.Contains(Check(rows), i => i.Message.Contains("Vendor NPC flag"));
        Assert.Contains(Check(rows), i => i.Message.Contains("quantity"));
        rows.Set(TcTables.CreatureTemplate, Row.Of(("entry", 42u), ("npcflag", 0x80ul)));
        rows.Set(Spec.Table, currency.With("maxcount", 1u).With("incrtime", 60u));
        Assert.Empty(Check(rows));
    }

    [Fact]
    public void ReferencesAndOtherCreaturesDoNotUseItemValidation()
    {
        var rows = Rows();
        rows.Set(Spec.Table, Spec.Add(rows, 42, ("item", -500)).After!);
        rows.Set(Spec.Table, Spec.Add(rows, 43, ("item", 999)).After!);
        Assert.Empty(Check(rows));
    }

    [Fact]
    public void DuplicateAddsAndKeyCollisionsPreserveBothRows()
    {
        var rows = Rows();
        var first = Spec.Add(rows, 42, ("item", 100)).After!;
        var second = Spec.Add(rows, 42, ("item", 101)).After!;
        rows.Set(Spec.Table, first);
        rows.Set(Spec.Table, second);
        using var drafts = new ContentDrafts(TempPath(), _ => TcTables.Instance);
        Assert.False(drafts.Commit(null, "tc", rows, rows, "Duplicate", [Spec.Add(rows, 42, ("item", 100))], Check).Ok);
        Assert.False(drafts.Commit(null, "tc", rows, rows, "Collision", ItemListSpec.Change(Spec.Table, first, first.With("item", 101)), Check).Ok);
        Assert.Empty(drafts.Draft!.NetEdits());
        Assert.Equal(2, Spec.Select(drafts.Draft.Effective("tc", rows), 42).Count());
    }
}
