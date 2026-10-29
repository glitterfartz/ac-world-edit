using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>
/// Live, read-only checks of the references a spawn delete would leave behind. Each test finds a real
/// referenced GUID in the database first, so it does not depend on particular data.
/// </summary>
[Trait("Category", "Db")]
public class SpawnImpactDbTests
{
    /// <summary>The first GUID the statement returns, or null when the table has none.</summary>
    static async Task<ulong?> Find(WorldConnection connection, string sql)
    {
        await using var session = await new WorldDatabase(connection).OpenAsync(CancellationToken.None);
        await using var command = session.ReadOnly(sql);
        return await command.ExecuteScalarAsync() is { } value and not DBNull ? Convert.ToUInt64(value) : null;
    }

    static async Task Check(WorldConnection connection)
    {
        var source = DBSource.Open(connection);
        bool tc = connection.Dialect == WorldDialect.TrinityCore;

        // A spawn nothing references reports nothing, and an empty request is empty.
        Assert.Empty(await source.CountSpawnReferencesAsync(SpawnKind.Creature, [4_000_000_000UL], default));
        Assert.Empty(await source.CountSpawnReferencesAsync(SpawnKind.Creature, [], default));

        // game event membership, pool membership and per-spawn SmartAI each show up under their own table.
        var cases = new (string Table, string Sql)[]
        {
            ("game_event_creature", "SELECT guid FROM game_event_creature LIMIT 1"),
            (tc ? "pool_members" : "pool_creature", tc ? "SELECT spawnId FROM pool_members WHERE type=0 LIMIT 1" : "SELECT guid FROM pool_creature LIMIT 1"),
            ("smart_scripts", "SELECT -s.entryorguid FROM smart_scripts s JOIN creature c ON c.guid=-s.entryorguid WHERE s.source_type=0 AND s.entryorguid<0 LIMIT 1")
        };
        foreach (var (table, sql) in cases)
        {
            ulong guid = await Find(connection, sql) ?? throw new InvalidOperationException($"No {table} row to test with.");
            var items = await source.CountSpawnReferencesAsync(SpawnKind.Creature, [guid], default);
            Assert.True(items.Any(i => i.Reference.Table == table && i.Count >= 1), $"{table}: guid {guid} not reported; got {string.Join(", ", items.Select(i => i.Reference.Table))}");
            Assert.All(items, i => Assert.True(i.Count > 0));
        }

        // Many GUIDs at once add up per table.
        var several = new ulong[] { await Find(connection, "SELECT guid FROM game_event_creature ORDER BY guid LIMIT 1") ?? 0, await Find(connection, "SELECT guid FROM game_event_creature ORDER BY guid DESC LIMIT 1") ?? 0 };
        var both = await source.CountSpawnReferencesAsync(SpawnKind.Creature, several, default);
        Assert.True(both.Where(i => i.Reference.Table == "game_event_creature").Sum(i => i.Count) >= 2);
    }

    [DbFact]
    public async Task AzerothCoreReportsWhatAnExportedDeleteWouldLeaveBehind() => await Check(new WorldConnection(Config, null, WorldDialect.AzerothCore));

    [TcDbFact]
    public async Task TrinityCoreReportsWhatAnExportedDeleteWouldLeaveBehind() => await Check(Environments.TrinityConnection()!);

