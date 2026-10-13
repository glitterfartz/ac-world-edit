using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Opt-in --interaction-smoke (graphical): Innkeeper Farley in Goldshire through real input and
/// controls. A right-drag must only turn the camera; a short right-click opens the window; gossip,
/// vendor (with client icons), submenu, quests, quest editor and Edit mode are exercised on an
/// isolated draft; a map switch closes the window. Saves artifacts/interaction-*.png.
/// </summary>
public partial class TerrainViewer
{
    const uint SmokeInnkeeper = 295;
    bool interactionSmoke;
    int interactionStep, interactionWait;
    double interactionTime, interactionStepTime;
    float smokeYaw;
    uint smokeRootMenu, smokeQuest;
    List<(string Name, Func<bool> Step)>? interactionSteps;

    void InteractionSmoke(double delta)
    {
        interactionTime += delta;
        interactionStepTime += delta;
        interactionSteps ??= InteractionSteps();
        TakeScreenshot();
        if (shot != null)
        {
            return;
        }

        // Fail fast with the stuck step's name rather than waiting on a long global timeout.
        if (interactionStepTime > 60 || interactionTime > 300)
        {
            GD.PushError($"INTERACTION_SMOKE_TIMEOUT step={interactionSteps[Math.Min(interactionStep, interactionSteps.Count - 1)].Name} status={interaction.SmokeStatus} questEditor='{questEditor.SmokeStatus}' visible={questEditor.Visible} quest={questEditor.QuestId} creatures={creatures.Records.Count} loading={creatures.IsLoading}");
            GetTree().Quit(1);
            return;
        }

        if (interactionWait > 0)
        {
            interactionWait--;
            return;
        }

        if (busy || world == null)
        {
            return;
        }

        if (interactionStep >= interactionSteps.Count)
        {
            GD.Print("INTERACTION_SMOKE_PASSED");
            GetTree().Quit();
            return;
        }

        var (name, step) = interactionSteps[interactionStep];
        if ((int)(interactionStepTime / 5) != (int)((interactionStepTime - delta) / 5))
        {
            GD.Print($"INTERACTION_SMOKE_WAITING {name} {interactionStepTime:F0}s status='{interaction.SmokeStatus}' questEditor='{questEditor.SmokeStatus}' quest={questEditor.QuestId}/{smokeQuest}");
        }

        try
        {
            if (step())
            {
                GD.Print("INTERACTION_SMOKE_STEP " + name);
                interactionStep++;
                interactionStepTime = 0;
                interactionWait = 3;
            }
        }
        catch (Exception error)
        {
            GD.PushError($"INTERACTION_SMOKE_FAILED {name}: {error.Message} status={interaction.SmokeStatus}");
            GetTree().Quit(1);
        }
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
            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://artifacts/interaction-{pending.Name}.png"));
        }
    }

    Foundation.CreatureSpawn? Farley => creatures.Records.FirstOrDefault(s => s.TemplateId == SmokeInnkeeper);

    void RightClick(Vector2 at, Vector2 drag)
    {
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = at, GlobalPosition = at });
        if (drag != Vector2.Zero)
        {
            Input.ParseInputEvent(new InputEventMouseMotion { Relative = drag, Position = at, GlobalPosition = at });
        }

        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = at, GlobalPosition = at });
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

            FocusCreature(spawn);
            interactionWait = 30;
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
            Require(!interaction.IsOpen && Math.Abs(yaw - smokeYaw) > 0.05f, "right-drag opened the window or did not turn the camera");
            FocusCreature(Farley!);
            interactionWait = 10;
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
            Require(interaction.IsOpen && interaction.Entry == SmokeInnkeeper && creatures.SelectedGuid == Farley!.Guid, "short right-click did not open Farley's window");
            if (!interaction.SmokeLoaded)
            {
                return false;
            }

            var v = interaction.View!;
            Require(v.Menus.Count > 0 && v.Menus[v.GossipMenuId].Options.Length > 0, "no gossip options");
            smokeRootMenu = v.GossipMenuId;
            GD.Print($"INTERACTION_SMOKE_VIEW '{v.Name}' menus={v.Menus.Count} options={v.Menus.Values.Sum(m => m.Options.Length)} vendor={v.Vendor.Length} starts={v.Starts.Length} ends={v.Ends.Length}");
            Screenshot("gossip");
            return true;
        }),
        ("vendor-page", () =>
        {
            interaction.SmokeShow("Gossip");
            Require(interaction.SmokeFollow(3), "no vendor option to follow");
            Require(interaction.SmokeOnVendorPage, "vendor option did not open the vendor page");
            return true;
        }),
        ("vendor-icons", () =>
        {
            if (interaction.SmokeIconsShown == 0)
            {
                return false;
            }

            Screenshot("vendor");
            return true;
        }),
        ("submenu-and-back", () =>
        {
            interaction.SmokeShow("Gossip");
            if (interaction.SmokeFollow(null))
            {
                Require(interaction.SmokeCurrentMenu != smokeRootMenu, "submenu did not open");
                Require(interaction.SmokePress("◂ Back") && interaction.SmokeCurrentMenu == smokeRootMenu, "back did not return to the root menu");
            }
            else
            {
                GD.Print("INTERACTION_SMOKE_NOTE root menu has no submenu option");
            }

            return true;
        }),
        ("quest-detail", () =>
        {
            interaction.SmokeShow("Quests");
            var quest = interaction.View!.Starts.Concat(interaction.View.Ends).First(q => q.Info != null);
            // Full button text: a title prefix can match another quest listed earlier on the page.
            Require(interaction.SmokePress($"{quest.Info!.Title} ({quest.Info.Level})"), "quest button missing");
            Require(interaction.SmokeOnQuestDetail, "quest detail did not open");
            smokeQuest = interaction.SmokeQuestDetail;
            Screenshot("quest");
            return true;
        }),
        ("open-quest-editor", () =>
        {
            Require(interaction.Size.X <= 480, "preview window grew wider than its client size: " + interaction.SmokeWidest());
            return interaction.SmokePress("Open in Quest Editor");
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

            Require(questEditor.ResultCount <= Data.InteractionSource.MaxSearch && questEditor.SmokeEditingDisabled, "quest search unbounded or editing enabled");
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
            interaction.SmokeSetEditing(true);
            interaction.SmokeShow("Gossip");
            int before = interaction.View!.Menus[smokeRootMenu].Options.Length;
            if (interaction.View.Menus[smokeRootMenu].SharedBy > 1)
            {
                Require(interaction.SmokePress("Make a copy for this NPC") && interaction.View.GossipMenuId != smokeRootMenu, "fork did not repoint the NPC");
                GD.Print($"INTERACTION_SMOKE_FORK {smokeRootMenu} -> {interaction.View.GossipMenuId}");
                smokeRootMenu = interaction.View.GossipMenuId;
            }

            Require(interaction.SmokePress("+ Add option → new submenu"), "add submenu button missing");
            var menu = interaction.View!.Menus[smokeRootMenu];
            Require(menu.Options.Length == before + 1 && menu.Options[^1].ActionMenu != 0 && interaction.View.Menus.ContainsKey(menu.Options[^1].ActionMenu), "new option with submenu missing");
            Screenshot("edit-gossip");
            return true;
        }),
        ("edit-greeting", () =>
        {
            Require(interaction.SmokeSetGreeting("Smoke greeting, $N."), "greeting editor missing");
            var text = interaction.View!.Menus[smokeRootMenu].Texts[0];
            Require(text.Male == "Smoke greeting, $N." && !text.FromBroadcast && text.Edited, "greeting edit not applied");
            return true;
        }),
        ("edit-vendor-search", () =>
        {
            interaction.SmokeShow("Vendor");
            Require(interaction.SmokeSearch("item", "Linen Cloth"), "item search box missing");
            return true;
        }),
        ("edit-vendor-add", () =>
        {
            if (!interaction.SmokeStatus.Contains("result(s)", StringComparison.Ordinal))
            {
                return false;
            }

            Require(interaction.SmokePress("+ Linen Cloth (2589)"), "Linen Cloth result missing");
            Require(interaction.View!.Vendor.Any(i => i.Item == 2589 && i.Edited), "vendor item not added");
            Screenshot("edit-vendor");
            return true;
        }),
        ("edit-vendor-remove", () =>
        {
            Require(interaction.SmokeRemoveVendorItem(2589) && interaction.View!.Vendor.All(i => i.Item != 2589), "vendor item not removed");
            return true;
        }),
        ("edit-flag-warning-fix", () =>
        {
            interaction.SmokeShow("Info");
            Require(interaction.SmokeToggleFlag("Quest giver", false), "quest giver checkbox missing");
            Require(interaction.View!.Issues.Any(i => !i.Error && i.Message.StartsWith("Quests are linked", StringComparison.Ordinal)), "missing questgiver warning");
            Require(interaction.SmokePress("Fix") && (interaction.View.NpcFlag & NpcFlags.QuestGiver) != 0, "fix did not restore the flag");
            return true;
        }),
        ("edit-undo-redo-reopen", () =>
        {
            Require(interaction.SmokePress("Undo") && (interaction.View!.NpcFlag & NpcFlags.QuestGiver) == 0, "undo did not revert the fix");
            Require(interaction.SmokePress("Redo") && (interaction.View!.NpcFlag & NpcFlags.QuestGiver) != 0, "redo did not reapply the fix");
            interaction.SmokeReopenDraft();
            var menu = interaction.View!.Menus[smokeRootMenu];
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

            if (interactionStepTime < 4)
            {
                return false;
            }

            GD.Print($"INTERACTION_SMOKE_FPS editWindow={interaction.IsOpen && interaction.Editing} fps={Engine.GetFramesPerSecond()}");
            interaction.CloseWindow();
            return true;
        }),
        ("settled-frame-rate-closed", () =>
        {
            if (interactionStepTime < 4)
            {
                return false;
            }

            GD.Print($"INTERACTION_SMOKE_FPS windowClosed fps={Engine.GetFramesPerSecond()}");
            interaction.Open(Farley!);
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

            Require(!interaction.IsOpen, "window stayed open after a map switch");
            return true;
        })
    ];
}
