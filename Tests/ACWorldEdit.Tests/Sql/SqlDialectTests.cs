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
    static readonly GameObjectPlacement Placed = new(Home, 0, 0, Math.Sin(0.5), Math.Cos(0.5));

    [Fact]
    public void OmittingTheSchemaMeansAzerothCore()
    {
        var move = new CreatureMove("src", 280689, Home, Home with { X = -9460 });
        Assert.Equal(SqlExport.Build("src", [move], [], [], Stamp).Apply, SqlExport.Build("src", [move], [], [], Stamp, null, AcSchema.Instance).Apply);
    }

    [Fact]
    public void TrinityCoreSpawnMovesUseTheSameStatementsWithBigGuids()
    {
        ulong guid = 5_000_000_000;
        var creature = new CreatureMove("src", guid, Home, Home with { X = -9460 });
        var gameObject = new GameObjectMove("src", guid, Placed, Placed with { Pose = Home with { Y = 70 } });
        var ac = SqlExport.Build("src", [creature], [], [], Stamp, null, AcSchema.Instance, [gameObject]);
        var tc = SqlExport.Build("src", [creature], [], [], Stamp, null, TcSchema.Instance, [gameObject]);
        Assert.Equal(ac.Apply, tc.Apply);
        Assert.Equal(ac.Revert, tc.Revert);
        Assert.Contains("`guid` = 5000000000", tc.Apply);
        Assert.Contains("UPDATE `gameobject`", tc.Apply);
    }

    [Fact]
    public void TrinityCoreWaypointsUpdateTheNodeRow()
    {
        var props = new WaypointProps(null, 0, 0, false, 0, 0, 0);
        var move = new WaypointEdit("src", 5, 2, new(1, 2, 3), new(4, 5, 6), props, props with { Orientation = 1.5f, Delay = 3000 });
        var tc = SqlExport.Build("src", [], [], [], Stamp, [move], TcSchema.Instance);
        Assert.Contains("UPDATE `waypoint_path_node` SET `PositionX` = 4, `PositionY` = 5, `PositionZ` = 6, `Orientation` = 1.5, `Delay` = 3000 WHERE `PathId` = 5 AND `NodeId` = 2", tc.Apply);
        Assert.Contains("`Orientation` IS NULL AND `Delay` = 0", tc.Apply);
        Assert.DoesNotContain("waypoint_data", tc.Apply + tc.Revert);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TrinityCoreRefusesWaypointChangesThatHaveNoNodeColumn(int field)
    {
        var props = new WaypointProps(null, 0, 0, false, 0, 0, 0);
        var changed = field switch { 1 => props with { Velocity = 7 }, 2 => props with { MoveType = 1 }, _ => props with { Smooth = true } };
        var edit = new WaypointEdit("src", 5, 2, new(1, 2, 3), new(1, 2, 3), props, changed);
        Assert.Contains("path", Assert.Throws<InvalidDataException>(() => SqlExport.Build("src", [], [], [], Stamp, [edit], TcSchema.Instance)).Message);
        Assert.False(props.EditableOn(WorldDialect.TrinityCore, changed));
        Assert.True(props.EditableOn(WorldDialect.AzerothCore, changed));
        Assert.True(props.EditableOn(WorldDialect.TrinityCore, props with { Delay = 5, Orientation = 2 }));
    }

    [Fact]
    public void TrinityCoreRefusesRowsItCannotExportYet()
    {
        // creature_default_trainer is an AzerothCore table; TrinityCore's catalog has no such table.
        var before = Row.Of(("CreatureId", 100), ("TrainerId", 0));
        var row = new RowEdit("creature_default_trainer", "100", before, before.With("TrainerId", 17));
        Assert.Contains("TrinityCore", Assert.Throws<InvalidDataException>(() => SqlExport.Build("src", [], [], [row], Stamp, null, TcSchema.Instance)).Message);
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
        var move = new CreatureMove("src", 7, Home, Home with { X = -9460 });
        var store = new PendingChanges(path);
        Assert.Equal(store.Collect([move], [], []).Single().Id, store.Collect([move], [], [], null, null, _ => AcSchema.Instance).Single().Id);
    }
}
