using Godot;
using ACWorldEdit.Data;

namespace ACWorldEdit;
/// <summary>Focused entity page; scene visibility and external connection settings live on the Scene tab.</summary>
public partial class CreatureOverlay
{
    Button fly = null!, undo = null!, redo = null!, drop = null!, reset = null!, copyGo = null!, properties = null!;
    Label interactionSummary = null!;
    // Server-space pose entry: X, Y, Z and heading in degrees. Enter in any field commits all four.
    readonly LineEdit[] poseFields = new LineEdit[4];
    public Control BuildPanel()
    {
        var page = InspectorUi.Page("Creatures", out var scroll);
        (filter, list, status, fly) = InspectorUi.ListSection(page, "Nearby creatures", "Search filters the scene before the result limit.",
            "Name, GUID or template…", "Waiting for a world…", Select, Refresh, FlyToSelection,
            () =>
            {
                PopulateList();
                query.MarkSearchChanged();
            }, 255, listFontSize: 14, searchName: "CreatureSearch", flyDisabled: true, clipList: true);
        var selection = InspectorUi.SelectionCard(page, "Select a creature", "Click a model or choose one above");
        var card = selection.Box;
        selectionName = selection.Title;
        selectionMeta = selection.Identity;
        details = InspectorUi.Text("Selection details appear here.", 13, true);
        card.AddChild(details);
        card.AddChild(new HSeparator());
        card.AddChild(InspectorUi.Text("Animation preview", 12, true));
        animation = new OptionButton
        {
            FitToLongestItem = false,
            Disabled = true
        };
        animation.AddItem("Stand / idle", 0);
        card.AddChild(animation);
        animation.ItemSelected += index =>
        {
            previewAnimation = (ushort)animation.GetItemId((int)index);
            modelUpdate = 0;
        };
        var history = new HBoxContainer();
        card.AddChild(history);
        undo = InspectorUi.Button("Undo move", () => History(false));
        redo = InspectorUi.Button("Redo move", () => History(true));
        history.AddChild(undo);
        history.AddChild(redo);
        card.AddChild(new HSeparator());
        card.AddChild(InspectorUi.Text("Pose (server coordinates)", 12, true));
        EditorFields.CoordinateGrid(card, ["X", "Y", "Z", "Heading°"], poseFields, SubmitPose);
        foreach (var field in poseFields)
        {
            field.Editable = false;
        }

        (drop, reset, copyGo) = InspectorUi.PlacementButtons(card, this);
        var interactions = InspectorUi.Card(page, "Interactions");
        interactionSummary = InspectorUi.Text("Select a creature to see its interactions.", 12, true);
        interactions.AddChild(interactionSummary);
        properties = InspectorUi.Button("Properties…", () =>
        {
            if (SelectedSpawn is { } spawn)
            {
                PropertiesRequested?.Invoke(spawn);
            }
        });
        properties.TooltipText = "Gossip, quests, vendor, trainer, behavior and speech of this creature";
        interactions.AddChild(properties);
        interactions.AddChild(InspectorUi.Text("Or right-click the creature.", 11, true));
        return scroll;
    }

    /// <summary>
    /// Open the creature properties window for a creature (viewport short right-click or Properties…).
    /// </summary>
    public event Action<Foundation.CreatureSpawn>? PropertiesRequested;

    public void SetInteractionSummary(string text) => interactionSummary.Text = text;

    /// <summary>
    /// Move the camera near the selection's effective draft pose.
    /// </summary>
    public void FlyToSelection()
    {
        if (selectedGuid.HasValue && markers.TryGetValue(selectedGuid.Value, out var marker))
        {
            FocusRequested?.Invoke(PoseOf(marker.Spawn));
        }
    }

    /// <summary>
    /// Parse the four pose fields with invariant culture and commit them as one draft command.
    /// </summary>
    void SubmitPose()
    {
        if (!EditorFields.TryParseAll(poseFields, out var values))
        {
            Say("Pose fields must be numbers (use . for decimals).");
            if (selectedGuid.HasValue)
            {
                Select(selectedGuid.Value);
            }

            return;
        }

        SetPose(values[0], values[1], values[2], values[3]);
    }

    /// <summary>
    /// Show the effective pose in the entry fields; editing is enabled only with a writable draft.
    /// </summary>
    void ShowPose(Foundation.WorldPose? pose)
    {
        double[] values = pose == null ? [] : [pose.X, pose.Y, pose.Z, Mathf.RadToDeg(Foundation.WorldCoordinates.NormalizeHeading(pose.Heading))];
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
        copyGo.Disabled = !selectedGuid.HasValue;
        properties.Disabled = !selectedGuid.HasValue;
        fly.Disabled = !LayerEnabled || !selectedGuid.HasValue || !markers.ContainsKey(selectedGuid.Value);
        undo.Disabled = !CanUndo;
        redo.Disabled = !CanRedo;
    }
}
