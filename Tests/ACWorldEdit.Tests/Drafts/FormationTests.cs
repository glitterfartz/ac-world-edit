using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class FormationTests
{
    static readonly IWorldSchema Tc = TcSchema.Instance;
    static readonly IWorldSchema Ac = AcSchema.Instance;
    static readonly WorldPose Here = new(0, -9449.5, 64.25, 56.5, 1.5);
    const string Source = "src";

    static ContentDrafts Open() => new(System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "content.json")), _ => TcTables.Instance);

    static (ContentDrafts Drafts, SpawnPoses Poses) Two()
    {
        var drafts = Open();
        var poses = new SpawnPoses(drafts, SpawnKind.Creature);
        poses.Load(Source, [SpawnEdits.NewCreature(Tc, 7, 1234, Here, 0, 0), SpawnEdits.NewCreature(Tc, 8, 1234, Here, 0, 0)]);
        var rows = new RowSet();
        var table = MovementEdits.FormationTable(Tc);
        rows.Set(table, MovementEdits.FormationRow(Tc, 7, 7, 0, 0, 515, 0, 0));
        rows.Set(table, MovementEdits.FormationRow(Tc, 8, 7, 2, 90, 515, 4, 8));
        poses.LoadFormationRows(Source, rows);
        return (drafts, poses);
    }

    [Fact]
    public void LoadedFormationsShowInEffectiveRows()
    {
        var (_, poses) = Two();
        var rows = poses.FormationRows(Source, Tc);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.UL("memberGUID") == 8 && r.F("dist") == 2 && r.U("point_2") == 8);
    }

    [Fact]
    public void OffsetsRoundTripThroughCommandsAndUndo()
    {
        var (drafts, poses) = Two();
        var schema = Tc;
        var existing = poses.FormationRows(Source, schema).Single(r => r.UL("memberGUID") == 8);
        var edit = MovementEdits.JoinFormation(schema, existing, 8, 7, 4, 180, 3, 0, 0)!;
        var (ok, message) = poses.Commit(null, Source, "Set formation offset", [edit]);
        Assert.True(ok, message);
        Assert.Equal(4f, poses.FormationRows(Source, schema).Single(r => r.UL("memberGUID") == 8).F("dist"));
        poses.Undo(null);
        Assert.Equal(2f, poses.FormationRows(Source, schema).Single(r => r.UL("memberGUID") == 8).F("dist"));
        Assert.True(drafts.Draft!.CanRedo);
        _ = schema;
    }

    [Fact]
    public void LeavingDeletesTheMemberRow()
    {
        var (_, poses) = Two();
        var existing = poses.FormationRows(Source, Tc).Single(r => r.UL("memberGUID") == 8);
        var (ok, message) = poses.Commit(null, Source, "Leave formation", [MovementEdits.LeaveFormation(Tc, existing)]);
        Assert.True(ok, message);
        Assert.DoesNotContain(poses.FormationRows(Source, Tc), r => r.UL("memberGUID") == 8);
        Assert.Contains(poses.FormationRows(Source, Tc), r => r.UL("memberGUID") == 7);
    }

    [Fact]
    public void DuplicateManyCarriesExtraFormationRemaps()
    {
        var (drafts, poses) = Two();
        var copies = new List<(ulong Guid, ulong NewGuid, WorldPose Pose)> { (7, 70, Here), (8, 80, Here) };
        var extra = new List<RowEdit>();
        foreach (var row in poses.FormationRows(Source, Tc))
        {
            var edit = MovementEdits.JoinFormation(Tc, null, row.UL("memberGUID") == 7 ? 70ul : 80ul, row.UL("leaderGUID") == 7 ? 70ul : 80ul,
                row.F("dist"), row.F("angle"), row.U("groupAI"), row.U("point_1"), row.U("point_2"));
            if (edit != null)
            {
                extra.Add(edit);
            }
        }

        var (ok, message) = poses.DuplicateMany(null, Tc, Source, copies, "Duplicate", extra);
        Assert.True(ok, message);
        var formationEdits = drafts.Draft!.NetEdits().Where(e => e.Edit.Table == "creature_formations").ToList();
        Assert.Equal(2, formationEdits.Count);
        string apply = SqlExport.Build(Source, [], drafts.Draft.NetEdits().Select(e => e.Edit), null, Tc).Apply;
        Assert.Contains("INSERT INTO `creature_formations`", apply);
        Assert.Contains("`memberGUID` = 80", apply);
    }

    [Theory]
    [InlineData(0, 0, 10, 0, 180)]
    [InlineData(0, 0, 0, 10, 270)]
    [InlineData(0, 0, -10, 0, 0)]
    [InlineData(0, 0, 0, -10, 90)]
    [InlineData(5, 5, 5, 5, 0)]
    public void FormationOffsetsAreBearingMemberToLeader(double lx, double ly, double mx, double my, float angle)
    {
        var (dist, computed) = MovementEdits.FormationOffset(lx, ly, mx, my);
        Assert.Equal(MathF.Sqrt((float)((mx - lx) * (mx - lx) + (my - ly) * (my - ly))), dist, 3);
        Assert.Equal(angle, computed, 3);
    }

    [Fact]
    public void GroupAiSplitsIntoAssistAndFollow()
    {
        Assert.Equal((0u, false), MovementEdits.SplitGroupAI(0));
        Assert.Equal((3u, true), MovementEdits.SplitGroupAI(515));
        Assert.Equal(513u, MovementEdits.GroupAI(1, true));
        Assert.Equal("Mutual assist", MovementEdits.AssistName(WorldDialect.AzerothCore, 3));
        Assert.Equal("Leader assists members", MovementEdits.AssistName(WorldDialect.TrinityCore, 2));
        Assert.Equal("Members assist each other", MovementEdits.AssistName(WorldDialect.AzerothCore, 2));
    }
}
