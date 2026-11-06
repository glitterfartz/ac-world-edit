using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The selected creature's formation: which leader it follows (or who follows it), the polar
/// follow offset, group AI and turn-around points. All commands are single undoable row-edit
/// commands; a delete only proceeds when the server would keep the formation (a leader keeps its
/// self-row while members remain, and leaving members are removed first).
/// </summary>
public partial class CreatureOverlay
{
    Label formationStatus = null!, formationLeaderPath = null!;
    LineEdit formationDist = null!, formationAngle = null!, formationPoint1 = null!, formationPoint2 = null!, formationLeader = null!;
    OptionButton formationAssist = null!;
    CheckBox formationFollow = null!, formationDrag = null!;
    Button formationApplyOffset = null!, formationFromPositions = null!, formationSetLeader = null!, formationMakeLeader = null!, formationLeave = null!, formationDelete = null!, formationPoint1Pick = null!, formationPoint2Pick = null!;
    int formationGeneration;
    string formationKey = "";
    ulong formationGuid;
    Row? formationRow;
    ulong formationLeaderGuid;
    ulong leaderPathId;
    int formationMembers;

    /// <summary>The waypoint tab's selected point, for turn-around pickers (wired by the shell).</summary>
    public Func<(uint Path, uint Point)?>? WaypointPointSelection { get; set; }

    /// <summary>When armed, gizmo drags on a formation member move its follow offset, not its pose.</summary>
    public bool FormationDragArmed { get; set; }

    void BuildFormationCard(VBoxContainer page)
    {
        var card = InspectorUi.Card(page, "Formation");
        formationStatus = InspectorUi.Text("Select a creature to see its formation.", 12, true);
        card.AddChild(formationStatus);
        var offset = new HBoxContainer();
        card.AddChild(offset);
        formationDist = FormationNumber(offset, "Dist (yd)", "Follow distance in yards.", SubmitFormationOffset);
        formationAngle = FormationNumber(offset, "Angle (°)", "Bearing from the member to the leader, 0 to 360. The line in the viewport shows the result.", SubmitFormationOffset);
        formationApplyOffset = InspectorUi.Button("Set offset", SubmitFormationOffset);
        formationApplyOffset.TooltipText = "Move this member to the typed offset.";
        offset.AddChild(formationApplyOffset);
        formationFromPositions = InspectorUi.Button("From positions", FormationOffsetFromPositions);
        formationFromPositions.TooltipText = "Take the offset from where the leader and this member stand now.";
        card.AddChild(formationFromPositions);
        var ai = new HBoxContainer();
        card.AddChild(ai);
        formationAssist = InspectorUi.Choice(["No assist", "Members assist the leader", "Leader assists", "Mutual assist"], 0, _ => SubmitFormationAi(), ai, "Who joins the fight when a member is attacked.");
        formationFollow = new CheckBox { Text = "Follow leader" };
        formationFollow.Toggled += _ => SubmitFormationAi();
        ai.AddChild(formationFollow);
        var points = new HBoxContainer();
        card.AddChild(points);
        formationPoint1 = FormationNumber(points, "Point 1", "Leader path node where the member switches sides on a return trip; 0 unused.", SubmitFormationPoints);
        formationPoint2 = FormationNumber(points, "Point 2", "Leader path node where the member switches back; 0 unused.", SubmitFormationPoints);
        formationPoint1Pick = InspectorUi.Button("1←wp", () => FormationPointFromWaypoint(1));
        formationPoint1Pick.TooltipText = "Use the waypoint tab's selected point (must be on the leader's path).";
        points.AddChild(formationPoint1Pick);
        formationPoint2Pick = InspectorUi.Button("2←wp", () => FormationPointFromWaypoint(2));
        formationPoint2Pick.TooltipText = "Use the waypoint tab's selected point (must be on the leader's path).";
        points.AddChild(formationPoint2Pick);
        formationLeaderPath = InspectorUi.Text("", 11, true);
        card.AddChild(formationLeaderPath);
        var leaders = new HBoxContainer();
        card.AddChild(leaders);
        formationLeader = new LineEdit { PlaceholderText = "Leader GUID", CustomMinimumSize = new(110, 0), SelectAllOnFocus = true, MaxLength = 20 };
        formationLeader.TextSubmitted += _ => SetFormationLeader();
        leaders.AddChild(formationLeader);
        formationSetLeader = InspectorUi.Button("Set leader", () => SetFormationLeader());
        formationSetLeader.TooltipText = "Follow the typed creature.";
        leaders.AddChild(formationSetLeader);
        formationMakeLeader = InspectorUi.Button("Make leader", () => MakeFormationLeader());
        formationMakeLeader.TooltipText = "Lead a new formation (warns followers nothing yet).";
        leaders.AddChild(formationMakeLeader);
        var leave = new HBoxContainer();
        card.AddChild(leave);
        formationLeave = InspectorUi.Button("Leave", () => LeaveFormation());
        formationLeave.TooltipText = "Leave the formation (a leader with members must delete it instead).";
        leave.AddChild(formationLeave);
        formationDelete = InspectorUi.Button("Delete formation", () => DeleteFormation());
        formationDelete.TooltipText = "Remove the whole formation: the leader row and every member row.";
        leave.AddChild(formationDelete);
        formationDrag = new CheckBox { Text = "Drag offset (gizmo moves the follow offset, not the pose)" };
        formationDrag.Toggled += armed => FormationDragArmed = armed;
        card.AddChild(formationDrag);
        RefreshFormationCard();
    }

