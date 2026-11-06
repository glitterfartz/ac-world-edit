using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Formation row-edit commands: offset, behavior, turn-around points, membership and the polar-drag hook.
/// </summary>
public partial class CreatureOverlay
{

    /// <summary>Commit formation row edits with one shared-draft command.</summary>
    void CommitFormation(string label, IReadOnlyList<RowEdit> edits, string success)
    {
        if (poses == null || edits.Count == 0)
        {
            if (edits.Count == 0)
            {
                Say("No change.");
            }

            return;
        }

        var (ok, message) = poses.Commit(this, sourceKey, label, edits);
        Say(ok ? success : message);
        if (ok)
        {
            RefreshFormationPairs();
        }

        RefreshFormationCard();
    }

    void SubmitFormationOffset()
    {
        if (!RequireFormation(out var guid, out var schema, out var row) || row == null)
        {
            Say(row == null ? "Join a formation first (Set leader)." : "The formation is still loading; try again.");
            return;
        }

        if (!float.TryParse(formationDist.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float dist)
            || !float.TryParse(formationAngle.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float angle))
        {
            Say("Distance and angle must be numbers (use . for decimals).");
            ShowFormationCard();
            return;
        }

        var edit = MovementEdits.JoinFormation(schema, row, guid, row.UL("leaderGUID"), dist, angle, row.U("groupAI"), row.U("point_1"), row.U("point_2"));
        CommitFormation("Set formation offset", edit == null ? [] : [edit], "Saved the follow offset.");
    }

    /// <summary>Take the offset from where the leader and this member stand now (bearing member-to-leader).</summary>
    void FormationOffsetFromPositions()
    {
        if (!RequireFormation(out var guid, out var schema, out var row) || row == null)
        {
            Say("Join a formation first (Set leader).");
            return;
        }

        ulong leader = row.UL("leaderGUID");
        if (!markers.TryGetValue(leader, out var leaderMarker) || !markers.TryGetValue(guid, out var member))
        {
            Say("Both creatures must be streamed to take the offset from positions.");
            return;
        }

        var (dist, angle) = FormationOffset(PoseOf(leaderMarker.Spawn), PoseOf(member.Spawn));
        var edit = MovementEdits.JoinFormation(schema, row, guid, leader, dist, angle, row.U("groupAI"), row.U("point_1"), row.U("point_2"));
        CommitFormation("Set formation offset", edit == null ? [] : [edit], $"Offset is now {dist:0.#} yd at {angle:0.#}°.");
    }

    /// <summary>Polar offset of a member from its leader: yards plus the bearing from the member to
    /// the leader in degrees, the way the server stores it (it walks the member to the opposite side).</summary>
    public static (float Dist, float Angle) FormationOffset(WorldPose leader, WorldPose member) =>
        MovementEdits.FormationOffset(leader.X, leader.Y, member.X, member.Y);

    void SubmitFormationAi()
    {
        if (formationAssist.GetSelected() < 0)
        {
            return;
        }

        if (!RequireFormation(out var guid, out var schema, out var row) || row == null)
        {
            return;
        }

        uint groupAI = MovementEdits.GroupAI((uint)formationAssist.GetSelected(), formationFollow.ButtonPressed);
        var edit = formationRow != null && formationRow.UL("leaderGUID") == guid && formationRow.UL("memberGUID") == guid
            ? MovementEdits.MakeLeader(schema, formationRow, guid, groupAI)
            : MovementEdits.JoinFormation(schema, row, guid, row.UL("leaderGUID"), row.F("dist"), row.F("angle"), groupAI, row.U("point_1"), row.U("point_2"));
        CommitFormation("Set formation behavior", edit == null ? [] : [edit], "Saved formation behavior.");
    }

