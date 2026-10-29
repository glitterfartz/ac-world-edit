using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class SpawnPosesTests
{
    static readonly IWorldSchema Tc = TcSchema.Instance;
    static readonly WorldPose Here = new(0, -9449.5, 64.25, 56.5, 1.5);
    const string Source = "src";

    static ContentDrafts Open() => new(System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "content.json")), _ => TcTables.Instance);

    static (ContentDrafts Drafts, SpawnPoses Poses, Row Row) Creature()
    {
        var drafts = Open();
        var poses = new SpawnPoses(drafts, SpawnKind.Creature);
        var row = SpawnEdits.NewCreature(Tc, 5, 1234, Here, 0, 0);
        poses.Load(Source, [row]);
        return (drafts, poses, row);
    }

    [Fact]
    public void AMoveIsOneUndoableRowEditAndShowsInTheEffectivePose()
    {
        var (drafts, poses, _) = Creature();
        Assert.Equal(Here, poses.Effective(Source, 5, Here));

        var (ok, message) = poses.Move(null, Source, 5, Here with { X = -9400 });
        Assert.True(ok, message);
        Assert.Equal(-9400, poses.Effective(Source, 5, Here).X);
        Assert.Single(drafts.Draft!.NetEdits());

        poses.Undo(null);
        Assert.Equal(Here, poses.Effective(Source, 5, Here));
        Assert.Empty(drafts.Draft.NetEdits());
        poses.Redo(null);
        Assert.Equal(-9400, poses.Effective(Source, 5, Here).X);
    }

    [Fact]
    public void ASecondMoveStartsFromTheFirstAndCollapsesToOneNetChange()
    {
        var (drafts, poses, row) = Creature();
        poses.Move(null, Source, 5, Here with { X = -9400 });
        poses.Move(null, Source, 5, Here with { X = -9300, Heading = 2 });

        var net = Assert.Single(drafts.Draft!.NetEdits()).Edit;
        Assert.Equal(row, net.Before);
        Assert.Equal(-9300, SpawnEdits.PoseOf(net.After!).X);
        Assert.Equal(2, SpawnEdits.PoseOf(net.After!).Heading);
    }

    [Fact]
    public void AChangedDatabasePoseIsAConflict()
    {
        var (_, poses, _) = Creature();
        Assert.False(poses.Conflict(Source, 5, Here));
        poses.Move(null, Source, 5, Here with { X = -9400 });
        Assert.False(poses.Conflict(Source, 5, Here));
        Assert.False(poses.Conflict(Source, 5, Here with { X = Here.X + 0.00001 }));
        Assert.True(poses.Conflict(Source, 5, Here with { X = -1 }));
    }

    [Fact]
    public void ASpawnWhoseRowIsNotLoadedCannotBeMovedYet()
    {
        var (_, poses, _) = Creature();
        Assert.False(poses.Has(Source, 99));
        var (ok, message) = poses.Move(null, Source, 99, Here);
        Assert.False(ok);
        Assert.Contains("still loading", message);
    }

    [Fact]
    public void ASpawnStaysOnItsMap()
    {
        var (drafts, poses, _) = Creature();
        var (ok, message) = poses.Move(null, Source, 5, Here with { Map = 1 });
        Assert.False(ok);
        Assert.Contains("another map", message);
        Assert.Empty(drafts.Draft!.NetEdits());
    }

    [Fact]
    public void ChangesFromAnotherEditorReachTheCachedNetEdits()
    {
        var (drafts, poses, row) = Creature();
        Assert.Equal(Here, poses.Effective(Source, 5, Here));
        // The cache was built; a change made through the draft (undo, discard, another editor) must show.
        poses.Move(null, Source, 5, Here with { Y = 70 });
        Assert.Equal(70, poses.Effective(Source, 5, Here).Y);
        Assert.True(drafts.Revert(Source, drafts.Draft!.NetEdits().Single().Edit));
        Assert.Equal(Here, poses.Effective(Source, 5, Here));
        Assert.Equal(row, SpawnEdits.WithPose(row, Here));
    }

    [Fact]
    public void AGameObjectTurnChangesOnlyTheRotationColumnsThatDiffer()
    {
        var drafts = Open();
        var poses = new SpawnPoses(drafts, SpawnKind.GameObject);
        var row = SpawnEdits.NewGameObject(Tc, 6, 4321, Here);
        poses.Load(Source, [row]);
        var original = SpawnPoses.PlacementOf(row);

        var turned = original.WithHeading(3.0);
        var (ok, message) = poses.Move(null, Source, 6, turned);
        Assert.True(ok, message);
        Assert.Equal(3.0, poses.Effective(Source, 6, original).Pose.Heading, 4);

        string apply = SqlExport.Build(Source, [], drafts.Draft!.NetEdits().Select(e => e.Edit), null, null, Tc).Apply;
        Assert.Contains("`orientation` = 3", apply);
        Assert.Contains("`rotation2`", apply);
        Assert.DoesNotContain("`position_x`", apply);
    }
}
