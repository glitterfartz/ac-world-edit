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
    public void TrinityCoreRefusesWaypointsAndRowsItCannotExportYet()
    {
        var waypoint = new WaypointEdit("src", 5, 1, new(1, 2, 3), new(4, 5, 6));
        var error = Assert.Throws<InvalidDataException>(() => SqlExport.Build("src", [], [], [], Stamp, [waypoint], TcSchema.Instance));
        Assert.Contains("TrinityCore", error.Message);
        // creature_ai is an AzerothCore column group; TrinityCore's catalog has no such table.
        var before = Row.Of(("entry", 100u), ("AIName", ""));
        var row = new RowEdit("creature_ai", "100", before, before.With("AIName", "SmartAI"));
        Assert.Contains("TrinityCore", Assert.Throws<InvalidDataException>(() => SqlExport.Build("src", [], [], [row], Stamp, null, TcSchema.Instance)).Message);
        Assert.DoesNotContain(TcSchema.Instance.Tables.All, t => t.Name is "creature_ai" or "smart_scripts");
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