    void SubmitFormationPoints()
    {
        if (!RequireFormation(out var guid, out var schema, out var row) || row == null)
        {
            return;
        }

        if (!uint.TryParse(formationPoint1.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint p1)
            || !uint.TryParse(formationPoint2.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint p2))
        {
            Say("Turn-around points must be whole point numbers (0 for unused).");
            ShowFormationCard();
            return;
        }

        var edit = MovementEdits.JoinFormation(schema, row, guid, row.UL("leaderGUID"), row.F("dist"), row.F("angle"), row.U("groupAI"), p1, p2);
        CommitFormation("Set formation turn-around points", edit == null ? [] : [edit], "Saved turn-around points.");
    }

    void FormationPointFromWaypoint(int which)
    {
        if (WaypointPointSelection?.Invoke() is not { } selected)
        {
            Say("Select a point in the Waypoints tab first.");
            return;
        }

        if (!RequireFormation(out var guid, out var schema, out var row) || row == null)
        {
            return;
        }

        // The leader may stream elsewhere; its path comes from the database read on the card.
        if (formationLeaderGuid == 0)
        {
            Say("Join a formation first (Set leader).");
            return;
        }

        if (leaderPathId == 0 || selected.Path != leaderPathId)
        {
            Say(leaderPathId == 0 ? "The leader walks no path; turn-around points need one." : $"That point is on path {selected.Path}, not the leader's path {leaderPathId}.");
            return;
        }

        (formationPoint1.Text, formationPoint2.Text) = which == 1
            ? (selected.Point.ToString(CultureInfo.InvariantCulture), formationPoint2.Text)
            : (formationPoint1.Text, selected.Point.ToString(CultureInfo.InvariantCulture));
        SubmitFormationPoints();
    }

    async void SetFormationLeader()
    {
        if (poses == null || !selectedGuid.HasValue)
        {
            return;
        }

        if (!ulong.TryParse(formationLeader.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong leader) || leader == 0)
        {
            Say("Type the leader's GUID first.");
            return;
        }

        ulong guid = selectedGuid.Value;
        if (leader == guid)
        {
            MakeFormationLeader();
            return;
        }

        string key = sourceKey;
        try
        {
            // The loader drops formations whose guids do not exist, so check first.
            var rows = await DBSource.Open(Connection()).ReadSpawnRowsAsync(SpawnKind.Creature, [leader], default);
            var schema = ContentDraftFiles.Sources.SchemaFor(key);
            if (!IsInsideTree() || selectedGuid != guid)
            {
                return;
            }

            if (rows.Get(schema.Tables.Find("creature"), leader.ToString(CultureInfo.InvariantCulture)) == null)
            {
                Say($"No creature spawn #{leader} exists.");
                return;
            }

            poses.LoadFormationRows(key, await DBSource.Open(Connection()).ReadFormationsAsync([guid, leader], default));
            var existing = poses.FormationRows(key, schema).FirstOrDefault(r => r.UL("memberGUID") == guid);
            var edit = MovementEdits.JoinFormation(schema, existing, guid, leader, 2, 0, MovementEdits.GroupAI(3, true), 0, 0);
            CommitFormation("Join formation", edit == null ? [] : [edit], $"Follows #{leader} now.");
        }
        catch (Exception)
        {
            Say("Could not read that creature; try again.");
        }
    }

    void MakeFormationLeader()
    {
        if (!RequireFormation(out var guid, out var schema, out _))
        {
            return;
        }

        // The controls already show the self-row's values when there is one, so an unchanged
        // click records nothing; a new leader starts from the shown behavior.
        int selected = formationAssist.GetSelected();
        var existing = poses!.FormationRows(sourceKey, schema).FirstOrDefault(r => r.UL("memberGUID") == guid && r.UL("leaderGUID") == guid);
        var edit = MovementEdits.MakeLeader(schema, existing, guid, MovementEdits.GroupAI((uint)Math.Max(0, selected), formationFollow.ButtonPressed));
        CommitFormation("Lead formation", edit == null ? [] : [edit], "Leads a formation now; members join with Set leader.");
    }

