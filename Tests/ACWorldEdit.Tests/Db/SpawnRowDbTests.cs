using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>
/// Live, read-only checks that a spawn's full row loads through each dialect's catalog and survives a
/// pose edit into the exported SQL (including the nullable TrinityCore overrides).
/// </summary>
[Trait("Category", "Db")]
public class SpawnRowDbTests
{
    static readonly CreatureRegion Elwynn = new(0, 31, 33, 48, 50);
    static readonly DateTime Stamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    static async Task Check(IDBSource source, WorldDialect dialect)
    {
        var region = await source.ReadCreaturesAsync(Elwynn, new SpawnPreview { Dialect = dialect }, null, "", default);
        var spawn = region.Spawns.First();
        var rows = await source.ReadSpawnRowsAsync(SpawnKind.Creature, [spawn.Guid], default);
        var table = SpawnEdits.SpawnTable(source.Schema, SpawnKind.Creature);
        var row = Assert.Single(rows.All(table));
        row.Validate(table);
        Assert.Equal(spawn.Guid, row.UL("guid"));
        Assert.Equal(spawn.TemplateId, row.U("id"));
        Assert.True(Math.Abs(SpawnEdits.PoseOf(row).X - spawn.Pose.X) < 0.001);

        // The export of a one-column move guards the database value and touches only that column.
        var edit = SpawnEdits.Move(source.Schema, SpawnKind.Creature, row, SpawnEdits.PoseOf(row) with { Z = SpawnEdits.PoseOf(row).Z + 1 });
        string apply = SqlExport.Build("src", [], [edit], Stamp, null, source.Schema).Apply;
        Assert.Contains("UPDATE `creature` SET `position_z` =", apply);
        Assert.DoesNotContain("position_x` =", apply);

        Assert.Empty((await source.ReadSpawnRowsAsync(SpawnKind.Creature, [], default)).All(table));
        Assert.Empty((await source.ReadSpawnRowsAsync(SpawnKind.Creature, [ulong.MaxValue - 1], default)).All(table));
    }

    [DbFact]
    public async Task AzerothCoreCreatureRowsLoadAndExport() => await Check(AcSource(Config), WorldDialect.AzerothCore);

    [TcDbFact]
    public async Task TrinityCoreCreatureRowsKeepNullOverridesAndExport()
    {
        var source = DBSource.Open(Environments.TrinityConnection()!);
        await Check(source, WorldDialect.TrinityCore);

        // Most TDB spawns inherit their flags: NULL must come back as NULL, not as 0.
        var region = await source.ReadCreaturesAsync(Elwynn, new SpawnPreview { Dialect = WorldDialect.TrinityCore }, null, "", default);
        var rows = await source.ReadSpawnRowsAsync(SpawnKind.Creature, region.Spawns.Take(200).Select(s => s.Guid).ToArray(), default);
        Assert.Contains(rows.All(TcTables.SpawnCreature), r => r.IsNull("npcflag") && r.IsNull("unit_flags"));
    }

    [TcDbFact]
    public async Task TrinityCoreGameObjectRowsLoad()
    {
        var source = DBSource.Open(Environments.TrinityConnection()!);
        var region = await source.ReadGameObjectsAsync(Elwynn, new SpawnPreview { Dialect = WorldDialect.TrinityCore }, null, "", default);
        var spawn = region.Spawns.First();
        var row = Assert.Single((await source.ReadSpawnRowsAsync(SpawnKind.GameObject, [spawn.Guid], default)).All(TcTables.SpawnGameObject));
        row.Validate(TcTables.SpawnGameObject);
        Assert.Equal(spawn.TemplateId, row.U("id"));
    }
}