    static LineEdit FormationNumber(HBoxContainer row, string caption, string tip, Action submit)
    {
        var label = InspectorUi.Text(caption, 12, true);
        label.CustomMinimumSize = new(64, 0);
        label.TooltipText = tip;
        row.AddChild(label);
        var edit = new LineEdit { CustomMinimumSize = new(70, 0), SelectAllOnFocus = true, TooltipText = tip };
        edit.TextSubmitted += _ => submit();
        row.AddChild(edit);
        return edit;
    }

    void SetFormationActions(bool editable, bool member, bool leader)
    {
        formationApplyOffset.Disabled = !editable || !member;
        formationFromPositions.Disabled = !editable || !member;
        formationAssist.Disabled = !editable || (!member && !leader);
        formationFollow.Disabled = formationAssist.Disabled;
        formationPoint1.Editable = editable && (member || leader);
        formationPoint2.Editable = editable && (member || leader);
        formationPoint1Pick.Disabled = !formationPoint1.Editable;
        formationPoint2Pick.Disabled = !formationPoint2.Editable;
        formationLeader.Editable = editable;
        formationSetLeader.Disabled = !editable;
        formationMakeLeader.Disabled = !editable;
        formationLeave.Disabled = !editable || (!member && !leader);
        formationDelete.Disabled = !editable || !leader;
        formationDrag.Disabled = !editable || !member;
        if (!member && !leader)
        {
            formationDrag.SetPressedNoSignal(false);
            FormationDragArmed = false;
        }
    }

    /// <summary>Reload the selected spawn's formation rows, then redraw the card and the lines.</summary>
    async void RefreshFormationCard()
    {
        if (formationStatus == null)
        {
            return;
        }

        if (!selectedGuid.HasValue || poses == null || !poses.Available || group.Members.Count > 0 || !markers.TryGetValue(selectedGuid.Value, out _))
        {
            formationStatus.Text = "Select a creature to see its formation.";
            formationLeaderPath.Text = "";
            formationRow = null;
            SetFormationActions(false, false, false);
            return;
        }

        ulong guid = selectedGuid.Value;
        string key = sourceKey;
        int generation = ++formationGeneration;
        formationGuid = guid;
        formationKey = key;
        SetFormationActions(poses.Has(key, guid), false, false);
        formationStatus.Text = "Loading formation…";
        try
        {
            var rows = await DBSource.Open(Connection()).ReadFormationsAsync([guid], default);
            if (!IsInsideTree() || generation != formationGeneration || selectedGuid != guid)
            {
                return;
            }

            poses.LoadFormationRows(key, rows);
            ShowFormationCard();
        }
        catch (Exception)
        {
            if (IsInsideTree() && generation == formationGeneration && selectedGuid == guid)
            {
                formationStatus.Text = "Could not read the formation; try again.";
            }
        }
    }

