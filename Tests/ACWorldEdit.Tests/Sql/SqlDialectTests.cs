using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Sql;

/// <summary>
/// Per-dialect export: each source's schema picks the statements, TrinityCore spawn moves match
/// AzerothCore's, what TrinityCore cannot export yet fails instead of producing AzerothCore SQL, and
/// the remembered source dialects survive a restart.
/// </summary>
public class SqlDialectTests
{
    static readonly DateTime Stamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    static readonly WorldPose Home = new(0, -9464.7, 62.8, 56.2, 1);

    [Fact]
    public void OmittingTheSchemaMeansAzerothCore()
    {
        var before = SpawnEdits.NewCreature(AcSchema.Instance, 280689, 1234, Home, 0, 0);
        var move = SpawnEdits.Move(AcSchema.Instance, SpawnKind.Creature, before, Home with { X = -9460 });
        Assert.Equal(SqlExport.Build("src", [], [move], Stamp).Apply, SqlExport.Build("src", [], [move], Stamp, AcSchema.Instance).Apply);
    }

    [Fact]
    public void TrinityCoreSpawnMovesExportWithBigGuids()
    {
        ulong guid = 5_000_000_000;
        var creature = SpawnEdits.NewCreature(TcSchema.Instance, guid, 1234, Home, 0, 0);
        var gameObject = SpawnEdits.NewGameObject(TcSchema.Instance, guid, 4321, Home);
        var moves = new[]
        {
            SpawnEdits.Move(TcSchema.Instance, SpawnKind.Creature, creature, Home with { X = -9460 }),
            SpawnEdits.Move(TcSchema.Instance, SpawnKind.GameObject, gameObject, Home with { Y = 70 })
        };
        var tc = SqlExport.Build("src", [], moves, Stamp, TcSchema.Instance);
        Assert.Contains("UPDATE `creature` SET `position_x` = -9460 WHERE `guid` = 5000000000", tc.Apply);
        Assert.Contains("UPDATE `gameobject` SET `position_y` = 70 WHERE `guid` = 5000000000", tc.Apply);
        Assert.Contains("SET `position_x` = -9464.7 WHERE `guid` = 5000000000", tc.Revert);
        // The TrinityCore spawn tables are not AzerothCore's: an AzerothCore export of these rows is refused.
        Assert.Throws<InvalidDataException>(() => SqlExport.Build("src", [], moves, Stamp, AcSchema.Instance));
    }

    [Fact]
    public void TrinityCoreWaypointsUpdateTheNodeRow()
    {
        var table = TcSchema.Instance.Tables.Find("waypoint_path_node");
        var before = MovementEdits.NodeRow(TcSchema.Instance, 5, 2, new WaypointSeed(1, 2, 3, null, 0));
        var after = before.With("PositionX", 4f).With("PositionY", 5f).With("PositionZ", 6f)
            .With("Orientation", 1.5f).With("Delay", 3000u);
        var tc = SqlExport.Build("src", [], [InteractionEdits.Update(table, before, after)], Stamp, TcSchema.Instance);
        Assert.Contains("UPDATE `waypoint_path_node` SET `PositionX` = 4, `PositionY` = 5, `PositionZ` = 6, `Orientation` = 1.5, `Delay` = 3000 WHERE `PathId` = 5 AND `NodeId` = 2", tc.Apply);
        Assert.Contains("`Orientation` IS NULL", tc.Apply);
        Assert.DoesNotContain("waypoint_data", tc.Apply + tc.Revert);
    }

    [Fact]
    public void AzerothCoreWaypointsGuardNullFacingAsIsNull()
    {
        var table = AcSchema.Instance.Tables.Find("waypoint_data");
        var before = MovementEdits.NodeRow(AcSchema.Instance, 5, 2, new WaypointSeed(1, 2, 3, 0.5, 0));
        var after = before.With("orientation", null).With("delay", 7000u);
        var ac = SqlExport.Build("src", [], [InteractionEdits.Update(table, before, after)], Stamp, AcSchema.Instance);
        Assert.Contains("`orientation` = NULL", ac.Apply);
        Assert.Contains("`orientation` = 0.5", ac.Revert);
        Assert.DoesNotContain("waypoint_path_node", ac.Apply + ac.Revert);
    }

    [Fact]
    public void TrinityCoreRefusesRowsItCannotExportYet()
    {
        // creature_default_trainer is an AzerothCore table; TrinityCore's catalog has no such table.
        var before = Row.Of(("CreatureId", 100), ("TrainerId", 0));
        var row = new RowEdit("creature_default_trainer", "100", before, before.With("TrainerId", 17));
        Assert.Contains("TrinityCore", Assert.Throws<InvalidDataException>(() => SqlExport.Build("src", [], [row], Stamp, TcSchema.Instance)).Message);
        Assert.DoesNotContain(TcSchema.Instance.Tables.All, t => t.Name is "creature_default_trainer" or "waypoint_data");
    }

    [Fact]
    public void EachDialectHasItsSchema()
    {
        Assert.Same(AcSchema.Instance, WorldSchemas.For(WorldDialect.AzerothCore));
        Assert.Same(TcSchema.Instance, WorldSchemas.For(WorldDialect.TrinityCore));
        Assert.Equal(WorldDialect.TrinityCore, TcSchema.Instance.Dialect);
    }

    [Fact]
    public void SourceDialectsAreRememberedAcrossSessions()
    {
        string path = Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "sources.json");
        var first = new SourceDialects(path);
        Assert.Same(AcSchema.Instance, first.SchemaFor("unknown"));
        first.Remember("ac", WorldDialect.AzerothCore);
        Assert.False(File.Exists(path));
        first.Remember("tc", WorldDialect.TrinityCore);
        var second = new SourceDialects(path);
        Assert.Same(TcSchema.Instance, second.SchemaFor("tc"));
        Assert.Equal(WorldDialect.AzerothCore, second.DialectOf("ac"));
    }

    [Fact]
    public void ACorruptSourceRecordFallsBackToAzerothCore()
    {
        string path = Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "sources.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        Assert.Equal(WorldDialect.AzerothCore, new SourceDialects(path).DialectOf("tc"));
    }

    [Fact]
    public void PendingFingerprintsAreUnchangedForAzerothCoreSources()
    {
        string path = Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "exported.json");
        var before = SpawnEdits.NewCreature(AcSchema.Instance, 7, 1234, Home, 0, 0);
        var move = ("src", SpawnEdits.Move(AcSchema.Instance, SpawnKind.Creature, before, Home with { X = -9460 }));
        var store = new PendingChanges(path);
        Assert.Equal(store.Collect([], [move], _ => AcSchema.Instance).Single().Id, store.Collect([], [move], _ => AcSchema.Instance).Single().Id);
    }
}
