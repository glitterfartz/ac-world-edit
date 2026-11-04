using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// UI composition for the viewer. Streaming, archive ownership and camera movement remain in the main partial; controls never write source data.
/// </summary>
public partial class ViewerShell
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
        var form = new UiForm(box);
        form.Heading("AC WORLD EDIT");
        clientLabel = form.Caption("WORLD VIEWER  /  " + profile.Title, new Color("5bd8c2"));
        form.Add(new HSeparator());
        form.Add(InspectorUi.Text("World settings", 24));
        form.Add(new Label { Text = "Map" });
        // Map names can be long; clip them instead of letting the button and its dropdown grow.
        maps = form.Add(form.Options([], -1, id =>
        {
            if (id < mapList.Count)
            {
                var m = mapList[id];
                tileX.Value = m.Directory == "Azeroth" ? 34 : 32;
                tileY.Value = m.Directory == "Azeroth" ? 49 : 32;
            }
        }, clip: true, minWidth: 220));
        var tileRow = form.Row();
        tileRow.AddChild(new Label { Text = "Tile X" });
        tileX = form.Spin(0, 63, 34);
        tileRow.AddChild(tileX);
        tileRow.AddChild(new Label { Text = "Y" });
        tileY = form.Spin(0, 63, 49);
        tileRow.AddChild(tileY);
        open = form.Action("Open world", () => _ = OpenMap(), disabled: true);
        BuildTeleportUi(box);
        var radiusRow = form.Row();
        radiusRow.AddChild(new Label { Text = "Tile view distance" });
        distance = form.Spin(0, 3, 1, value =>
        {
            radius = (int)value;
            UpdateWanted();
        });
        radiusRow.AddChild(distance);
        form.Add(new HSeparator());
        form.Add(new Label { Text = "Time of day" });
        var timeRow = form.Row();
        playTime = form.Button(sky.Playing ? "⏸" : "▶", () =>
        {
            sky.Playing = !sky.Playing;
            playTime.Text = sky.Playing ? "⏸" : "▶";
        }, "Pause/resume the day-night cycle");
        playTime.CustomMinimumSize = new Vector2(32, 0);
        timeRow.AddChild(playTime);
        timeOfDay = form.Slider(0, 24, sky.TimeOfDayHours, 1.0 / 60, value => sky.SetTimeOfDay(value),
            "Scrub the time of day", 190);
        timeRow.AddChild(timeOfDay);
        timeOfDayLabel = new Label
        {
            CustomMinimumSize = new Vector2(48, 0)
        };
        timeRow.AddChild(timeOfDayLabel);
        var speedRow = form.Row();
        speedRow.AddChild(new Label { Text = "Cycle speed" });
        string[] speedLabels = ["0.5x", "1x", "2x", "5x", "20x"];
        float[] speedValues = [0.5f, 1f, 2f, 5f, 20f];
        var speed = form.Options(speedLabels, 1, id => sky.Speed = speedValues[id]);
        speedRow.AddChild(speed);
        form.Add(new HSeparator());
        status = form.Add(new Label
        {
            Text = message,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(294, 55)
        });
        ui.AddChild(statusBar = new StatusBar(statusLog));
        zoneLabel = form.Text("", 17, new Color("5bd8c2"));
        zoneLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        continentLabel = form.Text("", 12, new Color("aabac8"));
        minimap = form.Add(new TerrainMinimap
        {
            CustomMinimumSize = new Vector2(180, 180),
            MouseFilter = Control.MouseFilterEnum.Stop
        });
        minimap.Jump += key =>
        {
            tileX.Value = key.X;
            tileY.Value = key.Y;
            Teleport(key);
        };
        sidebar = new SceneSidebar(creatures, gameObjects, graveyards, waypoints, areaTriggers, objects, connection, navigation);
        ui.AddChild(sidebar);
        BuildSelectionBox(ui);
        drafts = ContentDraftFiles.Open();
        creatures.Attach(drafts);
        gameObjects.Attach(drafts);
        creatureProps = new CreaturePropertiesWindow
        {
            Connection = () => DatabaseConnection(),
            Store = () => store,
            Lookup = () => clientLookup,
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
            Lookup = () => clientLookup,
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
        contentBrowser = new ContentBrowser
        {
            Connection = () => DatabaseConnection(),
            Lookup = () => clientLookup,
            Report = statusLog.Post,
            Thumbnails = thumbnails = new ModelThumbnails
            {
                Models = () => store == null ? null : models,
                MainCamera = () => camera
            }
        };
        AddChild(thumbnails);
        contentBrowser.Activated += OpenTemplate;
        ui.AddChild(contentBrowser);
        foreach (var side in new Control[] { leftPanel, sidebar })
        {
            side.Resized += FitContentBrowser;
            side.VisibilityChanged += FitContentBrowser;
        }
        creatures.PropertiesRequested += spawn =>
        {
            if (databaseEnabled)
            {
                creatureProps.Open(spawn);
            }
        };
        areaTriggers.BehaviorRequested += trigger =>
        {
            if (BehaviorEditors)
            {
                areaBehavior.OpenAreaTrigger(trigger.Owner);
            }
        };
        BuildMenus();
        BuildToolbar();
    }

    CreaturePropertiesWindow creatureProps = null!;
    ContentBrowser contentBrowser = null!;
    /// <summary>Off-screen model thumbnails for the content browser; reset when another client loads.</summary>
    ModelThumbnails? thumbnails;
    readonly string smokeThumbnails = Guid.NewGuid().ToString("N");

    /// <summary>
    /// The thumbnail cache of a client kind. Smoke runs get a fresh folder under artifacts, as their drafts
    /// do, so a run renders every thumbnail instead of reading an earlier run's PNGs.
    /// </summary>
    string ThumbnailDirectory(ClientKind kind) =>
        ProjectSettings.GlobalizePath(anySmoke ? $"res://artifacts/thumbs/{smokeThumbnails}" : $"user://thumbs/{kind.ToString().ToLowerInvariant()}");

    void ToggleContentBrowser()
    {
        if (contentBrowser.Visible)
        {
            contentBrowser.Close();
        }
        else if (!databaseEnabled)
        {
            statusLog.Post("The content browser needs a world database connection.");
        }
        else
        {
            FitContentBrowser();
            contentBrowser.Open();
        }
    }

    /// <summary>
    /// Keep the content browser between the navigation panel and the Scene explorer, which size to
    /// their content; it takes the freed width when either is hidden.
    /// </summary>
    void FitContentBrowser()
    {
        const float gap = 12, margin = 20;
        contentBrowser.OffsetLeft = leftPanel.Visible ? leftPanel.Position.X + leftPanel.Size.X + gap : margin;
        contentBrowser.OffsetRight = sidebar.Visible ? sidebar.OffsetLeft - gap : -margin;
    }

    /// <summary>
    /// A template opened in the content browser: creatures open in the properties window; game objects
    /// have no window yet, so they only name themselves on the status bar.
    /// </summary>
    void OpenTemplate(SpawnKind kind, uint entry, string name)
    {
        string label = string.IsNullOrEmpty(name) ? $"{kind} {entry}" : name;
        if (kind == SpawnKind.Creature)
        {
            creatureProps.OpenEntry(entry, label);
        }
        else
        {
            statusLog.Post($"Game object {entry}: {label}. Game objects have no properties window yet.");
        }
    }

    /// <summary>
    /// The behavior (SmartAI) editors open for both databases: creatures, client AreaTriggers and, for
    /// TrinityCore, server-spawned AreaTriggers.
    /// </summary>
    bool BehaviorEditors => databaseEnabled;
    /// <summary>
    /// The shared local content draft for creature, quest and behavior editing.
    /// </summary>
    ContentDrafts drafts = null!;
    QuestEditorWindow questEditor = null!;
    /// <summary>AreaTrigger rules have no creature, so they keep a window of their own.</summary>
    BehaviorEditorWindow areaBehavior = null!;

    /// <summary>
    /// Search creature templates by name or ID (the database of either dialect) and open the pick in the
    /// creature properties window, as a quest giver card does. The picker is the one the quest editor uses.
    /// </summary>
    void OpenCreatureTemplateSearch()
    {
        PickerPopup.Show(this, "Find creature template", term => DBSource.Open(DatabaseConnection(), clientLookup).SearchAsync(SmartParamKind.Creature, term, CancellationToken.None),
            (entry, name) => creatureProps.OpenEntry(entry, name));
    }

    /// <summary>
    /// Behavior of the selected AreaTrigger or creature; with nothing selected the creature window
    /// opens with its empty hint.
    /// </summary>
    void OpenBehaviorEditor()
    {
        if (areaTriggers.Selected is { } trigger)
        {
            if (BehaviorEditors)
            {
                areaBehavior.OpenAreaTrigger(trigger.Owner);
            }
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
        gameObjects.Mode = mode;
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
        toolbar.Mode("Rotate", "Drag the yellow ring to change heading (not for graveyards or waypoints)", modes, () => SetMode(GizmoMode.Rotate), () => creatures.Mode == GizmoMode.Rotate, EditorToolbar.Shortcut(Key.E), () => ActiveEditor.SupportsRotation);
        toolbar.Separator();
        toolbar.Action("Undo", "Undo the last local change for the selected kind (Ctrl+Z)", () => ActiveEditor.Undo(), () => ActiveEditor.CanUndo && !ActiveEditor.IsDragging);
        toolbar.Action("Redo", "Redo (Ctrl+Y)", () => ActiveEditor.Redo(), () => ActiveEditor.CanRedo && !ActiveEditor.IsDragging, EditorToolbar.Shortcut(Key.Z, ctrl: true, shift: true));
        toolbar.Separator();
        toolbar.Action("Duplicate", "Copy the selected spawn two yards ahead of it (Ctrl+D)", () => ActiveEditor.Duplicate(), () => ActiveEditor.CanDuplicate && !ActiveEditor.IsDragging);
        toolbar.Action("Delete", "Delete the selected spawn (Del)", () => ActiveEditor.Delete(), () => ActiveEditor.CanDelete && !ActiveEditor.IsDragging);
        toolbar.Separator();
        toolbar.Action("Drop to ground", "Snap the selection to terrain or a building floor below it (End)", () => ActiveEditor.DropToGround(), () => ActiveEditor.CanEdit);
        toolbar.Toggle("Snap on drop", "Snap to the ground when you release a move drag", value =>
        {
            creatures.SnapOnDrop = value;
            gameObjects.SnapOnDrop = value;
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

        step.ItemSelected += index =>
        {
            creatures.RotationStep = Mathf.DegToRad(steps[index]);
            gameObjects.RotationStep = Mathf.DegToRad(steps[index]);
        };
        toolbar.AddChild(step);
        toolbar.Separator();
        toolbar.Action("Fly to selection", "Move the camera to the selection (F)", FlyToSelection, () => HasSelection);
        toolbar.Separator();
        toolbar.Action("Quests", "Open the quest editor", () => questEditor.Open(0), () => databaseEnabled);
        toolbar.Action("Behavior", "Edit the selected creature's SmartAI rules and speech", OpenBehaviorEditor, () => BehaviorEditors);
        toolbar.Action("Creature Templates", "Search the database for a creature template by name or ID", OpenCreatureTemplateSearch, () => databaseEnabled);
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
            if (kind != profile.Kind)
            {
                SwitchDraftProfile(kind);
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

            WarnMissingDependencies(kind);
            await LoadClient();
        };
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>
    /// Say in the status bar what a profile needs that this install lacks (CascLib.dll, retail table
    /// definitions), before the client load fails on it.
    /// </summary>
    void WarnMissingDependencies(ClientKind kind)
    {
        foreach (string problem in DependencyCheck.Missing(kind, System.Environment.GetEnvironmentVariable("ACWORLD_CASCLIB"), System.Environment.GetEnvironmentVariable("ACWORLD_DBD_DIR"), AppContext.BaseDirectory))
        {
            statusLog.Post($"Retail unavailable: {problem}");
        }
    }

    /// <summary>
    /// Each client profile keeps its own draft files, so switching profile closes this profile's
    /// drafts (they stay on disk) and opens the other's.
    /// </summary>
    void SwitchDraftProfile(ClientKind kind)
    {
        ContentDraftFiles.UseProfile(kind);
        ContentDraftFiles.Rebind(drafts);
        graveyards.ReopenDraft();
        waypoints.ReopenDraft();
        pendingStore = null!;
        RefreshPending();
    }

    /// <summary>
    /// Reflect the opened client in the UI: the title line, whether database layers and editors are
    /// available, and the teleport list (which reads the database).
    /// </summary>
    void ApplyClientProfile(string description)
    {
        clientLabel.Text = "WORLD VIEWER  /  " + description.Split(" · ")[0];
        sidebar.SetDatabaseAvailable(databaseEnabled);
        sidebar.SetDialect(profile.Dialect);
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
