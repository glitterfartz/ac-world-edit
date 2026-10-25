using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Waypoints tab: nearby path list, display options, selected-point fields and placement actions.
/// Typed values commit on Enter; every change goes through the editing partial's draft.
/// </summary>
public partial class WaypointOverlay
{
    static readonly string[] MoveTypes = ["Walk", "Run", "Land", "Take off"];
    ItemList list = null!;
    LineEdit search = null!;
    readonly LineEdit[] position = new LineEdit[3];
    Label status = null!, title = null!, identity = null!, details = null!;
    Button fly = null!, drop = null!, reset = null!, copyGo = null!, undo = null!, redo = null!;
    CheckBox all = null!;
    Button preview = null!;

    public Control BuildPanel()
    {
        var page = InspectorUi.Page("Waypoints", out var scroll);
        (search, list, status, fly) = InspectorUi.ListSection(page, "Waypoints", "waypoint_data paths of the creatures in the streamed region.",
            "Path ID, creature name or GUID…", "Waiting for a world…", id => SelectPath((uint)id), Refresh, FlyToSelection, PopulateList, 170);
        var display = InspectorUi.Card(page, "Pathing");
        all = new CheckBox
        {
            Text = "Show every path in the region",
            TooltipText = "Off: only the path of the selected creature or waypoint is drawn."
        };
        all.Toggled += value => ShowAll = value;
        display.AddChild(all);
        preview = new Button
        {
            Text = "Preview walk",
            ToggleMode = true,
            CustomMinimumSize = new(0, 34),
            TooltipText = "Walk a marker along the path at default creature speeds, pausing at each point's delay and looping back to the first point."
        };
        preview.Toggled += value => SetPreview(value);
        display.AddChild(preview);
        display.AddChild(InspectorUi.Text("Green to orange follows the walking direction; purple closes the loop. Yellow dots have a delay; grey paths belong to creatures whose MovementType is not 2.", 11, true));

        var selection = InspectorUi.SelectionCard(page, "Select a waypoint", "Click a point or choose a path above");
        var card = selection.Box;
        title = selection.Title;
        identity = selection.Identity;
        EditorFields.CoordinateGrid(card, ["X", "Y", "Z"], position, SubmitPosition);
        (drop, reset, copyGo) = InspectorUi.PlacementButtons(card, this);
        var history = new HBoxContainer();
        card.AddChild(history);
        undo = InspectorUi.Button("Undo", Undo);
        redo = InspectorUi.Button("Redo", Redo);
        history.AddChild(undo);
        history.AddChild(redo);
        BuildProps(page);
        var notes = InspectorUi.Card(page, "Details");
        details = InspectorUi.Text("Selection details appear here.", 12, true);
        notes.AddChild(details);
        ShowSelection();
        return scroll;
    }

