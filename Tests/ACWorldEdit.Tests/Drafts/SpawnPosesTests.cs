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

        string apply = SqlExport.Build(Source, [], drafts.Draft!.NetEdits().Select(e => e.Edit), null, Tc).Apply;
        Assert.Contains("`orientation` = 3", apply);
        Assert.Contains("`rotation2`", apply);
        Assert.DoesNotContain("`position_x`", apply);
    }

    static (ContentDrafts Drafts, SpawnPoses Poses, Row Spawn) CreatureOnPath(uint path)
    {
        var drafts = Open();
        var poses = new SpawnPoses(drafts, SpawnKind.Creature, _ => WorldDialect.TrinityCore);
        var spawn = SpawnEdits.NewCreature(Tc, 5, 1234, Here, 0, 0).With("MovementType", 2u);
        var addon = SpawnEdits.NewAddon(Tc, SpawnKind.Creature, 5).With("PathId", path);
        var rows = new RowSet();
        rows.Set(Tc.Tables.Find("creature"), spawn);
        rows.Set(Tc.Tables.Find("creature_addon"), addon);
        poses.Load(Source, rows);
        return (drafts, poses, spawn);
    }

    static RowSet PathRows(uint path, params uint[] points)
    {
        var rows = new RowSet();
        var nodes = MovementEdits.NodeTable(Tc);
        foreach (uint point in points)
        {
            rows.Set(nodes, MovementEdits.NodeRow(Tc, path, point, new WaypointSeed(point, point + 1, 3, null, 0)));
        }

        rows.Set(Tc.Tables.Find("waypoint_path"), MovementEdits.HeaderRow(Tc, path, 1, 0, null, ""));
        return rows;
    }

    static RowEdit[] DeletePathEdits(RowSet pathRows, uint path, Row spawn, SpawnPoses poses, ContentDrafts drafts)
    {
        var schema = Tc;
        var net = drafts.Draft!.NetFor(Source).Select(e => e.Edit);
        var nodes = MovementEdits.EffectivePathRows(schema, path, pathRows.All(MovementEdits.NodeTable(schema)), net);
        var header = MovementEdits.EffectiveHeaderRow(schema, path,
            pathRows.Get(MovementEdits.HeaderTable(schema)!, path.ToString(System.Globalization.CultureInfo.InvariantCulture)), net);
        var baseline = SpawnEdits.AddonBaseline(schema, SpawnKind.Creature, null, 5);
        return MovementEdits.DeletePath(schema, nodes, header)
            .Concat(MovementEdits.Unlink(schema, spawn, poses.AddonOf(Source, 5), baseline, 5)).ToArray();
    }

    [Fact]
    public void DeletingADatabasePathUsesItsReadRowsAsTheBaseline()
    {
        var (drafts, poses, spawn) = CreatureOnPath(9);
        var pathRows = PathRows(9, 1, 2);
        poses.LoadPathRows(Source, Tc, pathRows);

        var (ok, message) = poses.Commit(null, Source, "Delete path 9", DeletePathEdits(pathRows, 9, spawn, poses, drafts));
        Assert.True(ok, message);
        var net = drafts.Draft!.NetEdits();
        Assert.Equal(2, net.Count(e => e.Edit.Table == "waypoint_path_node" && e.Edit.After == null));
        Assert.Contains(net, e => e.Edit.Table == "waypoint_path" && e.Edit.After == null);
        string apply = SqlExport.Build(Source, [], net.Select(e => e.Edit), null, Tc).Apply;
        Assert.Contains("DELETE FROM `waypoint_path_node`", apply);
        Assert.Contains("DELETE FROM `waypoint_path`", apply);
    }

    [Fact]
    public void DeletingAPathAfterADeletedNodeLeavesNoDuplicateDelete()
    {
        var (drafts, poses, spawn) = CreatureOnPath(9);
        var pathRows = PathRows(9, 1, 2, 3);
        poses.LoadPathRows(Source, Tc, pathRows);

        var schema = Tc;
        var nodeTable = MovementEdits.NodeTable(schema);
        var nodeRows = pathRows.All(nodeTable).ToList();
        var snapshot = new RowSet();
        foreach (var row in nodeRows)
        {
            snapshot.Set(nodeTable, row);
        }

        var effective = drafts.Draft!.Effective(Source, snapshot);
        var (nodeOk, nodeMessage) = drafts.Commit(null, Source, snapshot, effective, "Delete node",
            MovementEdits.DeleteNode(schema, 9, nodeRows, 2), _ => []);
        Assert.True(nodeOk, nodeMessage);

        // The renumber left point 3 net-deleted; the effective list must not still hold it.
        var afterNode = MovementEdits.EffectivePathRows(schema, 9, nodeRows, drafts.Draft.NetFor(Source).Select(e => e.Edit));
        Assert.Equal([1u, 2u], afterNode.Select(r => r.U("NodeId")).ToArray());
        string renumber = SqlExport.Build(Source, [], drafts.Draft.NetFor(Source).Select(e => e.Edit), null, Tc).Apply;
        Assert.DoesNotContain("INSERT INTO `waypoint_path_node`", renumber);
        Assert.Contains("UPDATE `waypoint_path_node` SET", renumber);
        Assert.Contains("DELETE FROM `waypoint_path_node`", renumber);

        var (ok, message) = poses.Commit(null, Source, "Delete path 9", DeletePathEdits(pathRows, 9, spawn, poses, drafts));
        Assert.True(ok, message);
        var deletes = drafts.Draft.NetFor(Source).Where(e => e.Edit.Table == nodeTable.Name && e.Edit.After == null).Select(e => e.Edit.Key).ToList();
        Assert.Equal(3, deletes.Count);
        Assert.Equal(deletes.Count, deletes.Distinct().Count());
    }

    [Fact]
    public void AStagedAppendSurvivesAReopenAndStillSeesTheDatabaseRows()
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "content.json"));
        var nodes = MovementEdits.NodeTable(Tc);
        var database = PathRows(9, 1, 2, 3);
        using (var drafts = new ContentDrafts(path, _ => TcTables.Instance))
        {
            var snapshot = new RowSet();
            foreach (var row in database.All(nodes))
            {
                snapshot.Set(nodes, row);
            }

            var effective = drafts.Draft!.Effective(Source, snapshot);
            var (ok, message) = drafts.Commit(null, Source, snapshot, effective, "Append",
                MovementEdits.InsertNode(Tc, 9, effective.All(nodes).ToList(), 3, new WaypointSeed(9, 9, 9, null, 0)), _ => []);
            Assert.True(ok, message);
        }

        using (var reopened = new ContentDrafts(path, _ => TcTables.Instance))
        {
            // A restarted overlay has no snapshot: the draft alone holds only the appended point.
            Assert.Single(reopened.Draft!.Effective(Source, new RowSet()).All(nodes));
            // Reading the database rows and layering the draft on top restores the whole path.
            var merged = MovementEdits.EffectivePathRows(Tc, 9, database.All(nodes), reopened.Draft.NetFor(Source).Select(e => e.Edit));
            Assert.Equal([1u, 2u, 3u, 4u], merged.Select(r => r.U("NodeId")).ToArray());
        }
    }

    [Fact]
    public void RemovingDependentsIsOneCommandAndOneUndo()
    {
        var (drafts, poses, _) = Creature();
        var formation = TcSchema.Instance.Tables.Find("creature_formations");
        var related = new RowSet();
        related.Set(formation, Row.Of(("leaderGUID", 5u), ("memberGUID", 5u), ("dist", 0f), ("angle", 0f), ("groupAI", 0u), ("point_1", 0u), ("point_2", 0u)));

        poses.LoadRelated(Source, related);
        var (ok, message) = poses.DeleteMany(null, Tc, Source, [5], "Delete", related);
        Assert.True(ok, message);
        Assert.Equal(2, drafts.Draft!.NetEdits().Count);

        poses.Undo(null);
        Assert.Empty(drafts.Draft.NetEdits());
        // The baseline still holds the formation row, so the same cleanup can be issued again.
        var (again, againMessage) = poses.DeleteMany(null, Tc, Source, [5], "Delete", related);
        Assert.True(again, againMessage);
        Assert.Equal(2, drafts.Draft.NetEdits().Count);
    }

    [Fact]
    public void TheNextPathIdClearsTheNodeAndHeaderTables()
    {
        var drafts = Open();
        var poses = new SpawnPoses(drafts, SpawnKind.Creature);
        Assert.Equal(61ul, poses.NextPathId(Source, Tc, 60, 50));
        Assert.Equal(51ul, poses.NextPathId(Source, Tc, 10, 50));

        var header = MovementEdits.HeaderRow(Tc, 90, 0, 0, null, "");
        var (ok, message) = drafts.Commit(null, Source, new RowSet(), new RowSet(), "header",
            [InteractionEdits.Insert(Tc.Tables.Find("waypoint_path"), header)], _ => []);
        Assert.True(ok, message);
        Assert.Equal(91ul, poses.NextPathId(Source, Tc, 60, 50));
    }
}
