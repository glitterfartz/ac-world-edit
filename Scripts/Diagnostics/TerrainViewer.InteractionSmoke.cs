using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Opt-in --interaction-smoke: Innkeeper Farley in Goldshire through real input and
/// controls. A right-drag must only turn the camera; a short right-click opens the window; gossip,
/// vendor (with client icons), submenu, quests, quest editor and editing are exercised on an
/// isolated draft; a map switch closes the window. Saves artifacts/interaction-*.png.
/// </summary>
public partial class TerrainViewer
{
    const uint SmokeInnkeeper = 295;
    bool interactionSmoke;
    readonly SmokeSteps interactionRun = new("INTERACTION", 60, 300, 3);
    float smokeYaw;
    uint smokeRootMenu, smokeQuest;
    List<(string Name, Func<bool> Step)>? interactionSteps;

    void InteractionSmoke(double delta)
    {
        interactionSteps ??= InteractionSteps();
        TakeScreenshot();
        if (shot != null)
        {
            return;
        }

        // Fail fast with the stuck step's name rather than waiting on a long global timeout.
        interactionRun.Advance(delta, interactionSteps, !busy && world != null,
            () => $"status={creatureProps.SmokeStatus}", SmokeFail, () => GetTree().Quit(),
            timeoutWhere: () => $"status={creatureProps.SmokeStatus} questEditor='{questEditor.SmokeStatus}' visible={questEditor.Visible} quest={questEditor.QuestId} creatures={creatures.Records.Count} loading={creatures.IsLoading}",
            waiting: () => $"status='{creatureProps.SmokeStatus}' questEditor='{questEditor.SmokeStatus}' quest={questEditor.QuestId}/{smokeQuest}");
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    (string Name, int Frames)? shot;
    /// <summary>
    /// Screenshot file prefix: artifacts/{prefix}-{name}.png.
    /// </summary>
    string shotPrefix = "interaction";

    /// <summary>
    /// Capture a few frames later: the viewport texture holds the previously drawn frame, so an
    /// immediate capture would miss UI built this frame. Steps pause while a capture is pending.
    /// </summary>
    void Screenshot(string name) => shot = (name, 3);

    void TakeScreenshot()
    {
        if (shot is not { } pending)
        {
            return;
        }

        if (pending.Frames > 0)
        {
            shot = pending with { Frames = pending.Frames - 1 };
            return;
        }

        shot = null;
        if (DisplayServer.GetName() != "headless")
        {
            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://artifacts/{shotPrefix}-{pending.Name}.png"));
        }
    }

    Foundation.CreatureSpawn? Farley => creatures.Records.FirstOrDefault(s => s.TemplateId == SmokeInnkeeper);

    void RightClick(Vector2 at, Vector2 drag)
    {
        // Injected events are in window coordinates; the viewport may be scaled (headless runs at 40x).
        var window = GetViewport().GetFinalTransform() * at;
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = window, GlobalPosition = window });
        if (drag != Vector2.Zero)
        {
            Input.ParseInputEvent(new InputEventMouseMotion { Relative = drag, Position = window, GlobalPosition = window });
        }

        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = window, GlobalPosition = window });
    }

    List<(string, Func<bool>)> InteractionSteps() =>
    [
        ("goldshire", () =>
        {
            // Goldshire is tile 31,49; the default start (34,49) is too far for its creature region.
            if (currentMapId != 0)
            {
                return false;
            }

            tileX.Value = 31;
            tileY.Value = 49;
            Teleport(new TileKey(31, 49));
            // The creature query only reaches the view distance (+64 yd) from the camera, and the
            // tile center is ~280 yd from the inn; stand in Goldshire itself.
            FocusPose(new WorldPose(0, -9460, 30, 58, 0));
            return true;
        }),
        ("farley-loaded", () =>
        {
            if (loaded.Count != wanted.Count || creatures.IsLoading || Farley is not { } spawn)
            {
                return false;
            }

            FocusPose(spawn.Pose);
            interactionRun.Wait = 30;
            return true;
        }),
        ("right-drag-only-looks", () =>
        {
            if (creatures.IsLoading)
            {
                return false;
            }

            smokeYaw = yaw;
            RightClick(GetViewport().GetVisibleRect().Size / 2, new Vector2(60, 0));
            return true;
        }),
        ("right-drag-checked", () =>
        {
            Require(!creatureProps.IsOpen && Math.Abs(yaw - smokeYaw) > 0.05f, "right-drag opened the window or did not turn the camera");
            FocusPose(Farley!.Pose);
            interactionRun.Wait = 10;
            return true;
        }),
        ("short-right-click", () =>
        {
            var at = camera.UnprojectPosition(creatures.PositionOf(Farley!.Guid) + Vector3.Up);
            RightClick(at, Vector2.Zero);
            return true;
        }),
        ("window-open", () =>
        {
            Require(creatureProps.IsOpen && creatureProps.Entry == SmokeInnkeeper && creatures.SelectedGuid == Farley!.Guid, "short right-click did not open Farley's window");
            if (!creatureProps.SmokeLoaded)
            {
                return false;
            }

            var v = creatureProps.View!;
            Require(v.Menus.Count > 0 && v.Menus[v.GossipMenuId].Options.Length > 0, "no gossip options");
            smokeRootMenu = v.GossipMenuId;
            GD.Print($"INTERACTION_SMOKE_VIEW '{v.Name}' menus={v.Menus.Count} options={v.Menus.Values.Sum(m => m.Options.Length)} vendor={v.Vendor.Length} starts={v.Starts.Length} ends={v.Ends.Length}");
            Screenshot("gossip");
            return true;
        }),
        ("vendor-page", () =>
        {
            creatureProps.SmokeShow("Vendor");
            Require(creatureProps.SmokeOnVendorPage, "the Vendor section did not open");
            return true;
        }),
        ("vendor-icons", () =>
        {
            if (creatureProps.SmokeIconsShown == 0)
            {
                return false;
            }

            Screenshot("vendor");
            return true;
        }),
        ("submenu-and-back", () =>
        {
            creatureProps.SmokeShow("Gossip");
            if (creatureProps.SmokeFollow(null))
            {
                Require(creatureProps.SmokeCurrentMenu != smokeRootMenu, "submenu did not open");
                Require(creatureProps.SmokePress("◂ Back") && creatureProps.SmokeCurrentMenu == smokeRootMenu, "back did not return to the root menu");
            }
            else
            {
                GD.Print("INTERACTION_SMOKE_NOTE root menu has no submenu option");
            }

            return true;
        }),
        ("quest-detail", () =>
        {
            creatureProps.SmokeShow("Quests");
            var quest = creatureProps.View!.Starts.Concat(creatureProps.View.Ends).First(q => q.Info != null);
            // Full button text: a title prefix can match another quest listed earlier on the page.
            Require(creatureProps.SmokePress($"{quest.Info!.Title} ({quest.Info.Level})"), "quest button missing");
            Require(creatureProps.SmokeOnQuestDetail, "quest detail did not open");
            smokeQuest = creatureProps.SmokeQuestDetail;
            return true;
        }),
        ("quest-detail-loaded", () =>
        {
            // The embedded quest editor loads its rows on a worker before it has anything to show.
            var panel = creatureProps.Context.Quest;
            if (panel.IsLoading || panel.QuestId != smokeQuest || !panel.HasQuest)
            {
                return false;
            }

            Screenshot("quest");
            return true;
        }),
        ("behavior-section", () =>
        {
            creatureProps.SmokeShow("Behavior");
            var panel = creatureProps.Context.Behavior;
            if (panel.IsLoading || panel.OwnerId != SmokeInnkeeper || panel.Scope == null)
            {
                return false;
            }

            Screenshot("behavior");
            return true;
        }),
        ("speech-section", () =>
        {
            creatureProps.SmokeShow("Speech");
            Require(creatureProps.Context.Behavior.Scope == null, "speech section did not show the speech page");
            Screenshot("speech");
            creatureProps.ShowSection("quests", smokeQuest);
            return true;
        }),
        ("open-quest-editor", () =>
        {
            return creatureProps.SmokePress("Open in Quest window");
        }),
        ("quest-editor-from-quest", () => questEditor.Visible && questEditor.QuestId == smokeQuest),
        ("quest-editor-from-toolbar", () =>
        {
            questEditor.Visible = false;
            var button = toolbar.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == "Quests");
            Require(button != null, "toolbar Quests button missing");
            button!.EmitSignal(BaseButton.SignalName.Pressed);
            Require(questEditor.Visible && questEditor.QuestId == null, "toolbar did not open an empty quest editor");
            questEditor.SmokeSearch("Kobold");
            return true;
        }),
        ("quest-editor-search", () =>
        {
            if (questEditor.ResultCount == 0)
            {
                return false;
            }

            Require(questEditor.ResultCount <= Data.DbLimits.MaxSearch && !questEditor.SmokeEditingEnabled, "quest search unbounded or a quest opened unexpectedly");
            Screenshot("quest-editor");
            return true;
        }),
        ("close-quest-editor", () =>
        {
            questEditor.Visible = false;
            return true;
        }),
        ("edit-add-submenu", () =>
        {
            creatureProps.SmokeShow("Gossip");
            int before = creatureProps.View!.Menus[smokeRootMenu].Options.Length;
            if (creatureProps.View.Menus[smokeRootMenu].SharedBy > 1)
            {
                Require(creatureProps.SmokePress("Make a copy for this NPC") && creatureProps.View.GossipMenuId != smokeRootMenu, "fork did not repoint the NPC");
                GD.Print($"INTERACTION_SMOKE_FORK {smokeRootMenu} -> {creatureProps.View.GossipMenuId}");
                smokeRootMenu = creatureProps.View.GossipMenuId;
            }

            Require(creatureProps.SmokePress("+ Add option → new submenu"), "add submenu button missing");
            var menu = creatureProps.View!.Menus[smokeRootMenu];
            Require(menu.Options.Length == before + 1 && menu.Options[^1].ActionMenu != 0 && creatureProps.View.Menus.ContainsKey(menu.Options[^1].ActionMenu), "new option with submenu missing");
            Screenshot("edit-gossip");
            return true;
        }),
        ("edit-greeting", () =>
        {
            Require(creatureProps.SmokeSetGreeting("Smoke greeting, $N."), "greeting editor missing");
            var text = creatureProps.View!.Menus[smokeRootMenu].Texts[0];
            Require(text.Male == "Smoke greeting, $N." && !text.FromBroadcast && text.Edited, "greeting edit not applied");
            return true;
        }),
        ("edit-vendor-search", () =>
        {
            creatureProps.SmokeShow("Vendor");
            Require(creatureProps.SmokeSearch("item", "Linen Cloth"), "item search box missing");
            return true;
        }),
        ("edit-vendor-add", () =>
        {
            if (!creatureProps.SmokeStatus.Contains("item(s) found", StringComparison.Ordinal))
            {
                return false;
            }

            Require(creatureProps.SmokePress("+ Linen Cloth (2589)"), "Linen Cloth result missing");
            Require(creatureProps.View!.Vendor.Any(i => i.Item == 2589 && i.Edited), "vendor item not added");
            Screenshot("edit-vendor");
            return true;
        }),
        ("edit-vendor-remove", () =>
        {
            Require(creatureProps.SmokeRemoveVendorItem(2589) && creatureProps.View!.Vendor.All(i => i.Item != 2589), "vendor item not removed");
            return true;
        }),
        ("edit-flag-warning-fix", () =>
        {
            creatureProps.SmokeShow("Template");
            Require(creatureProps.SmokeToggleFlag("Quest giver", false), "quest giver checkbox missing");
            Require(creatureProps.View!.Issues.Any(i => !i.Error && i.Message.StartsWith("Quests are linked", StringComparison.Ordinal)), "missing questgiver warning");
            Require(creatureProps.SmokePress("Fix") && (creatureProps.View.NpcFlag & AcNpcFlags.QuestGiver) != 0, "fix did not restore the flag");
            return true;
        }),
        ("edit-undo-redo-reopen", () =>
        {
            Require(creatureProps.SmokePress("Undo") && (creatureProps.View!.NpcFlag & AcNpcFlags.QuestGiver) == 0, "undo did not revert the fix");
            Require(creatureProps.SmokePress("Redo") && (creatureProps.View!.NpcFlag & AcNpcFlags.QuestGiver) != 0, "redo did not reapply the fix");
            creatureProps.SmokeReopenDraft();
            var menu = creatureProps.View!.Menus[smokeRootMenu];
            Require(menu.Options[^1].ActionMenu != 0 && menu.Texts[0].Male == "Smoke greeting, $N.", "edits lost after reopening the draft");
            Screenshot("edit-info");
            return true;
        }),
        ("settled-frame-rate", () =>
        {
            // Measure with the edit window open once model streaming has finished.
            if (models.Busy || creatures.IsLoading)
            {
                return false;
            }

            if (interactionRun.StepTime < 4)
            {
                return false;
            }

            GD.Print($"INTERACTION_SMOKE_FPS editWindow={creatureProps.IsOpen} fps={Engine.GetFramesPerSecond()}");
            creatureProps.CloseWindow();
            return true;
        }),
        ("settled-frame-rate-closed", () =>
        {
            if (interactionRun.StepTime < 4)
            {
                return false;
            }

            GD.Print($"INTERACTION_SMOKE_FPS windowClosed fps={Engine.GetFramesPerSecond()}");
            creatureProps.Open(Farley!, "gossip");
            return true;
        }),
        ("map-switch-closes", () =>
        {
            maps.Select(mapList.FindIndex(m => m.Directory == "Kalimdor"));
            tileX.Value = 32;
            tileY.Value = 32;
            _ = OpenMap();
            return true;
        }),
        ("closed", () =>
        {
            if (currentMapId != 1)
            {
                return false;
            }

            Require(!creatureProps.IsOpen, "window stayed open after a map switch");
            return true;
        })
    ];
}
