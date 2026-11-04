using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Sql;

/// <summary>
/// Pins the batch contract the viewer relies on: grouping and ordering by source, one pair per
/// source, and the eight-character file naming that users already depend on.
/// </summary>
public class SqlExportBatchTests
{
    static PendingChange Site(string source, uint id, double x) => new(
        $"id-{source}-{id}",
        PendingKind.Graveyard,
        source,
        $"Graveyard {id}",
        new GraveyardEdit(source, id, new GraveyardRecord(id, 0, x, 2, 3, "Before", []), new GraveyardRecord(id, 0, x + 1, 2, 3, "After", [])),
        null);

    static IWorldSchema Schema(string _) => WorldSchemas.For(WorldDialect.AzerothCore);

    [Fact]
    public void GroupsBySourceInOrdinalOrder()
    {
        var batch = SqlExportBatch.Build([Site("z_source", 1, 1), Site("a_source", 2, 2), Site("z_source", 3, 3)], Schema);

        Assert.Equal(["a_source", "z_source"], batch.Select(pair => pair.Source));
        Assert.Equal(1, batch[0].Scripts.Changes);
        Assert.Equal(2, batch[1].Scripts.Changes);
        Assert.Contains("graveyard", batch[1].Scripts.Apply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("graveyard", batch[1].Scripts.Revert, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyInputProducesNoScripts()
    {
        Assert.Empty(SqlExportBatch.Build([], Schema));
    }

    [Fact]
    public void WriteCreatesApplyAndRevertPerSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "acwe-sqlbatch-" + Guid.NewGuid().ToString("N"));
        try
        {
            var batch = SqlExportBatch.Build([Site("alpha", 1, 1), Site("bravo", 2, 2)], Schema);
            string folder = SqlExportBatch.Write(batch, root, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

            Assert.Equal(Path.Combine(root, "20260102-030405"), folder);
            Assert.True(File.Exists(Path.Combine(folder, "alpha-apply.sql")));
            Assert.True(File.Exists(Path.Combine(folder, "alpha-revert.sql")));
            Assert.True(File.Exists(Path.Combine(folder, "bravo-apply.sql")));
            Assert.Contains("graveyard", File.ReadAllText(Path.Combine(folder, "alpha-apply.sql")), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void FileNamesKeepOnlyTheFirstEightCharacters()
    {
        var root = Path.Combine(Path.GetTempPath(), "acwe-sqlbatch-" + Guid.NewGuid().ToString("N"));
        try
        {
            var batch = SqlExportBatch.Build([Site("a_very_long_source_name", 1, 1)], Schema);
            string folder = SqlExportBatch.Write(batch, root, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

            Assert.True(File.Exists(Path.Combine(folder, "a_very_l-apply.sql")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
