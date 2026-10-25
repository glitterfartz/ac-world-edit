using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class ItemListTests
{
    public static TheoryData<WorldDialect> Dialects => new() { WorldDialect.AzerothCore, WorldDialect.TrinityCore };

    [Theory]
    [MemberData(nameof(Dialects))]
    public void VendorSelectionFiltersParentsAndAcReferencesAndOrdersBySlot(WorldDialect dialect)
    {
        var spec = WorldSchemas.For(dialect).VendorPage;
        var rows = new RowSet();
        rows.Set(spec.Table, spec.Add(rows, 42, ("item", 100), ("slot", 5)).After!);
        rows.Set(spec.Table, spec.Add(rows, 43, ("item", 101), ("slot", 1)).After!);
        rows.Set(spec.Table, spec.Add(rows, 42, ("item", -200), ("slot", 0)).After!);
        rows.Set(spec.Table, spec.Add(rows, 42, ("item", 102), ("slot", 2)).After!);
        Assert.Equal(dialect == WorldDialect.AzerothCore ? new[] { 102, 100 } : new[] { -200, 102, 100 }, spec.Select(rows, 42).Select(r => r.I("item")));
        var next = spec.Add(rows, 42, ("item", 103)).After!;
        Assert.Equal(6, next.I("slot"));
        next.Validate(spec.Table);
        Assert.Throws<InvalidDataException>(() => spec.Add(rows, 42, ("entry", 43)));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void AlternatePriceChangeUndoesTogetherAndExportsBothKeys(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        var spec = schema.VendorPage;
        var rows = new RowSet();
        var before = spec.Add(rows, 42, ("item", 100)).After!;
        rows.Set(spec.Table, before);
        var after = before.With("ExtendedCost", 7u);
        string path = Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "items.json");
        using var draft = new InteractionDraft(path, schema.Tables);
        draft.Execute("source", rows, "Change price", ItemListSpec.Change(spec.Table, before, after));
        Assert.Equal(after, Assert.Single(spec.Select(draft.Effective("source", rows), 42)));
        var sql = SqlExport.Build("source", [], [], draft.NetEdits().Select(e => e.Edit).ToArray(), DateTime.UnixEpoch, schema: schema);
        Assert.Contains("DELETE FROM `npc_vendor`", sql.Apply);
        Assert.Contains("INSERT INTO `npc_vendor`", sql.Apply);
        Assert.Contains("`ExtendedCost` = 0", sql.Apply);
        Assert.Contains("`ExtendedCost` = 7", sql.Revert);
        draft.Undo();
        Assert.Equal(before, Assert.Single(spec.Select(draft.Effective("source", rows), 42)));
        Assert.Empty(draft.NetEdits());
        draft.Redo();
        Assert.Equal(after, Assert.Single(spec.Select(draft.Effective("source", rows), 42)));
        draft.Execute("source", rows, "Remove item", [spec.Remove(after)]);
        Assert.Empty(spec.Select(draft.Effective("source", rows), 42));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void NonKeyChangesStayUpdatesAndDescriptorsUseTheirOwnCatalog(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        var spec = schema.VendorPage;
        Assert.Same(schema.Tables.Find(spec.Table.Name), spec.Table);
        Assert.All(spec.Fields, field =>
        {
            Assert.Same(spec.Table, field.Table);
            Assert.True(spec.Table.Has(field.Column));
            Assert.Equal(RowBinding.Item, field.Row);
        });
        var before = spec.Add(new(), 42, ("item", 100)).After!;
        var edit = Assert.Single(ItemListSpec.Change(spec.Table, before, before.With("slot", -1)));
        Assert.Equal(before, edit.Before);
        Assert.Equal("-1", edit.After!.T("slot"));
        if (dialect == WorldDialect.TrinityCore)
        {
            Assert.Equal(1u, before.U("type"));
            Assert.Equal(spec.Table.Columns.Length - 1, spec.Fields.Length);
            var currency = before.With("type", 2u);
            Assert.NotEqual(spec.Table.KeyOf(before), spec.Table.KeyOf(currency));
            Assert.Equal(2, ItemListSpec.Change(spec.Table, before, currency).Length);
        }
    }
}
