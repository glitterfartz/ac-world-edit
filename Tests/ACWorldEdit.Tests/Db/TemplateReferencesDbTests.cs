using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>Live check that template reference counts match the database, on both dialects.</summary>
[Trait("Category", "Db")]
public class TemplateReferencesDbTests
{
    [DbFact]
    public async Task AzerothCoreCountsTheSpawnsOfATemplate() => await Check(new WorldConnection(Config, null, WorldDialect.AzerothCore));

    [TcDbFact]
    public async Task TrinityCoreCountsTheSpawnsOfATemplate() => await Check(Environments.TrinityConnection()!);

    static async Task Check(WorldConnection connection)
    {
        var source = DBSource.Open(connection);
        await using var session = await new WorldDatabase(connection).OpenAsync(CancellationToken.None);
        // AzerothCore's creature table names the entry id1 on recent schemas, as the reader probes.
        string id = session.Has("creature.id1") ? "id1" : "id";
        await using var pick = session.ReadOnly($"SELECT {id} FROM creature ORDER BY guid LIMIT 1");
        uint entry = Convert.ToUInt32(await pick.ExecuteScalarAsync());

        var counts = await source.CountTemplateReferencesAsync(SpawnKind.Creature, entry, default);
        var spawns = Assert.Single(counts, c => c.Reference.Table == "creature");

        await using var exact = session.ReadOnly($"SELECT COUNT(*) FROM creature WHERE {id}={entry}");
        Assert.Equal(Convert.ToInt32(await exact.ExecuteScalarAsync()), spawns.Count);
        Assert.All(counts, c => Assert.True(c.Count > 0));
        Assert.Empty(await source.CountTemplateReferencesAsync(SpawnKind.Creature, uint.MaxValue, default));
    }
}
