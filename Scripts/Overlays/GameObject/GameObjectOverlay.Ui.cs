using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>Read-only GameObject inspector; filtering/source settings live on the shared Scene page.</summary>
public partial class GameObjectOverlay
{
    Button duplicate = null!, remove = null!;
    Button undo = null!, redo = null!, drop = null!, reset = null!, copyGo = null!;
    // Server-space pose entry: X, Y, Z and heading in degrees. Enter in any field commits all four.
    readonly LineEdit[] poseFields = new LineEdit[4];

    public Control BuildPanel()
    {
        var page = InspectorUi.Page("GameObjects", out var scroll);
        (search, list, status, fly) = InspectorUi.ListSection(page, "Nearby objects", "Doors, chests and props from the world database.",
            "Name, GUID or template…", queryStatus, Select, Refresh, FlyToSelection,
            () =>
            {
                Populate();
                query.MarkSearchChanged();
            }, 255, listFontSize: 14, listName: "GameObjectList", flyDisabled: true);
        BuildPlaceCard(page);
        var selection = InspectorUi.SelectionCard(page, "Select a GameObject", "Click a model or choose one above");
        var card = selection.Box;
        title = selection.Title;
        identity = selection.Identity;
        details = InspectorUi.Text("Selection details appear here.", 13, true);
        card.AddChild(details);
        card.AddChild(new HSeparator());
        (undo, redo) = InspectorUi.HistoryRow(card, () => History(false), () => History(true));
        var copies = new HBoxContainer();
        card.AddChild(copies);
        duplicate = InspectorUi.Button("Duplicate", Duplicate);
        duplicate.TooltipText = "Copy two yards ahead (Ctrl+D)";
        remove = InspectorUi.Button("Delete", Delete);
        remove.TooltipText = "Delete this spawn (Del)";
        copies.AddChild(duplicate);
        copies.AddChild(remove);
        InspectorUi.PoseCard(card, poseFields, SubmitPose);

        (drop, reset, copyGo) = InspectorUi.PlacementButtons(card, this);
        BuildSpawnFields(card);
        return scroll;
    }

    /// <summary>Parse the four pose fields with invariant culture and commit them as one draft command.</summary>
    void SubmitPose()
    {
        if (!EditorFields.TryParseAll(poseFields, out var values))
        {
            Say("Pose fields must be numbers (use . for decimals).");
            ShowSelection();
            return;
        }

        SetPose(values[0], values[1], values[2], values[3]);
    }

    /// <summary>Show the effective pose in the entry fields; editing is enabled only with a writable draft.</summary>
    void ShowPose(WorldPose? pose)
    {
        double[] values = pose == null ? [] : [pose.X, pose.Y, pose.Z, Mathf.RadToDeg(WorldCoordinates.NormalizeHeading(pose.Heading))];
        for (int i = 0; i < 4; i++)
        {
            poseFields[i].Text = pose == null ? "" : values[i].ToString(i == 3 ? "F2" : "F4", System.Globalization.CultureInfo.InvariantCulture);
            poseFields[i].Editable = pose != null && poses?.Available == true;
        }
    }

    void UpdateInspectorActions()
    {
        bool editable = CanEdit;
        drop.Disabled = !editable || GroundAt == null;
        reset.Disabled = !editable;
        copyGo.Disabled = !selected.HasValue;
        undo.Disabled = !CanUndo;
        redo.Disabled = !CanRedo;
        duplicate.Disabled = !CanDuplicate;
        remove.Disabled = !CanDelete;
    }

    /// <summary>
    /// Move the camera near the selected GameObject's effective (draft) pose.
    /// </summary>
    public void FlyToSelection()
    {
        if (selected.HasValue && lookup.TryGetValue(selected.Value, out var item))
        {
            FocusRequested?.Invoke(PlacementOf(item).Pose);
        }
    }
}
