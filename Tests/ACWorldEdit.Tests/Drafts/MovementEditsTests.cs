using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class MovementEditsTests
{
    static readonly DateTime Stamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    static readonly IWorldSchema Tc = TcSchema.Instance;
    static readonly IWorldSchema Ac = AcSchema.Instance;
    static readonly WorldPose Here = new(0, -9449.5, 64.25, 56.5, 1.5);

    static Row Spawn(IWorldSchema schema, ulong guid = 7) => SpawnEdits.NewCreature(schema, guid, 1234, Here, equipmentId: 0, templateMovementType: 0);
    static Row Baseline(IWorldSchema schema, ulong guid = 7) => SpawnEdits.AddonBaseline(schema, SpawnKind.Creature, null, guid);
    static Row Seed(IWorldSchema schema, uint path, uint point, double x = 1) =>
        MovementEdits.NodeRow(schema, path, point, new WaypointSeed(x, 2, 3, 0.5, 1000));

    static IReadOnlyList<Row> Ordered(IWorldSchema schema, uint path, params uint[] points)
    {
        var table = MovementEdits.NodeTable(schema);
        string point = schema.Dialect == WorldDialect.TrinityCore ? "NodeId" : "point";
        return points.Select(p => Seed(schema, path, p).With(point, p)).Select(r => r).ToList();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NewNodeRowsValidateAgainstTheirCatalogs(bool trinity)
    {
        var schema = trinity ? Tc : Ac;
        var row = Seed(schema, 9, 2);
        row.Validate(MovementEdits.NodeTable(schema));
        Assert.Equal(9u, row.U(trinity ? "PathId" : "id"));
        Assert.Equal(2u, row.U(trinity ? "NodeId" : "point"));
        Assert.True(row.F(trinity ? "PositionX" : "position_x") > 0);
    }

    [Fact]
    public void NewHeaderAndFormationRowsValidate()
    {
        var header = MovementEdits.HeaderRow(Tc, 9, 1, 0, null, "Escort");
        header.Validate(Tc.Tables.Find("waypoint_path"));
        Assert.True(header.IsNull("Velocity"));

        var formation = MovementEdits.FormationRow(Tc, 8, 7, 2.5f, 120, MovementEdits.GroupAI(3, true), 4, 8);
        formation.Validate(Tc.Tables.Find("creature_formations"));
        var ac = MovementEdits.FormationRow(Ac, 8, 7, 2.5f, 120, 3, 0, 0);
        ac.Validate(Ac.Tables.Find("creature_formations"));
        Assert.Equal(515u, formation.U("groupAI"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LinkingSetsMovementAndTheAddonPath(bool trinity)
    {
        var schema = trinity ? Tc : Ac;
        string column = MovementEdits.AddonPathColumn(schema.Dialect);
        var edits = MovementEdits.Link(schema, Spawn(schema), null, Baseline(schema), 7, 42);
        Assert.Equal(2, edits.Length);
        var spawn = edits.Single(e => e.Table == "creature");
        Assert.Equal(2u, spawn.After!.U("MovementType"));
        var addon = edits.Single(e => e.Table == "creature_addon");
        Assert.Null(addon.Before);
        Assert.Equal(42u, addon.After!.U(column));

        // Already linked: no edits.
        var linked = spawn.After!;
        var linkedAddon = addon.After!;
        Assert.Empty(MovementEdits.Link(schema, linked, linkedAddon, Baseline(schema), 7, 42));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnlinkingStopsTheSpawnAndDropsAPathOnlyAddonRow(bool trinity)
    {
        var schema = trinity ? Tc : Ac;
        var spawn = Spawn(schema).With("MovementType", 2u).With("wander_distance", 5f);
        var baseline = Baseline(schema);
        var addon = baseline.With(MovementEdits.AddonPathColumn(schema.Dialect), 42u);
        var edits = MovementEdits.Unlink(schema, spawn, addon, baseline, 7);
        Assert.Equal(2, edits.Length);
        var move = edits.Single(e => e.Table == "creature");
        Assert.Equal(0u, move.After!.U("MovementType"));
        Assert.Equal(0f, move.After!.F("wander_distance"));
        // Back to the baseline, so the row goes away instead of lingering with path 0.
        Assert.Equal(addon, edits.Single(e => e.Table == "creature_addon").Before);
        Assert.Null(edits.Single(e => e.Table == "creature_addon").After);
    }

    [Fact]
    public void UnlinkingAnUnpathedSpawnWithWanderClearsTheRadius()
    {
        var spawn = Spawn(Tc).With("wander_distance", 3f);
        var edit = Assert.Single(MovementEdits.Unlink(Tc, spawn, null, Baseline(Tc), 7));
        Assert.Equal(0f, edit.After!.F("wander_distance"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NewPathNumbersNodesFromOne(bool trinity)
    {
        var schema = trinity ? Tc : Ac;
        var seeds = new[] { new WaypointSeed(1, 2, 3, null, 0), new WaypointSeed(4, 5, 6, 1.2, 500) };
        var edits = MovementEdits.NewPath(schema, 77, seeds);
        int nodes = trinity ? 3 : 2;
        Assert.Equal(nodes, edits.Length);
        var rows = edits.Select(e => e.After!).ToList();
        foreach (var row in rows.Where(r => r.Values.ContainsKey("NodeId") || r.Values.ContainsKey("point")))
        {
            row.Validate(MovementEdits.NodeTable(schema));
        }

        string point = trinity ? "NodeId" : "point";
        Assert.Equal([1u, 2u], rows.Where(r => r.Values.ContainsKey(point)).Select(r => r.U(point)).OrderBy(p => p).ToArray());
        if (trinity)
        {
            rows.Single(r => r.Values.ContainsKey("PathId") && r.Values.ContainsKey("MoveType")).Validate(schema.Tables.Find("waypoint_path"));
        }

        Assert.Throws<InvalidDataException>(() => MovementEdits.NewPath(schema, 77, []));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InsertingShiftsNodesAboveUpByOne(bool trinity)
    {
        var schema = trinity ? Tc : Ac;
        string point = trinity ? "NodeId" : "point";
        var edits = MovementEdits.InsertNode(schema, 9, Ordered(schema, 9, 1, 2, 3), 1, new WaypointSeed(9, 9, 9, null, 0));
        // Deletes of 3, 2 then inserts of 3, 4 then the new 2.
        Assert.Equal(5, edits.Length);
        Assert.All(edits.Take(2), e => Assert.Null(e.After));
        var keys = edits.Skip(2).Select(e => e.Key).ToArray();
        Assert.Equal(["9|3", "9|4", "9|2"], keys);
        var table = MovementEdits.NodeTable(schema);
        foreach (var edit in edits.Where(e => e.After != null))
        {
            edit.After!.Validate(table);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeletingFillsTheHoleDownByOne(bool trinity)
    {
        var schema = trinity ? Tc : Ac;
        var edits = MovementEdits.DeleteNode(schema, 9, Ordered(schema, 9, 1, 2, 3), 2);
        Assert.Equal(3, edits.Length);
        Assert.All(edits.Take(2), e => Assert.Null(e.After));
        var renumbered = edits.Skip(2).Select(e => e.After!).ToArray();
        string point = trinity ? "NodeId" : "point";
        Assert.Equal([2u], renumbered.Select(r => r.U(point)).ToArray());
        Assert.Throws<InvalidDataException>(() => MovementEdits.DeleteNode(schema, 9, Ordered(schema, 9, 1), 1));
        Assert.Throws<InvalidDataException>(() => MovementEdits.DeleteNode(schema, 9, Ordered(schema, 9, 1, 2), 5));
    }

    [Fact]
    public void FormationMembershipIsAnInsertUpdateOrDelete()
    {
        var insert = MovementEdits.JoinFormation(Tc, null, 8, 7, 2, 90, 3, 0, 0);
        Assert.NotNull(insert);
        Assert.Null(insert!.Before);
        Assert.Equal("8", insert.Key);
        var same = MovementEdits.JoinFormation(Tc, insert.After, 8, 7, 2, 90, 3, 0, 0);
        Assert.Null(same);
        var moved = MovementEdits.JoinFormation(Tc, insert.After, 8, 7, 3, 90, 3, 0, 0);
        Assert.NotNull(moved!.After);
        Assert.Equal(3f, moved!.After!.F("dist"));
        Assert.NotNull(MovementEdits.LeaveFormation(Tc, moved.After!));
    }

    [Fact]
    public void GroupAiSplitsIntoAssistAndFollow()
    {
        Assert.Equal((3u, true), MovementEdits.SplitGroupAI(515));
        Assert.Equal((0u, true), MovementEdits.SplitGroupAI(512));
        Assert.Equal((1u, false), MovementEdits.SplitGroupAI(1));
        Assert.Equal(515u, MovementEdits.GroupAI(3, true));
        Assert.Equal("Leader assists members", MovementEdits.AssistName(WorldDialect.TrinityCore, 2));
        Assert.Equal("Members assist each other", MovementEdits.AssistName(WorldDialect.AzerothCore, 2));
    }

    [Fact]
    public void BadSeedsAndOffsetsAreRefused()
    {
        Assert.Throws<InvalidDataException>(() => MovementEdits.NodeRow(Tc, 9, 0, new WaypointSeed(1, 2, 3, null, 0)));
        Assert.Throws<InvalidDataException>(() => MovementEdits.NodeRow(Tc, 9, 1, new WaypointSeed(double.NaN, 2, 3, null, 0)));
        Assert.Throws<InvalidDataException>(() => MovementEdits.NodeRow(Ac, 9, 1, new WaypointSeed(1, 2, 3, null, 0, MoveType: 9)));
        Assert.Throws<InvalidDataException>(() => MovementEdits.FormationRow(Tc, 0, 7, 1, 90, 0, 0, 0));
        Assert.Throws<InvalidDataException>(() => MovementEdits.FormationRow(Tc, 8, 7, float.NaN, 90, 0, 0, 0));
        Assert.Throws<InvalidDataException>(() => MovementEdits.FormationRow(Tc, 8, 7, 1, 90, 0, 70000, 0));
        // Out-of-range offsets still load on the server, so they warn instead of refusing.
        var rows = new RowSet();
        var formations = MovementEdits.FormationTable(Tc);
        rows.Set(formations, MovementEdits.FormationRow(Tc, 8, 7, -1, 400, 0, 0, 0));
        var warnings = SpawnRules.CheckFormations(rows, formations).Where(i => !i.Error).Select(i => i.Message).ToList();
        Assert.Contains(warnings, m => m.Contains("negative distance"));
        Assert.Contains(warnings, m => m.Contains("0 to 360"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MovementRulesCatchBadWaypointAndFormationRows(bool trinity)
    {
        var schema = trinity ? Tc : Ac;
        var rows = new RowSet();
        var nodes = MovementEdits.NodeTable(schema);
        var bad = Ordered(schema, 9, 1)[0].With(trinity ? "PositionX" : "position_x", float.NaN);
        rows.Set(nodes, bad);
        var formations = MovementEdits.FormationTable(schema);
        var member = Row.Of(("leaderGUID", 7ul), ("memberGUID", 8ul), ("dist", 1f), ("angle", 400f), ("groupAI", 0u), ("point_1", 0u), ("point_2", 0u));
        member.Validate(formations);
        rows.Set(formations, member);
        var issues = SpawnRules.CheckMovement(schema, rows).Where(i => i.Error).Select(i => i.Message).ToList();
        Assert.Contains(issues, m => m.Contains("coordinates"));
        Assert.Contains(SpawnRules.CheckMovement(schema, rows).Where(i => !i.Error), i => i.Message.Contains("0 to 360"));
        Assert.Empty(SpawnRules.CheckMovement(schema, new RowSet()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WaypointRowEditsExportAsGuardedInsertsUpdatesAndDeletes(bool trinity)
    {
        var schema = trinity ? Tc : Ac;
        var table = MovementEdits.NodeTable(schema);
        var insert = InteractionEdits.Insert(table, Seed(schema, 9, 1));
        var before = Seed(schema, 9, 2);
        var after = before.With(trinity ? "PositionX" : "position_x", 4f);
        var update = InteractionEdits.Update(table, before, after);
        var delete = InteractionEdits.Delete(table, Seed(schema, 9, 3));
        string apply = SqlExport.Build("src", [], [insert, update, delete], Stamp, schema).Apply;
        string name = trinity ? "`waypoint_path_node`" : "`waypoint_data`";
        string key = trinity ? "`PathId` = 9 AND `NodeId`" : "`id` = 9 AND `point`";
        Assert.Contains($"INSERT INTO {name}", apply);
        Assert.Contains($"UPDATE {name} SET", apply);
        Assert.Contains($"DELETE FROM {name} WHERE {key}", apply);
    }
}
