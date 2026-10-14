using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// UI composition for the viewer. Streaming, archive ownership and camera movement remain in the main partial; controls never write source data.
/// </summary>
public partial class TerrainViewer
{
    EditorMenu menu = null!;
    EditorToolbar toolbar = null!;
    PanelContainer leftPanel = null!;
    bool showCellBorders;

    /// <summary>
    /// Build the menu/tool strip above a body holding the navigation panel, Scene explorer and
    /// telemetry. The strip sits in a VBox so panels never overlap it. Actions only reload sources,
    /// change previews or record local drafts.
    /// </summary>
    void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var frame = new VBoxContainer
        {
            Name = "Frame",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        frame.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        frame.AddThemeConstantOverride("separation", 0);
        layer.AddChild(frame);
        // Ignore filters let clicks on empty viewport areas fall through to world picking.
        var ui = new Control
        {
            Name = "Interface",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        dataDirectory = ProjectSettings.GlobalizePath("res://wow-data");
        var theme = new Theme();
        theme.DefaultFontSize = 15;
        theme.SetColor("font_color", "Label", new Color("e7eef4"));
        theme.SetColor("font_color", "Button", new Color("e7eef4"));
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.08f, 0.11f, 0.94f),
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 16,
            ContentMarginBottom = 16
        };
        theme.SetStylebox("panel", "PanelContainer", style);
        ui.Theme = theme;
        frame.AddChild(BuildStrip(theme));
        frame.AddChild(ui);
        // Anchored to the window bottom (matching the telemetry bar and Scene explorer below) and
        // scrollable so added controls never clip off-screen on a shorter window.
        var panel = leftPanel = new PanelContainer
        {
            AnchorBottom = 1,
            OffsetLeft = 20,
            OffsetTop = 20,
            OffsetBottom = -82,
            CustomMinimumSize = new Vector2(330, 0)
        };
        ui.AddChild(panel);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        panel.AddChild(scroll);
        var box = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        box.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(box);
        var title = new Label
        {
            Text = "AC WORLD EDIT"
        };
        title.AddThemeFontSizeOverride("font_size", 24);
        box.AddChild(title);
        var subtitle = new Label
        {
            Text = "WORLD VIEWER  /  WRATH 3.3.5"
        };
        subtitle.AddThemeColorOverride("font_color", new Color("5bd8c2"));
        subtitle.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(subtitle);
        box.AddChild(new HSeparator());
        box.AddChild(new Label { Text = "World" });
        maps = new OptionButton();
        box.AddChild(maps);
        maps.ItemSelected += id =>
        {
            if ((int)id < mapList.Count)
            {
                var m = mapList[(int)id];
                tileX.Value = m.Directory == "Azeroth" ? 34 : 32;
                tileY.Value = m.Directory == "Azeroth" ? 49 : 32;
            }
        };
        var row = new HBoxContainer();
        box.AddChild(row);
        row.AddChild(new Label { Text = "Tile X" });
        tileX = new SpinBox
        {
            MinValue = 0,
            MaxValue = 63,
            Value = 34
        };
        row.AddChild(tileX);
        row.AddChild(new Label { Text = "Y" });
        tileY = new SpinBox
        {
            MinValue = 0,
            MaxValue = 63,
            Value = 49
        };
        row.AddChild(tileY);
        open = new Button
        {
            Text = "Open world",
            Disabled = true
        };
        open.Pressed += async () => await OpenMap();
        box.AddChild(open);
        var radiusRow = new HBoxContainer();
        box.AddChild(radiusRow);
        radiusRow.AddChild(new Label { Text = "Nearby tile radius" });
        distance = new SpinBox
        {
            MinValue = 0,
            MaxValue = 3,
            Value = 1
        };
        radiusRow.AddChild(distance);
        distance.ValueChanged += value =>
        {
            radius = (int)value;
            UpdateWanted();
        };
        box.AddChild(new HSeparator());
        box.AddChild(new Label { Text = "Time of day" });
        var timeRow = new HBoxContainer();
        box.AddChild(timeRow);
        playTime = new Button
        {
            Text = "⏸",
            CustomMinimumSize = new Vector2(32, 0),
            TooltipText = "Pause/resume the day-night cycle"
        };
        playTime.Pressed += () =>
        {
            sky.Playing = !sky.Playing;
            playTime.Text = sky.Playing ? "⏸" : "▶";
        };
        timeRow.AddChild(playTime);
        timeOfDay = new HSlider
        {
            MinValue = 0,
            MaxValue = 24,
            Step = 1.0 / 60,
            Value = sky.TimeOfDayHours,
            CustomMinimumSize = new Vector2(190, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Scrub the time of day"
        };
        timeOfDay.ValueChanged += value => sky.SetTimeOfDay(value);
        timeRow.AddChild(timeOfDay);
        timeOfDayLabel = new Label
        {
            CustomMinimumSize = new Vector2(48, 0)
        };
        timeRow.AddChild(timeOfDayLabel);
        var speedRow = new HBoxContainer();
        box.AddChild(speedRow);
        speedRow.AddChild(new Label { Text = "Cycle speed" });
        var speed = new OptionButton();
        string[] speedLabels = ["0.5x", "1x", "2x", "5x", "20x"];
        float[] speedValues = [0.5f, 1f, 2f, 5f, 20f];
        foreach (string label in speedLabels)
        {
            speed.AddItem(label);
        }

        speed.Select(1);
        speed.ItemSelected += id => sky.Speed = speedValues[id];
        speedRow.AddChild(speed);
        box.AddChild(new HSeparator());
        status = new Label
        {
            Text = message,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(294, 55)
        };
        box.AddChild(status);
        var bottom = new PanelContainer
        {
            AnchorTop = 1,
            AnchorBottom = 1,
            AnchorRight = 1,
            OffsetLeft = 20,
            OffsetRight = -20,
            OffsetTop = -62,
            OffsetBottom = -18
        };
        ui.AddChild(bottom);
        telemetry = new Label
        {
            Text = "Waiting for terrain"
        };
        bottom.AddChild(telemetry);
        zoneLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        zoneLabel.AddThemeColorOverride("font_color", new Color("5bd8c2"));
        zoneLabel.AddThemeFontSizeOverride("font_size", 17);
        box.AddChild(zoneLabel);
        continentLabel = new Label();
        continentLabel.AddThemeColorOverride("font_color", new Color("aabac8"));
        continentLabel.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(continentLabel);
        minimap = new TerrainMinimap
        {
            CustomMinimumSize = new Vector2(180, 180),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        box.AddChild(minimap);
        minimap.Jump += key =>
        {
            tileX.Value = key.X;
            tileY.Value = key.Y;
            Teleport(key);
        };
        sidebar = new SceneSidebar(creatures, gameObjects, graveyards, areaTriggers, objects, connection);
        ui.AddChild(sidebar);
        drafts = ContentDrafts.Open();
        interaction = new InteractionWindow
        {
            ConnectionPath = () => connection.Text,
            Store = () => store,
            Drafts = drafts
        };
        behaviorEditor = new BehaviorEditorWindow
        {
            ConnectionPath = () => connection.Text,
            Store = () => store,
            Drafts = drafts
        };
        questEditor = new QuestEditorWindow
        {
            ConnectionPath = () => connection.Text,
            Store = () => store,
            Areas = () => areas,
            Drafts = drafts,
            FlyToEntry = LocateCreature,
            BehaviorRequested = entry => behaviorEditor.Open(entry, 0)
        };
        behaviorEditor.QuestEditorRequested = questEditor.Open;
        // Floating windows sit on the UI layer above the panels.
        layer.AddChild(interaction);
        layer.AddChild(questEditor);
        layer.AddChild(behaviorEditor);
        interaction.SummaryChanged += creatures.SetInteractionSummary;
        interaction.QuestEditorRequested += questEditor.Open;
        creatures.InteractRequested += interaction.Open;
        creatures.BehaviorRequested += spawn => behaviorEditor.Open(spawn.TemplateId, spawn.Guid);
        interaction.BehaviorRequested += behaviorEditor.Open;
        areaTriggers.BehaviorRequested += trigger => behaviorEditor.OpenAreaTrigger(trigger.Entry);
        BuildMenus();
        BuildToolbar();
    }

    InteractionWindow interaction = null!;
    /// <summary>
    /// The shared local content draft for interaction, quest and behavior editing.
    /// </summary>
    ContentDrafts drafts = null!;
    QuestEditorWindow questEditor = null!;
    BehaviorEditorWindow behaviorEditor = null!;

    /// <summary>
    /// Behavior editor for the selected creature or AreaTrigger, or its empty hint when nothing is
    /// selected.
    /// </summary>
    void OpenBehaviorEditor()
    {
        if (areaTriggers.SelectedEntry is uint trigger)
        {
            behaviorEditor.OpenAreaTrigger(trigger);
        }
        else if (creatures.SelectedSpawn is { } spawn)
        {
            behaviorEditor.Open(spawn.TemplateId, spawn.Guid);
        }
        else
        {
            behaviorEditor.Open(0, 0);
        }
    }

    /// <summary>
    /// Full-width flat strip holding the menu bar above the toolbar.
    /// </summary>
    Control BuildStrip(Theme theme)
    {
        var strip = new PanelContainer
        {
            Name = "CommandStrip",
            Theme = theme
        };
        strip.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.06f, 0.085f, 0.97f),
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 4,
            ContentMarginBottom = 6,
            BorderWidthBottom = 1,
            BorderColor = new Color(1, 1, 1, 0.08f)
        });
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 4);
        strip.AddChild(rows);
        menu = new EditorMenu();
        rows.AddChild(menu);
        toolbar = new EditorToolbar();
        rows.AddChild(toolbar);
        return strip;
    }

    bool HasSelection => creatures.SelectedGuid.HasValue || gameObjects.SelectedGuid.HasValue || graveyards.HasSelection || areaTriggers.HasSelection;

    void FlyToSelection()
    {
        if (areaTriggers.HasSelection)
        {
            areaTriggers.FlyToSelection();
        }
        else if (graveyards.HasSelection)
        {
            graveyards.FlyToSelection();
        }
        else if (creatures.SelectedGuid.HasValue)
        {
            creatures.FlyToSelection();
        }
        else
        {
            gameObjects.FlyToSelection();
        }
    }

    /// <summary>
    /// Tool mode applies to both draft editors; graveyards map Rotate to hidden handles.
    /// </summary>
    void SetMode(GizmoMode mode)
    {
        creatures.Mode = mode;
        graveyards.Mode = mode;
    }

    /// <summary>
    /// File/Edit/View/Help commands. Placement commands act on the selected creature's local draft;
    /// GameObjects stay read-only. Layer and SQL visibility controls remain only in the Scene
    /// explorer so there is one source of truth.
    /// </summary>
    void BuildMenus()
    {
        const Key ctrl = (Key)KeyModifierMask.MaskCtrl;
        const Key shift = (Key)KeyModifierMask.MaskShift;
        var file = menu.Menu("File");
        menu.Command(file, "Load archives…", ChooseArchives, () => !busy);
        menu.Command(file, "Open selected world", async () => await OpenMap(), () => !busy && store != null && maps.Selected >= 0);
        menu.Command(file, "Database connection…", () =>
        {
            sidebar.Visible = true;
            sidebar.ShowConnection();
        });
        file.AddSeparator();
        menu.Command(file, "Quit", () => GetTree().Quit(), accelerator: ctrl | Key.Q);

        var edit = menu.Menu("Edit");
        menu.Command(edit, "Undo", () => ActiveEditor.Undo(), () => ActiveEditor.CanUndo && !ActiveEditor.IsDragging, ctrl | Key.Z);
        menu.Command(edit, "Redo", () => ActiveEditor.Redo(), () => ActiveEditor.CanRedo && !ActiveEditor.IsDragging, ctrl | Key.Y);
        edit.AddSeparator();
        menu.Command(edit, "Drop to ground", () => ActiveEditor.DropToGround(), () => ActiveEditor.CanEdit, Key.End);
        menu.Command(edit, "Reset to source", () => ActiveEditor.ResetToSource(), () => ActiveEditor.CanEdit);
        menu.Command(edit, "Copy .go command", () => ActiveEditor.CopyGoCommand(), () => ActiveEditor.HasSelection, ctrl | shift | Key.C);

        var view = menu.Menu("View");
        menu.Command(view, "Fly to selection", FlyToSelection, () => HasSelection, Key.F);
        menu.Command(view, "Quest editor", () => questEditor.Open(0));
        menu.Command(view, "Behavior editor (selected creature)", OpenBehaviorEditor);
        menu.Command(view, "Scene explorer", () =>
        {
            sidebar.Visible = true;
            sidebar.ShowSceneTab();
        });
        view.AddSeparator();
        menu.Check(view, "Terrain cell borders", () =>
        {
            showCellBorders = !showCellBorders;
            renderer.SetGrid(showCellBorders);
        }, () => showCellBorders);
        menu.Check(view, "Navigation panel", () => leftPanel.Visible = !leftPanel.Visible, () => leftPanel.Visible);
        menu.Check(view, "Scene explorer panel", () => sidebar.Visible = !sidebar.Visible, () => sidebar.Visible);

        var help = menu.Menu("Help");
        menu.Command(help, "Controls…", () => ShowInfo("Controls", ControlsText));
        menu.Command(help, "About", () => ShowInfo("About AC World Edit", "AC World Edit · world viewer and local placement drafts for AzerothCore (Wrath 3.3.5a).\n\nClient archives and the world database are read-only. Moves save to a local draft file; nothing is written to the server."));
    }

    const string ControlsText = """
        Camera
          Right mouse (hold)    look / fly
          W A S D               move;  Q / E  down / up
          Shift                 faster;  Wheel  flight speed
          Esc                   release mouse

        Editing (selected creature)
          Q / W / E             Select / Move / Rotate tool
          Drag colored axis     move along that axis
          Drag yellow ring      rotate heading (hold Ctrl for 15° steps)
          End                   drop to ground
          Ctrl+Z / Ctrl+Y       undo / redo (also Ctrl+Shift+Z)
          F                     fly to selection
          Right-click (no drag) a creature to see its gossip, quests, vendor and trainer
          Esc during a drag     cancel
        """;

    /// <summary>
    /// Toolbar mirrors the creature tool state each frame. Mode keys are blocked while the camera is
    /// captured so Q/W/E keep flying the camera (see _Input).
    /// </summary>
    void BuildToolbar()
    {
        var modes = new ButtonGroup();
        toolbar.Mode("Select", "Pick only; hide handles", modes, () => SetMode(GizmoMode.Select), () => creatures.Mode == GizmoMode.Select, EditorToolbar.Shortcut(Key.Q));
        toolbar.Mode("Move", "Drag colored axes to move", modes, () => SetMode(GizmoMode.Move), () => creatures.Mode == GizmoMode.Move, EditorToolbar.Shortcut(Key.W));
        toolbar.Mode("Rotate", "Drag the yellow ring to change heading (creatures only)", modes, () => SetMode(GizmoMode.Rotate), () => creatures.Mode == GizmoMode.Rotate, EditorToolbar.Shortcut(Key.E), () => ActiveEditor.SupportsRotation);
        toolbar.Separator();
        toolbar.Action("Undo", "Undo the last local change for the selected kind (Ctrl+Z)", () => ActiveEditor.Undo(), () => ActiveEditor.CanUndo && !ActiveEditor.IsDragging);
        toolbar.Action("Redo", "Redo (Ctrl+Y)", () => ActiveEditor.Redo(), () => ActiveEditor.CanRedo && !ActiveEditor.IsDragging, EditorToolbar.Shortcut(Key.Z, ctrl: true, shift: true));
        toolbar.Separator();
        toolbar.Action("Drop to ground", "Snap the selection to terrain or a building floor below it (End)", () => ActiveEditor.DropToGround(), () => ActiveEditor.CanEdit);
        toolbar.Toggle("Snap on drop", "Snap to the ground when you release a move drag", value =>
        {
            creatures.SnapOnDrop = value;
            graveyards.SnapOnDrop = value;
        }, () => creatures.SnapOnDrop);
        toolbar.AddChild(new Label { Text = "Rotation step", Modulate = new Color("aabac8") });
        var step = new OptionButton
        {
            TooltipText = "Fixed heading increments while rotating",
            FocusMode = Control.FocusModeEnum.None
        };
        float[] steps = [0, 5, 15, 45];
        foreach (float degrees in steps)
        {
            step.AddItem(degrees == 0 ? "Free" : $"{degrees:0}°");
        }

        step.ItemSelected += index => creatures.RotationStep = Mathf.DegToRad(steps[index]);
        toolbar.AddChild(step);
        toolbar.Separator();
        toolbar.Action("Fly to selection", "Move the camera to the selection (F)", FlyToSelection, () => HasSelection);
        toolbar.Separator();
        toolbar.Action("Quests", "Open the quest editor", () => questEditor.Open(0));
        toolbar.Action("Behavior", "Edit the selected creature's SmartAI rules and speech", OpenBehaviorEditor);
    }

    /// <summary>
    /// Pick a client data directory; loading reuses the normal archive lifecycle and cancellation.
    /// </summary>
    void ChooseArchives()
    {
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Choose the client Data directory (MPQs)",
            CurrentDir = dataDirectory,
            UseNativeDialog = true
        };
        dialog.DirSelected += async directory =>
        {
            dataDirectory = directory;
            dialog.QueueFree();
            await LoadArchives();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(760, 520));
    }

    void ShowInfo(string title, string text)
    {
        var dialog = new AcceptDialog
        {
            Title = title,
            DialogText = text
        };
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
    }
}
