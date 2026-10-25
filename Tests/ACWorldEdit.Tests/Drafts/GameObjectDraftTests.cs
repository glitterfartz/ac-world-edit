using ACWorldEdit.Foundation;
using Xunit;

/// <summary>
/// GameObject placement drafts: persistence, stale baselines, rotation turns and the SQL they export.
/// Each test uses a unique disposable path and no database.
/// </summary>
public class GameObjectDraftTests
{
    static string TempPath() => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "gameobjects.json"));

    static readonly GameObjectPlacement Baseline = new(new WorldPose(0, 100, 200, 30, 1), 0, 0, Math.Sin(0.5), Math.Cos(0.5));
    static readonly DateTime Stamp = new(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void MovesSurviveReopenAndUndo()
    {
        string path = TempPath();
        var moved = Baseline with { Pose = Baseline.Pose with { X = 110, Z = 35 } };
        using (var draft = new GameObjectDraft(path))
        {
            draft.Move("a", 7, Baseline, moved);
            Assert.Equal(moved, draft.Effective("a", 7, Baseline));
            Assert.Equal(Baseline, draft.Effective("b", 7, Baseline));
        }

        using (var draft = new GameObjectDraft(path))
        {
            Assert.Equal(moved, draft.Effective("a", 7, Baseline));
            draft.Undo();
            Assert.Equal(Baseline, draft.Effective("a", 7, Baseline));
            draft.Redo();
            Assert.Equal(moved, draft.Effective("a", 7, Baseline));
            Assert.Single(draft.NetMoves());
        }
    }

    [Fact]
    public void AStaleBaselineIsAConflictAndNeverWins()
    {
        using var draft = new GameObjectDraft(TempPath());
        draft.Move("a", 7, Baseline, Baseline with { Pose = Baseline.Pose with { X = 110 } });
        var changed = Baseline with { Pose = Baseline.Pose with { X = 101 } };
        Assert.True(draft.Conflict("a", 7, changed));
        Assert.Equal(101, draft.Effective("a", 7, changed).Pose.X);
        Assert.Throws<InvalidDataException>(() => draft.Move("a", 7, changed, changed with { Pose = changed.Pose with { Y = 5 } }));
    }

    [Fact]
    public void AMoveBackToTheBaselineIsNotANetChange()
    {
        using var draft = new GameObjectDraft(TempPath());
        var away = Baseline with { Pose = Baseline.Pose with { X = 110 } };
        draft.Move("a", 7, Baseline, away);
        draft.Move("a", 7, Baseline, Baseline);
        Assert.Empty(draft.NetMoves());
    }

    [Fact]
    public void RevertingANetMoveReturnsToTheBaseline()
    {
        using var draft = new GameObjectDraft(TempPath());
        draft.Move("a", 7, Baseline, Baseline with { Pose = Baseline.Pose with { Z = 99 } });
        draft.Revert(draft.NetMoves()[0]);
        Assert.Equal(Baseline, draft.Effective("a", 7, Baseline));
        Assert.Empty(draft.NetMoves());
    }

    [Fact]
    public void ASecondWriterIsRejected()
    {
        string path = TempPath();
        using var first = new GameObjectDraft(path);
        Assert.Throws<IOException>(() => new GameObjectDraft(path));
    }

    [Fact]
    public void TurningKeepsPitchAndRollAndOnlyChangesTheHeading()
    {
        // A tilted object: 30 degrees about X, facing heading 1.
        double tilt = Math.PI / 6;
        var tilted = new GameObjectPlacement(new WorldPose(0, 0, 0, 0, 1), Math.Sin(tilt / 2), 0, 0, Math.Cos(tilt / 2));
        var turned = tilted.WithHeading(1 + Math.PI / 2);
        Assert.Equal(1 + Math.PI / 2, turned.Pose.Heading, 9);
        // The product stays a unit quaternion and its tilt component is unchanged in magnitude.
        var (x, y, z, w) = turned.FacingQuaternion();
        Assert.Equal(1, x * x + y * y + z * z + w * w, 9);
        Assert.Equal(Math.Sin(tilt / 2), Math.Sqrt(x * x + y * y), 9);
    }

    [Fact]
    public void AnObjectWithoutAStoredRotationGetsAPlainTurnAboutZ()
    {
        var bare = new GameObjectPlacement(new WorldPose(0, 0, 0, 0, 0.5), 0, 0, 0, 0);
        Assert.False(bare.HasRotation);
        var (x0, y0, z0, w0) = bare.FacingQuaternion();
        Assert.Equal((0, 0), (x0, y0));
        Assert.Equal(Math.Sin(0.25), z0, 9);
        var turned = bare.WithHeading(2);
        Assert.Equal(Math.Sin(1), turned.RotZ, 9);
        Assert.Equal(Math.Cos(1), turned.RotW, 9);
        Assert.Equal(0, turned.RotX, 9);
        Assert.Equal(0, turned.RotY, 9);
    }

    [Fact]
    public void HeadingsWrapIntoARange()
    {
        var turned = Baseline.WithHeading(-0.5);
        Assert.InRange(turned.Pose.Heading, 0, 2 * Math.PI);
    }

    [Fact]
    public void AMoveExportsPositionOnlyWithGuardsAndALeavesRotationAlone()
    {
        var after = Baseline with { Pose = Baseline.Pose with { X = 110.5 } };
        var scripts = SqlExport.Build("src", [], [], [], Stamp, null, null, [new("src", 12, Baseline, after)]);
        Assert.Equal(1, scripts.Changes);
        Assert.Contains("UPDATE `gameobject` SET `position_x` = 110.5, `position_y` = 200, `position_z` = 30, `orientation` = 1 WHERE `guid` = 12 AND ABS(`position_x` - (100)) < 0.0001", scripts.Apply);
        Assert.DoesNotContain("rotation0", scripts.Apply);
        Assert.Contains("UPDATE `gameobject` SET `position_x` = 100", scripts.Revert);
        Assert.Contains("WHERE `guid` = 12 AND ABS(`position_x` - (110.5))", scripts.Revert);
    }

    [Fact]
    public void ATurnExportsAndGuardsTheRotationColumns()
    {
        var after = Baseline.WithHeading(2);
        var scripts = SqlExport.Build("src", [], [], [], Stamp, null, null, [new("src", 12, Baseline, after)]);
        Assert.Contains("`rotation0` = ", scripts.Apply);
        Assert.Contains("`rotation3` = ", scripts.Apply);
        // The freshness guard repeats the stored (before) rotation, so a changed row is left alone.
        Assert.Contains($"ABS(`rotation2` - ({Baseline.RotZ.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}))", scripts.Apply);
        Assert.Contains("`orientation` = 2", scripts.Apply);
    }

    [Fact]
    public void PendingChangesListGameObjectMovesWithAStableId()
    {
        var move = new GameObjectMove("src", 12, Baseline, Baseline with { Pose = Baseline.Pose with { Z = 31 } });
        var store = new PendingChanges(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "exported.json"));
        var first = Assert.Single(store.Collect([], [], [], null, [move]));
        Assert.Equal(PendingKind.GameObject, first.Kind);
        Assert.Equal(move, first.GameObject);
        Assert.Contains("gameobject guid 12", first.Label);
        Assert.Equal(first.Id, Assert.Single(store.Collect([], [], [], null, [move])).Id);
        store.MarkExported([first]);
        Assert.Empty(store.Collect([], [], [], null, [move]));
    }
}
