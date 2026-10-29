using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class GossipPageTests
{
    public static TheoryData<WorldDialect> Dialects => new() { WorldDialect.AzerothCore, WorldDialect.TrinityCore };

    [Theory]
    [MemberData(nameof(Dialects))]
    public void OptionsHaveCompleteDefaultsAndSelectOnlyTheirMenuInOrder(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        var page = schema.GossipPage;
        var spec = page.Options;
        var rows = new RowSet();
        var first = spec.Add(rows, 42).After!;
        Assert.Equal(0u, first.U("OptionID"));
        rows.Set(spec.Table, first.With("OptionID", 8u));
        rows.Set(spec.Table, spec.Add(rows, 43, ("OptionID", 100u)).After!);
        var next = spec.Add(rows, 42).After!;
        Assert.Equal(9u, next.U("OptionID"));
        rows.Set(spec.Table, next);
        rows.Set(spec.Table, first);
        Assert.Equal(new uint[] { 0, 8, 9 }, spec.Select(rows, 42).Select(r => r.U("OptionID")));
        first.Validate(spec.Table);
        Assert.Equal(spec.Table.Columns.Length - 2, spec.Fields.Length);
        Assert.All(spec.Fields, field =>
        {
            Assert.Same(schema.Tables.Find(field.Table.Name), field.Table);
            Assert.True(field.Table.Has(field.Column));
            Assert.Equal(RowBinding.Item, field.Row);
        });
        Assert.Same(schema.Tables.Find(page.TextTable.Name), page.TextTable);
        var link = page.Menus.Add(rows, 42, ("TextID", 123u)).After!;
        rows.Set(page.Menus.Table, link);
        Assert.Equal(link, Assert.Single(page.Menus.Select(rows, 42)));
        Assert.Empty(page.Menus.Select(rows, 43));
        Assert.Equal(dialect == WorldDialect.AzerothCore ? 1u : 0u,
            first.U(dialect == WorldDialect.AzerothCore ? "OptionType" : "OptionNpc"));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void LiteralTextClearsBroadcastTogetherAndUndoRestoresBoth(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        var page = schema.GossipPage;
        var rows = new RowSet();
        var before = page.Options.Add(rows, 42, ("OptionText", "Old"), ("OptionBroadcastTextID", 123)).After!;
        rows.Set(page.Options.Table, before);
        using var draft = Draft(schema);
        draft.Execute("source", rows, "Edit text", page.ChangeText(before, "New"));
        var after = Assert.Single(page.Options.Select(draft.Effective("source", rows), 42));
        Assert.Equal("New", after.T("OptionText"));
        Assert.Equal(0u, after.U("OptionBroadcastTextID"));
        var sql = Export(schema, draft);
        Assert.Contains("`OptionBroadcastTextID` = 0", sql.Apply);
        Assert.Contains("`OptionBroadcastTextID` = 123", sql.Revert);
        draft.Undo();
        Assert.Equal(before, Assert.Single(page.Options.Select(draft.Effective("source", rows), 42)));
        Assert.Empty(draft.NetEdits());
        draft.Redo();
        Assert.Equal(after, Assert.Single(page.Options.Select(draft.Effective("source", rows), 42)));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void ReorderingPreservesAllFieldsAndDeletesBothKeysBeforeInserting(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        var page = schema.GossipPage;
        var rows = new RowSet();
        var first = page.Options.Add(rows, 42, ("OptionID", 3u), ("OptionText", "First"), ("ActionMenuID", 90u)).After!;
        var second = page.Options.Add(rows, 42, ("OptionID", 8u), ("OptionText", "Second"), ("BoxMoney", 123u)).After!;
        rows.Set(page.Options.Table, first);
        rows.Set(page.Options.Table, second);
        var edits = page.SwapOptions(first, second);
        Assert.All(edits.Take(2), e => Assert.Null(e.After));
        Assert.All(edits.Skip(2), e => Assert.Null(e.Before));
        using var draft = Draft(schema);
        draft.Execute("source", rows, "Reorder", edits);
        Assert.Equal(new[] { second.With("OptionID", 3u), first.With("OptionID", 8u) },
            page.Options.Select(draft.Effective("source", rows), 42));
        var sql = Export(schema, draft);
        Assert.Contains("`OptionID` = 3", sql.Apply);
        Assert.Contains("`OptionID` = 8", sql.Revert);
        draft.Undo();
        Assert.Empty(draft.NetEdits());
        draft.Redo();
        Assert.Equal("Second", page.Options.Select(draft.Effective("source", rows), 42).First().T("OptionText"));
        Assert.Throws<InvalidDataException>(() => page.SwapOptions(first, first));
        Assert.Throws<InvalidDataException>(() => page.SwapOptions(first, second.With("MenuID", 43u)));
    }

    [Fact]
    public void RetailOptionsKeepWideCostsSignedFieldsAndUnsignedOrder()
    {
        var spec = TcSchema.Instance.GossipPage.Options;
        var rows = new RowSet();
        var high = spec.Add(rows, 42, ("OptionID", uint.MaxValue), ("BoxMoney", ulong.MaxValue), ("SpellID", -1)).After!;
        rows.Set(spec.Table, high);
        var low = spec.Add(new(), 42).After!;
        rows.Set(spec.Table, low);
        Assert.Equal(new[] { low, high }, spec.Select(rows, 42));
        Assert.Equal(ulong.MaxValue, high.UL("BoxMoney"));
        Assert.Equal(-1, high.I("SpellID"));
        Assert.Throws<OverflowException>(() => spec.Add(rows, 42));
    }

    static InteractionDraft Draft(IWorldSchema schema) => new(
        Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "gossip.json"), schema.Tables);

    static SqlScripts Export(IWorldSchema schema, InteractionDraft draft) => SqlExport.Build("source", [],
        draft.NetEdits().Select(e => e.Edit).ToArray(), DateTime.UnixEpoch, schema: schema);
}
