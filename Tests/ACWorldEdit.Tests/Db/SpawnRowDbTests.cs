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
        string apply = SqlExport.Build("src", [], [edit], Stamp, source.Schema).Apply;
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

    /// <summary>A template's look must match what the region read draws for the same creature.</summary>
    static async Task CheckLooks(IDBSource source, WorldDialect dialect)
    {
        var region = await source.ReadCreaturesAsync(Elwynn, new SpawnPreview { Dialect = dialect }, null, "", default);
        var spawns = region.Spawns.Take(100).ToArray();
        var rows = await source.ReadSpawnRowsAsync(SpawnKind.Creature, spawns.Select(s => s.Guid).ToArray(), default);
        var table = SpawnEdits.SpawnTable(source.Schema, SpawnKind.Creature);
        var looks = await source.ReadCreatureLooksAsync(spawns.Select(s => s.TemplateId).ToArray(), default);
        Assert.NotEmpty(looks);
        foreach (var spawn in spawns)
        {
            var look = looks[spawn.TemplateId];
            var row = rows.Get(table, spawn.Guid.ToString())!;
            var staged = SpawnPoses.CreatureOf(row, look, dialect);
            Assert.Equal((spawn.Name, spawn.DisplayId, spawn.DisplayScale), (staged.Name, staged.DisplayId, staged.DisplayScale));
        }

        Assert.Empty(await source.ReadCreatureLooksAsync([], default));
        Assert.Empty(await source.ReadCreatureLooksAsync([uint.MaxValue], default));
    }

    [DbFact]
    public async Task AzerothCoreTemplateLooksMatchTheRegionRead() => await CheckLooks(AcSource(Config), WorldDialect.AzerothCore);

    [TcDbFact]
    public async Task TrinityCoreTemplateLooksMatchTheRegionRead() => await CheckLooks(DBSource.Open(Environments.TrinityConnection()!), WorldDialect.TrinityCore);

    static async Task CheckGameObjectLooks(IDBSource source, WorldDialect dialect)
    {
        var region = await source.ReadGameObjectsAsync(Elwynn, new SpawnPreview { Dialect = dialect }, null, "", default);
        var spawns = region.Spawns.Take(100).ToArray();
        var rows = await source.ReadSpawnRowsAsync(SpawnKind.GameObject, spawns.Select(s => s.Guid).ToArray(), default);
        var table = SpawnEdits.SpawnTable(source.Schema, SpawnKind.GameObject);
        var looks = await source.ReadGameObjectLooksAsync(spawns.Select(s => s.TemplateId).ToArray(), default);
        Assert.NotEmpty(looks);
        foreach (var spawn in spawns)
        {
            var staged = SpawnPoses.GameObjectOf(rows.Get(table, spawn.Guid.ToString())!, looks[spawn.TemplateId], dialect);
            Assert.Equal((spawn.Name, spawn.DisplayId, spawn.Scale, spawn.Type, spawn.State), (staged.Name, staged.DisplayId, staged.Scale, staged.Type, staged.State));
            Assert.Equal(spawn.StoredRotation, staged.StoredRotation);
            Assert.True(System.Numerics.Quaternion.Dot(spawn.Rotation, staged.Rotation) > 0.9999f);
            Assert.Equal(spawn.Phasing, staged.Phasing);
        }

        Assert.Empty(await source.ReadGameObjectLooksAsync([], default));
        Assert.Empty(await source.ReadGameObjectLooksAsync([uint.MaxValue], default));
    }

    [DbFact]
    public async Task AzerothCoreGameObjectLooksMatchTheRegionRead() => await CheckGameObjectLooks(AcSource(Config), WorldDialect.AzerothCore);

    [TcDbFact]
    public async Task TrinityCoreGameObjectLooksMatchTheRegionRead() => await CheckGameObjectLooks(DBSource.Open(Environments.TrinityConnection()!), WorldDialect.TrinityCore);

    /// <summary>A spawn read for editing brings its addon row, so a delete or a copy can carry it.</summary>
    static async Task CheckAddonRows(IDBSource source, WorldDialect dialect, SpawnKind kind)
    {
        var preview = new SpawnPreview { Dialect = dialect };
        IReadOnlyList<ulong> guids = kind == SpawnKind.Creature
            ? (await source.ReadCreaturesAsync(Elwynn, preview, null, "", default)).Spawns.Select(s => s.Guid).ToArray()
            : (await source.ReadGameObjectsAsync(Elwynn, preview, null, "", default)).Spawns.Select(s => s.Guid).ToArray();
        var rows = await source.ReadSpawnRowsAsync(kind, guids.Take(300).ToArray(), default);
        var addons = rows.All(SpawnEdits.AddonTable(source.Schema, kind));
        Assert.NotEmpty(addons);
        foreach (var addon in addons)
        {
            addon.Validate(SpawnEdits.AddonTable(source.Schema, kind));
            Assert.NotNull(rows.Get(SpawnEdits.SpawnTable(source.Schema, kind), addon["guid"]));
        }
    }

    [DbFact]
    public async Task AzerothCoreCreatureAddonRowsComeWithTheSpawn() => await CheckAddonRows(AcSource(Config), WorldDialect.AzerothCore, SpawnKind.Creature);

    [TcDbFact]
    public async Task TrinityCoreCreatureAddonRowsComeWithTheSpawn() => await CheckAddonRows(DBSource.Open(Environments.TrinityConnection()!), WorldDialect.TrinityCore, SpawnKind.Creature);

    /// <summary>The rules mirror the server loader, so real rows should almost all satisfy them.</summary>
    static async Task CheckRulesOnRealRows(IDBSource source, WorldDialect dialect, SpawnKind kind)
    {
        var preview = new SpawnPreview { Dialect = dialect };
        var guids = kind == SpawnKind.Creature
            ? (await source.ReadCreaturesAsync(Elwynn, preview, null, "", default)).Spawns.Select(s => s.Guid).ToArray()
            : (await source.ReadGameObjectsAsync(Elwynn, preview, null, "", default)).Spawns.Select(s => s.Guid).ToArray();
        var rows = await source.ReadSpawnRowsAsync(kind, guids.Take(500).ToArray(), default);
        var table = SpawnEdits.SpawnTable(source.Schema, kind);
        var all = rows.All(table);
        Assert.NotEmpty(all);
        var failing = SpawnRules.Check(dialect, kind, rows, table).Where(i => i.Error).Select(i => i.Message).ToArray();
        Assert.True(failing.Length * 20 <= all.Count, $"{failing.Length} of {all.Count} real rows fail the rules, e.g. {string.Join(" | ", failing.Take(3))}");
    }

    [DbFact]
    public async Task AzerothCoreRealCreaturesSatisfyTheRules() => await CheckRulesOnRealRows(AcSource(Config), WorldDialect.AzerothCore, SpawnKind.Creature);

    [TcDbFact]
    public async Task TrinityCoreRealCreaturesSatisfyTheRules() => await CheckRulesOnRealRows(DBSource.Open(Environments.TrinityConnection()!), WorldDialect.TrinityCore, SpawnKind.Creature);

    [DbFact]
    public async Task AzerothCoreRealGameObjectsSatisfyTheRules() => await CheckRulesOnRealRows(AcSource(Config), WorldDialect.AzerothCore, SpawnKind.GameObject);

    [TcDbFact]
    public async Task TrinityCoreRealGameObjectsSatisfyTheRules() => await CheckRulesOnRealRows(DBSource.Open(Environments.TrinityConnection()!), WorldDialect.TrinityCore, SpawnKind.GameObject);

    static async Task CheckTemplateColumns(IDBSource source, WorldDialect dialect)
    {
        var region = await source.ReadCreaturesAsync(Elwynn, new SpawnPreview { Dialect = dialect }, null, "", default);
        var spawn = region.Spawns.First();
        var wanted = SpawnFields.TemplateColumns(dialect, SpawnKind.Creature);
        var values = await source.ReadTemplateColumnsAsync(SpawnKind.Creature, spawn.TemplateId, wanted, default);
        // Every column the inspector shows a template value for exists, and flags read back as numbers.
        Assert.Equal(wanted.OrderBy(c => c), values.Keys.OrderBy(c => c));
        foreach (string flag in wanted.Where(c => c != "ScriptName"))
        {
            Assert.True(long.TryParse(values[flag], out _), $"{flag}: '{values[flag]}'");
        }

        Assert.Equal(["ScriptName"], (await source.ReadTemplateColumnsAsync(SpawnKind.Creature, spawn.TemplateId, ["ScriptName", "no_such_column"], default)).Keys);
        Assert.Empty(await source.ReadTemplateColumnsAsync(SpawnKind.Creature, uint.MaxValue, wanted, default));
        Assert.Empty(await source.ReadTemplateColumnsAsync(SpawnKind.Creature, spawn.TemplateId, [], default));
        Assert.Contains("ScriptName", (await source.ReadTemplateColumnsAsync(SpawnKind.GameObject, (await source.ReadGameObjectsAsync(Elwynn, new SpawnPreview { Dialect = dialect }, null, "", default)).Spawns.First().TemplateId, ["ScriptName"], default)).Keys);
    }

    [DbFact]
    public async Task AzerothCoreTemplateColumnsBehindTheOverridesRead() => await CheckTemplateColumns(AcSource(Config), WorldDialect.AzerothCore);

    [TcDbFact]
    public async Task TrinityCoreTemplateColumnsBehindTheOverridesRead() => await CheckTemplateColumns(DBSource.Open(Environments.TrinityConnection()!), WorldDialect.TrinityCore);
}