    void PopulateList()
    {
        if (list == null)
        {
            return;
        }

        list.Clear();
        string term = search.Text.Trim();
        foreach (var path in lookup.Values.OrderBy(p => p.Id))
        {
            string users = path.Users.Length switch
            {
                0 => "",
                1 => path.Users[0].Name,
                _ => $"{path.Users[0].Name} +{path.Users.Length - 1}"
            };
            if (term.Length > 0 && !(path.Id.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.Ordinal)
                || path.Users.Any(u => u.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || u.Guid.ToString(CultureInfo.InvariantCulture) == term)))
            {
                continue;
            }

            int index = list.AddItem($"Path {path.Id}   ·   {path.Nodes.Length} points   ·   {users}");
            list.SetItemMetadata(index, path.Id.ToString(CultureInfo.InvariantCulture));
            list.SetItemTooltip(index, string.Join("\n", path.Users.Take(12).Select(u => $"{u.Name} #{u.Guid}{(u.Walks ? "" : " (not following)")}")));
            if (selected?.Path == path.Id)
            {
                list.Select(index);
            }
        }
    }

    /// <summary>
    /// The property card: one labeled field per editable waypoint_data column.
    /// </summary>
    void BuildProps(VBoxContainer page)
    {
        var card = InspectorUi.Card(page, "Point properties");
        LineEdit Field(string caption, string tip, Action<string> submit)
        {
            var row = new HBoxContainer();
            card.AddChild(row);
            var label = InspectorUi.Text(caption, 12, true);
            label.AutowrapMode = TextServer.AutowrapMode.Off;
            label.CustomMinimumSize = new(120, 0);
            label.TooltipText = tip;
            row.AddChild(label);
            var edit = new LineEdit
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = tip,
                SelectAllOnFocus = true
            };
            edit.TextSubmitted += text => submit(text.Trim());
            row.AddChild(edit);
            return edit;
        }

        delay = Field("Delay (ms)", "Pause at this point, in milliseconds.", text => ParseThen<uint>(text, "Delay must be a whole number of milliseconds.", SetDelay));
        var moveRow = new HBoxContainer();
        card.AddChild(moveRow);
        var moveLabel = InspectorUi.Text("Move type", 12, true);
        moveLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        moveLabel.CustomMinimumSize = new(120, 0);
        moveRow.AddChild(moveLabel);
        moveType = new OptionButton
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FitToLongestItem = false,
            TooltipText = "How the creature travels to this point."
        };
        foreach (string name in MoveTypes)
        {
            moveType.AddItem(name);
        }

        moveType.ItemSelected += index => SetMoveType((int)index);
        moveRow.AddChild(moveType);
        facing = Field("Facing (°)", "Direction to face while waiting; empty for none. The server only applies it to points with a delay.", text =>
        {
            if (text.Length == 0)
            {
                SetFacing(null);
            }
            else if (text == FacingText(SelectedNode() is { } current ? StateOf(current.Path, current.Node).Props.Orientation : null))
            {
                ShowSelection();
            }
            else
            {
                ParseThen<double>(text, "Facing must be a number of degrees.", degrees => SetFacing(degrees * Math.PI / 180));
            }
        });
        velocity = Field("Velocity (yd/s)", "Speed to this point; 0 uses the creature's walk or run speed.", text => ParseThen<double>(text, "Velocity must be a number.", SetVelocity));
        smooth = new CheckBox
        {
            Text = "Smooth transition",
            TooltipText = "Curve through this point without stopping; points with a delay still stop."
        };
        smooth.Toggled += value => SetSmooth(value);
        card.AddChild(smooth);
        action = Field("Action", "waypoint_scripts ID run on arrival; 0 for none.", text => ParseThen<int>(text, "Action must be a whole number.", SetAction));
        actionChance = Field("Action chance (%)", "Chance that the action runs.", text => ParseThen<int>(text, "Action chance must be a whole number.", SetActionChance));
    }

    LineEdit delay = null!, facing = null!, velocity = null!, action = null!, actionChance = null!;
    OptionButton moveType = null!;
    CheckBox smooth = null!;

    static string FacingText(double? radians) => radians is double r ? (r * 180 / Math.PI).ToString("F2", CultureInfo.InvariantCulture) : "";

    /// <summary>
    /// Parse a typed number and pass it on; bad text shows the message in the status bar and
    /// restores the fields.
    /// </summary>
    void ParseThen<T>(string text, string error, Action<T> apply) where T : IParsable<T>
    {
        if (T.TryParse(text, CultureInfo.InvariantCulture, out var value))
        {
            apply(value);
            return;
        }

        Say(error);
        ShowSelection();
    }

    /// <summary>
    /// Fill the property controls from the effective values; null clears and disables them.
    /// </summary>
    void ShowProps(WaypointProps? props)
    {
        bool editable = props.HasValue && draft != null;
        var p = props.GetValueOrDefault();
        delay.Text = props.HasValue ? p.Delay.ToString(CultureInfo.InvariantCulture) : "";
        facing.Text = props.HasValue ? FacingText(p.Orientation) : "";
        velocity.Text = props.HasValue ? p.Velocity.ToString("0.####", CultureInfo.InvariantCulture) : "";
        action.Text = props.HasValue ? p.Action.ToString(CultureInfo.InvariantCulture) : "";
        actionChance.Text = props.HasValue ? p.ActionChance.ToString(CultureInfo.InvariantCulture) : "";
        smooth.SetPressedNoSignal(props.HasValue && p.Smooth);
        if (props.HasValue)
        {
            moveType.Select(Math.Clamp(p.MoveType, 0, MoveTypes.Length - 1));
        }

        foreach (var edit in new[] { delay, facing, velocity, action, actionChance })
        {
            edit.Editable = editable;
        }

        moveType.Disabled = !editable;
        smooth.Disabled = !editable;
    }

    void SubmitPosition()
    {
        if (!EditorFields.TryParseAll(position, out var values))
        {
            Say("Position fields must be numbers (use . for decimals).");
            ShowSelection();
            return;
        }

        SetPosition(values[0], values[1], values[2]);
    }

    /// <summary>
    /// Refresh the selection card and details from the effective position.
    /// </summary>
    void ShowSelection()
    {
        if (title == null)
        {
            return;
        }

        if (SelectedNode() is not { } current)
        {
            title.Text = "Select a waypoint";
            identity.Text = emphasized.HasValue ? $"Path {emphasized} is shown; click one of its points" : "Click a point or choose a path above";
            details.Text = "Selection details appear here.";
            foreach (var field in position)
            {
                field.Text = "";
                field.Editable = false;
            }

            ShowProps(null);
            return;
        }

        var (path, node) = current;
        var effective = PositionOf(path, node);
        title.Text = $"Waypoint {node.Point}";
        int index = Array.IndexOf(path.Nodes, node) + 1;
        identity.Text = $"Path {path.Id}   /   Map {path.Map}   /   {index} of {path.Nodes.Length}";
        double[] values = [effective.X, effective.Y, effective.Z];
        for (int i = 0; i < 3; i++)
        {
            position[i].Text = values[i].ToString("F4", CultureInfo.InvariantCulture);
            position[i].Editable = draft != null;
        }

        ShowProps(StateOf(path, node).Props);
        var lines = new List<string>();
        if (draft?.Conflict(sourceKey, path.Id, node.Point, SourceState(node)) == true)
        {
            lines.Add("Source waypoint changed since this draft; draft requires review.");
        }

        if (!path.Active)
        {
            lines.Add("No creature using this path has MovementType 2, so the server loads it but nobody follows it.");
        }

        lines.Add(path.Users.Length == 0 ? "No creature in this region uses the path." : "Used by: " + string.Join(", ", path.Users.Take(8).Select(u => $"{u.Name} #{u.Guid}")) + (path.Users.Length > 8 ? $" and {path.Users.Length - 8} more" : ""));
        if (path.Users.Length > 1)
        {
            lines.Add("Shared path: moving a point changes the route of every creature listed.");
        }

        details.Text = string.Join("\n", lines);
        InspectorUi.SelectRow(list, path.Id);
    }

    void UpdateInspectorActions()
    {
        bool editable = CanEdit;
        fly.Disabled = !selected.HasValue && !emphasized.HasValue;
        drop.Disabled = !editable || GroundAt == null;
        reset.Disabled = !editable;
        copyGo.Disabled = !selected.HasValue;
        undo.Disabled = !CanUndo;
        redo.Disabled = !CanRedo;
        preview.Disabled = !CanPreview;
        if (preview.ButtonPressed != previewing)
        {
            preview.SetPressedNoSignal(previewing);
        }
    }
}
