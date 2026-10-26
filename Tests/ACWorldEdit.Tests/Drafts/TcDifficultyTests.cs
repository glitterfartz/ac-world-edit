using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>Adding a creature_template_difficulty row from the TrinityCore Template page.</summary>
public class TcDifficultyTests
{
    static readonly DifficultySpec Spec = TcCreaturePage.Page.Difficulty!;
    static string TempPath() => Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "difficulty.json");
    static IEnumerable<InteractionIssue> NoIssues(RowSet rows) => [];

    [Fact]
    public void ANewRowHasEveryColumnWithTheSchemaDefaults()
    {
        var row = Spec.Add(new RowSet(), 5, 2).After!;
        row.Validate(TcTables.CreatureDifficulty);
        Assert.Equal(5u, row.U("Entry"));
        Assert.Equal(2, row.I("DifficultyID"));
        Assert.Equal(1f, row.F("HealthModifier"));
        Assert.Equal(1f, row.F("DamageModifier"));
        Assert.Equal(0u, row.U("LootID"));
    }

    [Fact]
    public void ACopyKeepsTheShownRowsValuesUnderTheNewKey()
    {
        var rows = new RowSet();
        var normal = Spec.Add(rows, 5, 0).After!.With("ContentTuningID", 864).With("LootID", 99u);
        rows.Set(Spec.Table, normal);
        var heroic = Spec.Add(rows, 5, 2, normal).After!;
        Assert.Equal(2, heroic.I("DifficultyID"));
        Assert.Equal(864, heroic.I("ContentTuningID"));
        Assert.Equal(99u, heroic.U("LootID"));
    }

    [Theory]
    [InlineData(-1, "between 0 and 255")]
    [InlineData(256, "between 0 and 255")]
    [InlineData(0, "already has a row")]
    public void ExistingOrOutOfRangeDifficultiesAreRefused(int id, string message)
    {
        var rows = new RowSet();
        rows.Set(Spec.Table, Spec.Add(rows, 5, 0).After!);
        Assert.Contains(message, Spec.CannotAdd(rows, 5, id));
        Assert.Contains(message, Assert.Throws<InvalidDataException>(() => Spec.Add(rows, 5, id)).Message);
        Assert.Null(Spec.CannotAdd(rows, 6, 0));
    }

    [Fact]
    public void AnAddedRowPersistsUndoesAndExports()
    {
        var rows = new RowSet();
        string path = TempPath();
        using (var drafts = new ContentDrafts(path, _ => TcTables.Instance))
        {
            Assert.True(drafts.Commit(null, "tc", rows, rows, "Add", [Spec.Add(rows, 5, 1)], NoIssues).Ok);
            Assert.Single(Spec.Select(drafts.Draft!.Effective("tc", rows), 5));
            var sql = SqlExport.Build("tc", [], [], drafts.Draft.NetEdits().Select(e => e.Edit).ToArray(), DateTime.UnixEpoch, schema: TcSchema.Instance);
            Assert.Contains("`creature_template_difficulty`", sql.Apply);
            Assert.Contains("`HealthModifier`", sql.Apply);
            drafts.History(null, false);
            Assert.Empty(Spec.Select(drafts.Draft.Effective("tc", rows), 5));
            drafts.History(null, true);
        }

        using var reopened = new ContentDrafts(path, _ => TcTables.Instance);
        Assert.Equal(1, Spec.Select(reopened.Draft!.Effective("tc", rows), 5).Single().I("DifficultyID"));
    }
}
