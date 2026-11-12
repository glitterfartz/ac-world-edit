using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>
/// Live, read-only checks that the rows offered for removal are exactly the rows the counts name.
/// Each test finds a real referenced GUID first, so it does not depend on particular data.
/// </summary>
[Trait("Category", "Db")]
public class ReferenceCleanupDbTests
{
    [DbFact]
    public async Task AzerothCoreReturnsTheRowsItCounts() => await Check(new WorldConnection(Config, null, WorldDialect.AzerothCore));

    [TcDbFact]
    public async Task TrinityCoreReturnsTheRowsItCounts() => await Check(Environments.TrinityConnection()!);

    static async Task Check(WorldConnection connection)
    {
        var source = DBSource.Open(connection);
        await using var session = await new WorldDatabase(connection).OpenAsync(CancellationToken.None);
        await using var command = session.ReadOnly("SELECT guid FROM game_event_creature LIMIT 1");
        if (await command.ExecuteScalarAsync() is not { } picked || picked is DBNull)
        {
            return;
        }

        ulong guid = Convert.ToUInt64(picked);
        var references = await source.ReadSpawnReferencesAsync(SpawnKind.Creature, [guid], default);
        Assert.True(references.Total > 0);
        Assert.True(references.CanRemove);

        // The counts and the rows come from one read: each distinct row is counted once.
        Assert.Equal(references.Total, references.Rows.Names.Sum(name => references.Rows.All(name).Count));
        int eventRows = references.Rows.All("game_event_creature").Count;
        Assert.Equal(references.Counts.Where(c => c.Reference.Table == "game_event_creature").Sum(c => c.Count), eventRows);

        // Everything read can be deleted by the same catalog the draft validates against, and each
        // row survives that schema's validation (the real proof that no column kind is wrong).
        var catalog = connection.Dialect == WorldDialect.TrinityCore ? (ITableCatalog)TcTables.Instance : AcTables.Instance;
        foreach (string name in references.Rows.Names)
        {
            var table = Assert.Single(catalog.All, candidate => candidate.Name == name);
            Assert.NotEmpty(references.Rows.All(name));
            foreach (var row in references.Rows.All(name))
            {
                row.Validate(table);
            }
        }

        var none = await source.ReadSpawnReferencesAsync(SpawnKind.Creature, [4_000_000_000UL], default);
        Assert.Empty(none.Counts);
        Assert.False(none.CanRemove);

        var empty = await source.ReadSpawnReferencesAsync(SpawnKind.Creature, [], default);
        Assert.Empty(empty.Counts);
        Assert.False(empty.CanRemove);
    }
}
