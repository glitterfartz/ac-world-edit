using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class InteractionConflictTests
{
    static string TempPath() => Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "interactions.json");
    static Row Template(TableSchema table) => new Row(table.Columns.Select(c => new KeyValuePair<string, string>(c.Name, c.Kind == ColumnKind.Text ? "" : "0"))).With("entry", 295u);

    [Theory]
    [InlineData(WorldDialect.AzerothCore)]
    [InlineData(WorldDialect.TrinityCore)]
    public void DiscardedTemplateEditNeitherConflictsNorReplacesCurrentDatabaseValues(WorldDialect dialect)
    {
        var tables = WorldSchemas.For(dialect).Tables;
        var table = tables.Find("creature_template");
        var baseline = Template(table);
        var snapshot = new RowSet();
        snapshot.Set(table, baseline);
        var current = snapshot.Clone();
        var databaseRow = baseline.With("subname", "Current database value");
        current.Set(table, databaseRow);
        string path = TempPath();
        using (var draft = new InteractionDraft(path, tables))
        {
            draft.Execute("db", snapshot, "Add flag", [InteractionEdits.Update(table, baseline, baseline.With("npcflag", 0x800000u))]);
            draft.Revert("db", Assert.Single(draft.NetEdits()).Edit);
            Assert.Empty(draft.NetEdits());
            Assert.Empty(draft.Conflicts("db", current));
            Assert.Equal(databaseRow, draft.Effective("db", current).Get(table, "295"));
            Assert.Equal(databaseRow, draft.Frozen("db")(current).Get(table, "295"));
            draft.Undo();
            Assert.Single(draft.Conflicts("db", current));
            Assert.Equal(databaseRow, draft.Effective("db", current).Get(table, "295"));
            draft.Redo();
        }
        using var reopened = new InteractionDraft(path, tables);
        Assert.Empty(reopened.Conflicts("db", current));
        Assert.Equal(databaseRow, reopened.Effective("db", current).Get(table, "295"));
        reopened.Execute("db", current, "Change current title", [InteractionEdits.Update(table, databaseRow, databaseRow.With("subname", "New title"))]);
        Assert.Equal("New title", reopened.Effective("db", current).Get(table, "295")!.T("subname"));
    }

    [Theory]
    [InlineData(WorldDialect.AzerothCore, false)]
    [InlineData(WorldDialect.TrinityCore, false)]
    [InlineData(WorldDialect.AzerothCore, true)]
    [InlineData(WorldDialect.TrinityCore, true)]
    public void CancelledInsertOrDeleteDoesNotHideOrResurrectDatabaseRows(WorldDialect dialect, bool deleteFirst)
    {
        var tables = WorldSchemas.For(dialect).Tables;
        var table = tables.Find("creature_template");
        var row = Template(table);
        var snapshot = new RowSet();
        if (deleteFirst) snapshot.Set(table, row);
        using var draft = new InteractionDraft(TempPath(), tables);
        draft.Execute("db", snapshot, "Change existence", [deleteFirst ? InteractionEdits.Delete(table, row) : InteractionEdits.Insert(table, row)]);
        draft.Revert("db", Assert.Single(draft.NetEdits()).Edit);
        var current = new RowSet();
        if (!deleteFirst) current.Set(table, row.With("subname", "Added in database"));
        Assert.Empty(draft.Conflicts("db", current));
        Assert.Equal(current.Get(table, "295"), draft.Effective("db", current).Get(table, "295"));
    }
}
