using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// UI composition for the viewer. Streaming, archive ownership and camera movement remain in the main partial; controls never write source data.
/// </summary>
public partial class TerrainViewer
{
    PanelContainer leftPanel = null!;

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
        SetUiScale(UserSettings.LoadUiScale(), false);
        profile = ClientProfile.For(UserSettings.LoadProfile());
        wrathDirectory = UserSettings.LoadDataDirectory();
        retailDirectory = UserSettings.LoadRetailDirectory();
        databaseEnabled = profile.SupportsDatabase;
        connection.Dialect = profile.Dialect;
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
        clientLabel = new Label
        {
            Text = "WORLD VIEWER  /  " + profile.Title
        };
        clientLabel.AddThemeColorOverride("font_color", new Color("5bd8c2"));
        clientLabel.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(clientLabel);
        box.AddChild(new HSeparator());
        box.AddChild(new Label { Text = "World" });
        // Map names can be long; clip them instead of letting the button and its dropdown grow.
        maps = new OptionButton { ClipText = true, FitToLongestItem = false, CustomMinimumSize = new Vector2(220, 0), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
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
        BuildTeleportUi(box);
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
            Text = sky.Playing ? "⏸" : "▶",
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
        ui.AddChild(statusBar = new StatusBar(statusLog));
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
        sidebar = new SceneSidebar(creatures, gameObjects, graveyards, waypoints, areaTriggers, objects, connection);
        ui.AddChild(sidebar);
        drafts = ContentDraftFiles.Open();
        creatureProps = new CreaturePropertiesWindow
        {
            Connection = () => DatabaseConnection(),
            Store = () => store,
            Areas = () => areas,
            Drafts = drafts,
            FlyToEntry = LocateCreature,
            BehaviorForEntryRequested = entry => creatureProps.OpenEntry(entry, $"Creature {entry}", "behavior")
        };
        areaBehavior = new BehaviorEditorWindow
        {
            Connection = () => DatabaseConnection(),
            Store = () => store,
            Drafts = drafts
        };
        questEditor = new QuestEditorWindow
        {
            Connection = () => DatabaseConnection(),
            Store = () => store,
            Areas = () => areas,
            Drafts = drafts,
            FlyToEntry = LocateCreature,
            BehaviorRequested = entry => creatureProps.OpenEntry(entry, $"Creature {entry}", "behavior")
        };
        // Floating windows sit on the UI layer above the panels.
        layer.AddChild(creatureProps);
        layer.AddChild(questEditor);
        layer.AddChild(areaBehavior);
        pendingWindow = new PendingChangesWindow
        {
            Drafts = drafts
        };
        pendingWindow.DiscardRequested += change =>
        {
            DiscardPending(change);
            RefreshPending();
            pendingWindow.SetStatus("Discarded.");
        };
        sqlPreview = new SqlPreviewWindow();
        layer.AddChild(sqlPreview);
        pendingWindow.PreviewSqlRequested += PreviewPendingSql;
        pendingWindow.ExportAllRequested += ExportAllPending;
        pendingWindow.DiscardAllRequested += DiscardAllPending;
        layer.AddChild(pendingWindow);
        sidebar.ReviewPendingRequested += OpenPendingChanges;
        creatureProps.SummaryChanged += creatures.SetInteractionSummary;
        creatureProps.QuestEditorRequested += questEditor.Open;
        creatures.PropertiesRequested += spawn => creatureProps.Open(spawn);
        areaTriggers.BehaviorRequested += trigger => areaBehavior.OpenAreaTrigger(trigger.Entry);
        BuildMenus();
        BuildToolbar();
    }

    CreaturePropertiesWindow creatureProps = null!;
    /// <summary>
    /// The shared local content draft for creature, quest and behavior editing.
    /// </summary>
    ContentDrafts drafts = null!;
    QuestEditorWindow questEditor = null!;
    /// <summary>AreaTrigger rules have no creature, so they keep a window of their own.</summary>
    BehaviorEditorWindow areaBehavior = null!;

    /// <summary>
    /// Behavior of the selected AreaTrigger or creature; with nothing selected the creature window
    /// opens with its empty hint.
    /// </summary>
    void OpenBehaviorEditor()
    {
        if (areaTriggers.SelectedEntry is uint trigger)
        {
            areaBehavior.OpenAreaTrigger(trigger);
        }
        else if (creatures.SelectedSpawn is { } spawn)
        {
            creatureProps.Open(spawn, "behavior");
        }
        else
        {
            creatureProps.Open(null, "behavior");
        }
    }

    bool HasSelection => creatures.SelectedGuid.HasValue || gameObjects.SelectedGuid.HasValue || graveyards.HasSelection || waypoints.HasSelection || areaTriggers.HasSelection;

