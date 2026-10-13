using Godot;
using ACWorldEdit.Data;

namespace ACWorldEdit;
/// <summary>Focused entity page; scene visibility and external connection settings live on the Scene tab.</summary>
public partial class CreatureOverlay
{
    Button fly = null!, undo = null!, redo = null!, drop = null!, reset = null!, copyGo = null!, interact = null!;
    Label interactionSummary = null!;
    // Server-space pose entry: X, Y, Z and heading in degrees. Enter in any field commits all four.
    readonly LineEdit[] poseFields = new LineEdit[4];
    public Control BuildPanel()
    {
        var page = InspectorUi.Page("Creatures", out var scroll);
        page.AddChild(InspectorUi.Text("Nearby creatures", 20));
        page.AddChild(InspectorUi.Text("Search filters the scene before the result limit.", 12, true));
        config = new LineEdit
        {
            Text = System.Environment.GetEnvironmentVariable("ACWORLD_SERVER_CONFIG") ?? CreatureSource.DefaultConfigPath,
            PlaceholderText = "External worldserver.conf path"
        };
        filter = new LineEdit
        {
            Name = "CreatureSearch",
            PlaceholderText = "Name, GUID or template…",
            ClearButtonEnabled = true,
            MaxLength = 80
        };
        page.AddChild(filter);
        filter.TextChanged += _ =>
        {
            PopulateList();
            searchPending = true;
            searchChanged = System.Diagnostics.Stopwatch.GetTimestamp();
        };
        list = new ItemList
        {
            CustomMinimumSize = new(0, 255),
            ClipContents = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        list.AddThemeFontSizeOverride("font_size", 14);
        page.AddChild(list);
        list.ItemSelected += index => Select(uint.Parse(list.GetItemMetadata((int)index).AsString()));
        status = InspectorUi.Text("Waiting for a world…", 12, true);
        page.AddChild(status);
        var actions = new HBoxContainer();
        page.AddChild(actions);
        actions.AddChild(InspectorUi.Button("Refresh", Refresh));
        fly = InspectorUi.Button("Fly to selection", FlyToSelection);
        fly.Disabled = true;
        actions.AddChild(fly);
        var card = InspectorUi.Card(page, "Selection");
        selectionName = InspectorUi.Text("Select a creature", 19);
        card.AddChild(selectionName);
        selectionMeta = InspectorUi.Text("Click a model or choose one above", 12, true);
        card.AddChild(selectionMeta);
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
        var grid = new GridContainer
        {
            Columns = 4
        };
        card.AddChild(grid);
        string[] names = ["X", "Y", "Z", "Heading°"];
        for (int i = 0; i < 4; i++)
        {
            // Plain non-wrapping labels: wrapped text has no minimum width inside a grid cell.
            var label = new Label
            {
                Text = names[i],
                Modulate = InspectorUi.Muted
            };
            label.AddThemeFontSizeOverride("font_size", 12);
            grid.AddChild(label);
            poseFields[i] = new LineEdit
            {
                Editable = false,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new(70, 0),
                SelectAllOnFocus = true
            };
            poseFields[i].TextSubmitted += _ => SubmitPose();
            grid.AddChild(poseFields[i]);
        }

        var placement = new HBoxContainer();
        card.AddChild(placement);
        drop = InspectorUi.Button("Drop to ground", DropToGround);
        reset = InspectorUi.Button("Reset to source", ResetToSource);
        copyGo = InspectorUi.Button("Copy .go", CopyGoCommand);
        copyGo.TooltipText = "Copy a GM teleport (.go xyz X Y Z map heading) to compare this pose in the client";
        placement.AddChild(drop);
        placement.AddChild(reset);
        placement.AddChild(copyGo);
        card.AddChild(InspectorUi.Text("Move (W) drags colored axes; Rotate (E) drags the yellow ring. Changes save to your local draft.", 12, true));
        var interactions = InspectorUi.Card(page, "Interactions");
        interactionSummary = InspectorUi.Text("Select a creature to see its interactions.", 12, true);
        interactions.AddChild(interactionSummary);
        interact = InspectorUi.Button("Interact…", () =>
        {
            if (SelectedSpawn is { } spawn)
            {
                InteractRequested?.Invoke(spawn);
            }
        });
        interactions.AddChild(interact);
        interactions.AddChild(InspectorUi.Text("Or right-click the creature without dragging.", 11, true));
        return scroll;
    }

    /// <summary>
    /// Open the interaction window for a creature (viewport short right-click or Interact…).
    /// </summary>
    public event Action<Foundation.CreatureSpawn>? InteractRequested;

    public void SetInteractionSummary(string text) => interactionSummary.Text = text;

    /// <summary>
    /// Move the camera near the selection's effective draft pose.
    /// </summary>
    public void FlyToSelection()
    {
        if (selectedGuid.HasValue && markers.TryGetValue(selectedGuid.Value, out var marker))
        {
            FocusRequested?.Invoke(marker.Spawn with { Pose = PoseOf(marker.Spawn) });
        }
    }

    /// <summary>
    /// Parse the four pose fields with invariant culture and commit them as one draft command.
    /// </summary>
    void SubmitPose()
    {
        var values = new double[4];
        for (int i = 0; i < 4; i++)
        {
            if (!double.TryParse(poseFields[i].Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out values[i]))
            {
                draftMessage = "Pose fields must be numbers (use . for decimals).";
                if (selectedGuid.HasValue)
                {
                    Select(selectedGuid.Value);
                }

                return;
            }
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
            poseFields[i].Editable = pose != null && draft != null;
        }
    }

    void UpdateInspectorActions()
    {
        bool editable = CanEdit;
        drop.Disabled = !editable || GroundAt == null;
        reset.Disabled = !editable;
        copyGo.Disabled = !selectedGuid.HasValue;
        interact.Disabled = !selectedGuid.HasValue;
        fly.Disabled = !LayerEnabled || !selectedGuid.HasValue || !markers.ContainsKey(selectedGuid.Value);
        undo.Disabled = draft == null || !draft.CanUndo;
        redo.Disabled = draft == null || !draft.CanRedo;
    }
}