    void LeaveFormation()
    {
        if (!RequireFormation(out var guid, out var schema, out var row) || row == null)
        {
            Say("In no formation.");
            return;
        }

        bool self = row.UL("leaderGUID") == guid;
        if (self && poses!.FormationRows(sourceKey, schema).Any(r => r.UL("leaderGUID") == guid && r.UL("memberGUID") != guid))
        {
            Say("This leader still has members; remove them or delete the formation first (the server drops leaderless formations).");
            return;
        }

        CommitFormation("Leave formation", [MovementEdits.LeaveFormation(schema, row)], "Left the formation.");
    }

    void DeleteFormation()
    {
        if (!RequireFormation(out var guid, out var schema, out _))
        {
            return;
        }

        var rows = poses!.FormationRows(sourceKey, schema).Where(r => r.UL("leaderGUID") == guid).ToArray();
        if (rows.Length == 0)
        {
            Say("Leads no formation.");
            return;
        }

        CommitFormation($"Delete formation of #{guid}", rows.Select(r => MovementEdits.LeaveFormation(schema, r)).ToArray(),
            $"Deleted the formation ({rows.Length} row(s)).");
    }

    /// <summary>
    /// Formation inserts for a group duplicate: every formation whose leader and member are both
    /// copied is recreated on the fresh GUIDs in the same command. Returns null when the formation
    /// rows cannot be read (the duplicate is aborted rather than silently dropping the formation).
    /// </summary>
    async Task<IReadOnlyList<RowEdit>?> FormationRemapAsync(IDBSource database, string key, ulong[] originals, ulong[] fresh)
    {
        if (poses == null || originals.Length < 2)
        {
            return [];
        }

        try
        {
            poses.LoadFormationRows(key, await database.ReadFormationsAsync(originals, default));
        }
        catch (Exception)
        {
            Say("Could not read formations; not duplicated.");
            return null;
        }

        var schema = database.Schema;
        var map = originals.Zip(fresh).ToDictionary(p => p.First, p => p.Second);
        var edits = new List<RowEdit>();
        foreach (var row in poses.FormationRows(key, schema))
        {
            if (!map.TryGetValue(row.UL("leaderGUID"), out ulong freshLeader) || !map.TryGetValue(row.UL("memberGUID"), out ulong freshMember))
            {
                continue;
            }

            if (MovementEdits.JoinFormation(schema, null, freshMember, freshLeader, row.F("dist"), row.F("angle"), row.U("groupAI"), row.U("point_1"), row.U("point_2")) is { } edit)
            {
                edits.Add(edit);
            }
        }

        return edits;
    }

    /// <summary>
    /// Gizmo-commit interception for polar drag: when offset dragging is armed and the selected
    /// spawn follows a streamed leader, the drop moves its follow offset instead of its pose.
    /// </summary>
    public bool TryFormationOffset(ulong guid, Foundation.WorldPose drop)
    {
        if (!FormationDragArmed || poses == null)
        {
            return false;
        }

        var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
        var row = poses.FormationRows(sourceKey, schema).FirstOrDefault(r => r.UL("memberGUID") == guid);
        if (row == null || row.UL("leaderGUID") == guid || !markers.TryGetValue(row.UL("leaderGUID"), out var leaderMarker))
        {
            Say("Offset dragging needs a formation member whose leader streams nearby.");
            return true;
        }

        var (dist, angle) = FormationOffset(PoseOf(leaderMarker.Spawn), drop);
        var edit = MovementEdits.JoinFormation(schema, row, guid, row.UL("leaderGUID"), dist, angle, row.U("groupAI"), row.U("point_1"), row.U("point_2"));
        CommitFormation("Drag formation offset", edit == null ? [] : [edit], $"Offset is now {dist:0.#} yd at {angle:0.#}°.");
        Select(guid);
        return true;
    }
}