    void FlyToSelection()
    {
        if (areaTriggers.HasSelection)
        {
            areaTriggers.FlyToSelection();
        }
        else if (waypoints.HasSelection)
        {
            waypoints.FlyToSelection();
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
        waypoints.Mode = mode;
    }

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
            waypoints.SnapOnDrop = value;
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
        toolbar.Action("Quests", "Open the quest editor", () => questEditor.Open(0), () => databaseEnabled);
        toolbar.Action("Behavior", "Edit the selected creature's SmartAI rules and speech", OpenBehaviorEditor, () => databaseEnabled);
    }

    Label clientLabel = null!;

    /// <summary>
    /// Pick the client profile and folder; loading reuses the normal client lifecycle and
    /// cancellation. Each profile remembers its own folder, and the chosen profile is remembered too.
    /// </summary>
    void ChooseClient()
    {
        var dialog = new ClientPickerDialog(profile.Kind, kind => kind == ClientKind.Retail ? retailDirectory : wrathDirectory);
        dialog.Chosen += async (kind, directory) =>
        {
            if (kind != profile.Kind && !await ConfirmDiscardDrafts(kind))
            {
                return;
            }

            profile = ClientProfile.For(kind);
            dataDirectory = directory;
            UserSettings.SaveProfile(kind);
            connection.Dialect = profile.Dialect;
            if (kind == ClientKind.Retail)
            {
                UserSettings.SaveRetailDirectory(directory);
            }
            else
            {
                UserSettings.SaveDataDirectory(directory);
            }

            await LoadClient();
        };
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>
    /// Draft edits belong to the open client's database, so switching profile drops them. Ask first
    /// when there are any; false means the user kept them and the switch should not happen.
    /// </summary>
    async Task<bool> ConfirmDiscardDrafts(ClientKind target)
    {
        var changes = CollectPending();
        if (changes.Count == 0)
        {
            return true;
        }

        // The picker is still closing; a popup opened in the same frame is hidden with it.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var answer = new TaskCompletionSource<bool>();
        var ask = new ConfirmationDialog
        {
            Title = "Unsaved changes",
            OkButtonText = "Discard and switch",
            CancelButtonText = "Keep editing",
            DialogText = $"You have {changes.Count} unsaved change(s). Switching to the {(target == ClientKind.Retail ? "retail" : "Wrath 3.3.5")} client discards them. Discard and switch?"
        };
        ask.Confirmed += () => answer.TrySetResult(true);
        ask.Canceled += () => answer.TrySetResult(false);
        ask.Confirmed += ask.QueueFree;
        ask.Canceled += ask.QueueFree;
        AddChild(ask);
        ask.PopupCentered(new Vector2I(420, 0));
        if (!await answer.Task)
        {
            return false;
        }

        foreach (var change in changes)
        {
            DiscardPending(change);
        }

        RefreshPending();
        return true;
    }

    /// <summary>
    /// Reflect the opened client in the UI: the title line, whether database layers and editors are
    /// available, and the teleport list (which reads the database).
    /// </summary>
    void ApplyClientProfile(string description)
    {
        clientLabel.Text = "WORLD VIEWER  /  " + description.Split(" · ")[0];
        sidebar.SetDatabaseAvailable(databaseEnabled);
        _ = LoadTeleports();
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

    /// <summary>
    /// Interactive launches only: verify the saved database settings with a real read-only session and
    /// ask for working ones if they fail, then ask for the client Data folder if none is remembered.
    /// Smoke runs are non-interactive and skip both prompts.
    /// </summary>
    async Task StartupPrompts()
    {
        if (databaseEnabled && !anySmoke)
        {
            var saved = connection.Connection;
            string? error = await Task.Run(() => ACWorldEdit.Data.DatabaseDiagnostics.TestAsync(saved));
            if (error != null)
            {
                await PromptConnection();
            }
        }

        if (dataDirectory.Length == 0 && !IsQueuedForDeletion())
        {
            ChooseClient();
        }
    }

    /// <summary>
    /// Show the connection dialog and return when it closes. Settings are adopted only after they
    /// connect, so cancelling never replaces a working connection.
    /// </summary>
    Task PromptConnection()
    {
        var closed = new TaskCompletionSource();
        var dialog = new ConnectionDialog(connection.Connection);
        dialog.Connected += connection.Adopt;
        dialog.VisibilityChanged += () =>
        {
            if (!dialog.Visible)
            {
                closed.TrySetResult();
                dialog.QueueFree();
            }
        };
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(420, 0));
        // The first layout pass measures wrapping labels at zero width, and a window never shrinks
        // by itself, so refit it once the labels have their real width.
        Callable.From(() =>
        {
            dialog.ResetSize();
            dialog.MoveToCenter();
        }).CallDeferred();
        return closed.Task;
    }
}
