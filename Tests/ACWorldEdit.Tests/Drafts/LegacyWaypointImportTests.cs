using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class LegacyWaypointImportTests
{
    static readonly IWorldSchema Tc = TcSchema.Instance;
    static readonly IWorldSchema Ac = AcSchema.Instance;

    static string TempPath() => Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "waypoints.json");

    static void WriteLegacy(string path, LegacyWaypointEdit[] history)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string payload = JsonSerializer.Serialize(new DraftSnapshot<LegacyWaypointEdit>(1, history, history.Length));
        File.WriteAllText(path, JsonSerializer.Serialize(new[] { payload, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))) }));
    }

    static LegacyWaypointEdit Move(string source, uint path, uint point, double x) => new(source, path, point, new(1, 2, 3), new(x, 2, 3), null, null);

    [Fact]
    public void MissingFileLoadsNothingAndCorruptFileThrows()
    {
        Assert.Empty(LegacyWaypointImport.Load(TempPath()));
        string bad = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(bad)!);
        File.WriteAllText(bad, "[\"oops\"]");
        Assert.Throws<InvalidDataException>(() => LegacyWaypointImport.Load(bad));
    }

    [Fact]
    public void NetMovesCollapseAndOmitRoundTrips()
    {
        string path = TempPath();
        WriteLegacy(path, [
            Move("s", 7, 1, 4),
            new("s", 7, 1, new(1, 2, 3), new(4, 5, 6), null, new(null, 0, 1000, false, 0, 0, 100)),
            Move("s", 7, 2, 9),
            Move("s", 7, 2, 1),
        ]);
        var net = LegacyWaypointImport.Load(path);
        var kept = Assert.Single(net, m => m.Point == 1);
        Assert.Equal(4, kept.After.X);
        Assert.Equal(1000u, kept.AfterProps!.Delay);
        Assert.DoesNotContain(net, m => m.Point == 2);
    }

    static Row RecordRow(IWorldSchema schema, double x = 1, double? facing = null, uint delay = 0) =>
        MovementEdits.NodeRow(schema, 7, 1, new WaypointSeed(x, 2, 3, facing, delay));

    [Fact]
    public void ToUpdateMatchesAppliesAndReportsConflicts()
    {
        var record = RecordRow(Ac);
        var move = new LegacyWaypointMove("s", 7, 1, new(1, 2, 3), new(4, 5, 6), null, null);
        var (edit, conflict) = LegacyWaypointImport.ToUpdate(Ac, record, move);
        Assert.False(conflict);
        Assert.NotNull(edit);
        Assert.Equal(record, edit!.Before);
        Assert.Equal(4f, edit.After!.F("position_x"));

        // Already applied: nothing to do.
        var appliedRow = MovementEdits.NodeRow(Ac, 7, 1, new WaypointSeed(4, 5, 6, null, 0));
        var applied = LegacyWaypointImport.ToUpdate(Ac, appliedRow, move);
        Assert.Equal((null, false), applied);

        // The database moved under the draft: conflict.
        var stale = LegacyWaypointImport.ToUpdate(Ac, RecordRow(Ac, 10), move);
        Assert.Equal((null, true), stale);
    }

    [Fact]
    public void ClearEmptiesTheLegacyFile()
    {
        string path = TempPath();
        WriteLegacy(path, [Move("s", 7, 1, 4)]);
        Assert.NotEmpty(LegacyWaypointImport.Load(path));
        LegacyWaypointImport.Clear(path);
        Assert.Empty(LegacyWaypointImport.Load(path));
    }
}