    /// <summary>A creature whose path nothing else uses is reported as an orphan-to-be; a shared path is not.</summary>
    static async Task CheckPaths(WorldConnection connection)
    {
        var source = DBSource.Open(connection);
        bool tc = connection.Dialect == WorldDialect.TrinityCore;
        string column = tc ? "PathId" : "path_id", nodes = tc ? "waypoint_path_node" : "waypoint_data", key = tc ? "PathId" : "id";
        string others = $"(SELECT COUNT(*) FROM creature_addon o WHERE o.{column}=a.{column} AND o.guid<>a.guid)+(SELECT COUNT(*) FROM creature_template_addon t WHERE t.{column}=a.{column})";
        string from = $"FROM creature_addon a WHERE a.{column}<>0 AND EXISTS(SELECT 1 FROM {nodes} n WHERE n.{key}=a.{column})";

        ulong alone = await Find(connection, $"SELECT a.guid {from} AND {others}=0 LIMIT 1") ?? throw new InvalidOperationException("No creature with a path of its own to test with.");
        var use = Assert.Single(await source.ReadSpawnPathsAsync([alone], default));
        Assert.Equal((alone, 0), (use.Guid, use.OtherUsers));
        Assert.True(use.Nodes > 0 && use.PathId > 0 && use.WouldBeOrphaned);

        ulong shared = await Find(connection, $"SELECT a.guid {from} AND {others}>0 LIMIT 1") ?? throw new InvalidOperationException("No creature on a shared path to test with.");
        var common = Assert.Single(await source.ReadSpawnPathsAsync([shared], default));
        Assert.True(common.OtherUsers > 0 && !common.WouldBeOrphaned);

        Assert.Empty(await source.ReadSpawnPathsAsync([4_000_000_000UL], default));
        Assert.Empty(await source.ReadSpawnPathsAsync([], default));
    }

    [DbFact]
    public async Task AzerothCoreReportsThePathADeletedCreatureWouldLeaveUnused() => await CheckPaths(new WorldConnection(Config, null, WorldDialect.AzerothCore));

    [TcDbFact]
    public async Task TrinityCoreReportsThePathADeletedCreatureWouldLeaveUnused() => await CheckPaths(Environments.TrinityConnection()!);

    /// <summary>The scalar of a statement, as a number.</summary>
    static async Task<long> Scalar(WorldConnection connection, string sql)
    {
        await using var session = await new WorldDatabase(connection).OpenAsync(CancellationToken.None);
        await using var command = session.ReadOnly(sql);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    static async Task CheckTemplateAddon(WorldConnection connection)
    {
        var source = DBSource.Open(connection);
        var schema = source.Schema;
        uint entry = (uint)(await Find(connection, "SELECT t.entry FROM creature_template_addon t WHERE EXISTS(SELECT 1 FROM creature c WHERE c.id=t.entry) ORDER BY t.entry LIMIT 1") ?? throw new InvalidOperationException("No creature template with an addon and spawns."));

        var row = await source.ReadTemplateAddonAsync(entry, default);
        Assert.NotNull(row);
        row!.Validate(SpawnEdits.TemplateAddonTable(schema));
        Assert.Equal(entry.ToString(), row["entry"]);
        // The override of a spawn starts as exactly this appearance.
        var copy = SpawnEdits.AddonFromTemplate(schema, row, 5);
        copy.Validate(SpawnEdits.AddonTable(schema, SpawnKind.Creature));
        Assert.All(SpawnEdits.AddonTable(schema, SpawnKind.Creature).Columns.Select(c => c.Name).Where(n => n != "guid"), c => Assert.Equal(row[c], copy[c]));

        Assert.Null(await source.ReadTemplateAddonAsync(uint.MaxValue, default));

        var use = await source.CountTemplateUseAsync(entry, default);
        Assert.Equal(await Scalar(connection, $"SELECT COUNT(*) FROM creature WHERE id={entry}"), use.Spawns);
        Assert.Equal(await Scalar(connection, $"SELECT COUNT(*) FROM creature c JOIN creature_addon a ON a.guid=c.guid WHERE c.id={entry}"), use.WithOwnAppearance);
        Assert.Equal(use.Spawns - use.WithOwnAppearance, use.Affected);
        Assert.Equal(new TemplateUse(0, 0), await source.CountTemplateUseAsync(uint.MaxValue, default));
    }

    [DbFact]
    public async Task AzerothCoreReadsATemplateAddonAndHowItsSpawnsShareIt() => await CheckTemplateAddon(new WorldConnection(Config, null, WorldDialect.AzerothCore));

    [TcDbFact]
    public async Task TrinityCoreReadsATemplateAddonAndHowItsSpawnsShareIt() => await CheckTemplateAddon(Environments.TrinityConnection()!);
}
