using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The floating windows and the content browser, and every signal that ties them to the overlays.
/// Windows are configured before they enter the tree, because their <c>_Ready</c> reads those hooks.
/// </summary>
public partial class ViewerShell
{
    CreaturePropertiesWindow creatureProps = null!;
    GameObjectPropertiesWindow gameObjectProps = null!;
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

    /// <summary>A template opened in the content browser: creatures and GameObjects open in their properties windows.</summary>
    void OpenTemplate(SpawnKind kind, uint entry, string name)
    {
        string label = string.IsNullOrEmpty(name) ? $"{kind} {entry}" : name;
        if (kind == SpawnKind.Creature)
        {
            creatureProps.OpenEntry(entry, label);
        }
        else
        {
            gameObjectProps.OpenEntry(entry, label);
        }
    }

    /// <summary>
    /// The behavior (SmartAI) editors open for both databases: creatures, client AreaTriggers and, for
    /// TrinityCore, server-spawned AreaTriggers.
    /// </summary>
    bool BehaviorEditors => databaseEnabled;

    /// <summary>
    /// Tell the user when another editor holds the draft's lock file and offer to delete a stale one.
    /// The draft itself is never touched; only the sibling .lock file is removed.
    /// </summary>
    void PromptLockedDraft()
    {
        if (!drafts.Locked)
        {
            return;
        }

        string lockPath = drafts.LockPath ?? ContentDraftFiles.LocalPath("interactions.json.lock");
        Dialogs.Confirm(this, "Draft is locked",
            $"Another ACWorldEdit may be running with this draft.\n\nClose the other editor, or delete its lock file and continue:\n{lockPath}",
            () =>
            {
                var (ok, message) = drafts.DeleteLockAndReopen();
                statusLog.Post(message);
                if (!ok)
                {
                    Dialogs.Info(this, "Still locked", message + "\nClose the other editor, then reopen this one.", "OK");
                }
            },
            ok: "Delete lock file and continue", cancel: "Keep editing disabled");
    }

    /// <summary>
    /// The draft editor that owns the current selection; creatures when nothing editable is
    /// selected. Menus, toolbar and viewport drags route through it.
    /// </summary>
    IPlacementEditor ActiveEditor => waypoints != null && waypoints.HasSelection ? waypoints : graveyards != null && graveyards.HasSelection ? graveyards : gameObjects != null && gameObjects.HasSelection ? gameObjects : creatures;
    /// <summary>
    /// The shared local content draft for creature, quest and behavior editing.
    /// </summary>
    ContentDrafts drafts = null!;
    QuestEditorWindow questEditor = null!;
    /// <summary>AreaTrigger rules have no creature, so they keep a window of their own.</summary>
    BehaviorEditorWindow areaBehavior = null!;

    /// <summary>Open the draft, build the editor windows and attach them to the UI layer in draw order.</summary>
    void BuildEditors(Node layer)
    {
        drafts = ContentDraftFiles.Open();
        if (drafts.Locked)
        {
            Callable.From(PromptLockedDraft).CallDeferred();
        }

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
            FlyToSpawn = (guid, map, x, y, z) => LocateSpawn(SpawnKind.Creature, guid, map, x, y, z),
            BehaviorForEntryRequested = entry => creatureProps.OpenEntry(entry, $"Creature {entry}", "behavior")
        };
        gameObjectProps = new GameObjectPropertiesWindow
        {
            Connection = () => DatabaseConnection(),
            Store = () => store,
            Lookup = () => clientLookup,
            Drafts = drafts,
            FlyToSpawn = (guid, map, x, y, z) => LocateSpawn(SpawnKind.GameObject, guid, map, x, y, z)
        };
        bookmarkWindow = new BookmarkWindow
        {
            Drafts = drafts,
            FilePath = () => BookmarkStore.PathFor(profile.Kind),
            Capture = CaptureBookmark,
            CanCapture = () => world != null && currentMapId != null,
            OpenBookmark = OpenBookmark
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
        layer.AddChild(gameObjectProps);
        layer.AddChild(bookmarkWindow);
        layer.AddChild(questEditor);
        layer.AddChild(areaBehavior);
        pendingWindow = new PendingChangesWindow
        {
            Drafts = drafts
        };
        sqlPreview = new SqlPreviewWindow();
        layer.AddChild(sqlPreview);
        layer.AddChild(pendingWindow);
        WirePendingWindow();
        WireEditors();
    }

    /// <summary>Pending-changes buttons and the cross-window summaries.</summary>
    void WirePendingWindow()
    {
        pendingWindow.DiscardRequested += change =>
        {
            DiscardPending(change);
            RefreshPending();
            pendingWindow.SetStatus("Discarded.");
        };
        pendingWindow.PreviewSqlRequested += PreviewPendingSql;
        pendingWindow.ExportAllRequested += ExportAllPending;
        pendingWindow.DiscardAllRequested += DiscardAllPending;
        sidebar.ReviewPendingRequested += OpenPendingChanges;
        creatureProps.SummaryChanged += creatures.SetInteractionSummary;
        creatureProps.QuestEditorRequested += questEditor.Open;
    }

    /// <summary>Selection signals that open an editor window.</summary>
    void WireEditors()
    {
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
        gameObjects.PropertiesRequested += spawn =>
        {
            if (databaseEnabled)
            {
                gameObjectProps.Open(spawn);
            }
        };
    }

    /// <summary>The template browser, its thumbnail renderer and the wiring that keeps it fitted.</summary>
    void BuildContentBrowser(Control ui)
    {
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
        creatureProps.Thumbnails = thumbnails;
        gameObjectProps.Thumbnails = thumbnails;
        questEditor.Thumbnails = thumbnails;
        contentBrowser.Activated += OpenTemplate;
        ui.AddChild(contentBrowser);
        foreach (var side in new Control[] { leftPanel, sidebar })
        {
            side.Resized += FitContentBrowser;
            side.VisibilityChanged += FitContentBrowser;
        }
    }

    /// <summary>
    /// Search creature templates by name or ID (the database of either dialect) and open the pick in the
    /// creature properties window, as a quest giver card does. The picker is the one the quest editor uses.
    /// </summary>
    void OpenCreatureTemplateSearch()
    {
        RecordPicker.Show(this, "Find creature template", term => DBSource.Open(DatabaseConnection(), clientLookup).SearchAsync(SmartParamKind.Creature, term, CancellationToken.None),
            (entry, name) => creatureProps.OpenEntry(entry, name), store: () => store);
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

    /// <summary>
    /// Move the camera to the selection, in the precedence the inspector uses for the toolbar button.
    /// </summary>
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
}
