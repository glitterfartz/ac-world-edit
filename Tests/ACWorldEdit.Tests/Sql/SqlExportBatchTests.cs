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
}