    /// <summary>Draw the card from the effective formation rows (loaded by <see cref="RefreshFormationCard"/>).</summary>
    void ShowFormationCard()
    {
        if (formationStatus == null || poses == null || selectedGuid != formationGuid)
        {
            return;
        }

        var schema = ContentDraftFiles.Sources.SchemaFor(formationKey);
        var rows = poses.FormationRows(formationKey, schema);
        formationRow = rows.FirstOrDefault(r => r.UL("memberGUID") == formationGuid);
        bool member = formationRow != null && formationRow.UL("leaderGUID") != formationGuid;
        bool leader = rows.Any(r => r.UL("leaderGUID") == formationGuid);
        formationLeaderGuid = formationRow?.UL("leaderGUID") ?? 0;
        formationMembers = rows.Count(r => r.UL("leaderGUID") == formationGuid && r.UL("memberGUID") != formationGuid);
        bool editable = poses.Has(formationKey, formationGuid);
        if (formationRow == null)
        {
            formationStatus.Text = leader ? $"Leads a formation with {formationMembers} member(s)." : "In no formation.";
        }
        else if (!member)
        {
            formationStatus.Text = $"Leads a formation with {formationMembers} member(s).";
        }
        else
        {
            string leaderName = markers.TryGetValue(formationLeaderGuid, out var leaderMarker) ? leaderMarker.Spawn.Name : "creature";
            formationStatus.Text = $"Follows {leaderName} #{formationLeaderGuid}.";
        }

        if (member)
        {
            formationDist.Text = formationRow!.F("dist").ToString("0.##", CultureInfo.InvariantCulture);
            formationAngle.Text = formationRow.F("angle").ToString("0.##", CultureInfo.InvariantCulture);
        }
        else
        {
            formationDist.Text = "";
            formationAngle.Text = "";
        }

        var (assist, follow) = MovementEdits.SplitGroupAI(formationRow?.U("groupAI") ?? 0);
        var dialect = ContentDraftFiles.Sources.DialectOf(formationKey);
        for (int i = 0; i < 4; i++)
        {
            formationAssist.SetItemText(i, MovementEdits.AssistName(dialect, (uint)i));
        }

        formationAssist.Select((int)Math.Min(assist, 3));
        formationFollow.SetPressedNoSignal(follow);
        formationPoint1.Text = formationRow?.U("point_1").ToString(CultureInfo.InvariantCulture) ?? "";
        formationPoint2.Text = formationRow?.U("point_2").ToString(CultureInfo.InvariantCulture) ?? "";
        SetFormationActions(editable, member, leader);
        RefreshFormationLeaderPath(member ? formationLeaderGuid : leader ? formationGuid : 0);
        RefreshFormationPairs();
    }

    async void RefreshFormationLeaderPath(ulong leader)
    {
        formationLeaderPath.Text = "";
        leaderPathId = 0;
        if (leader == 0 || poses == null)
        {
            return;
        }

        string key = formationKey;
        int generation = formationGeneration;
        try
        {
            var use = (await DBSource.Open(Connection()).ReadSpawnPathsAsync([leader], default)).FirstOrDefault();
            if (!IsInsideTree() || generation != formationGeneration)
            {
                return;
            }

            leaderPathId = use?.PathId ?? 0;
            formationLeaderPath.Text = use == null ? "The leader walks no path." : $"Leader path {use.PathId} ({use.Nodes} point(s)). Turn-around points name that path's nodes.";
        }
        catch (Exception)
        {
            // Silent here; the card still edits the formation.
        }
    }

    bool RequireFormation(out ulong guid, out IWorldSchema schema, out Row? row)
    {
        guid = 0;
        schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
        row = null;
        if (poses == null || !selectedGuid.HasValue)
        {
            Say("Select a creature first.");
            return false;
        }

        if (formationKey != sourceKey || formationGuid != selectedGuid.Value)
        {
            Say("The formation is still loading; try again.");
            return false;
        }

        ulong id = selectedGuid.Value;
        guid = id;
        row = poses.FormationRows(sourceKey, schema).FirstOrDefault(r => r.UL("memberGUID") == id);
        return true;
    }

    /// <summary>Formation rows touching the streamed creatures, for the lines and the card.</summary>
    async void LoadRegionFormations()
    {
        if (poses == null || !poses.Available || records.Count == 0)
        {
            return;
        }

        string key = sourceKey;
        int generation = ++formationGeneration;
        try
        {
            var rows = await DBSource.Open(Connection()).ReadFormationsAsync(records.Select(s => s.Guid).ToList(), default);
            if (!IsInsideTree() || generation != formationGeneration || key != sourceKey)
            {
                return;
            }

            poses.LoadFormationRows(key, rows);
            RefreshFormationPairs();
        }
        catch (Exception)
        {
            // Silent here; the card reports a failure when its selection asks.
        }
    }

    /// <summary>Leader-member pairs from the effective formation rows (self-rows excluded).</summary>
    void RefreshFormationPairs()
    {
        formationPairs.Clear();
        if (poses?.Available == true)
        {
            var schema = ContentDraftFiles.Sources.SchemaFor(sourceKey);
            foreach (var row in poses.FormationRows(sourceKey, schema))
            {
                ulong leader = row.UL("leaderGUID"), member = row.UL("memberGUID");
                if (leader != member)
                {
                    formationPairs.Add((leader, member));
                }
            }
        }

        RebuildFormationLines();
    }

    void RebuildFormationLines()
    {
        var positioned = new List<(Vector3 Leader, Vector3 Member)>();
        foreach (var (leader, member) in formationPairs)
        {
            if (markers.TryGetValue(leader, out var l) && markers.TryGetValue(member, out var m))
            {
                positioned.Add((l.Node.GlobalPosition, m.Node.GlobalPosition));
            }
        }

        if (positioned.Count == 0)
        {
            formationLines.Clear();
        }
        else
        {
            formationLines.Rebuild(positioned);
        }

        formationLines.SetVisible(LayerEnabled);
    }
}
