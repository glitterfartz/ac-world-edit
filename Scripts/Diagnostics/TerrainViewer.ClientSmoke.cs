using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Opt-in check that the selected client profile opens: it waits for the client services, the
/// default map's WDT and, for a terrain map, its first streamed tile, prints one summary line and
/// exits. It needs no database, so it runs headless: <c>--headless -- --client-smoke</c> with
/// ACWORLD_CLIENT_PROFILE and the profile's folder variable set. It proves loading, not visual output.
/// </summary>
public partial class TerrainViewer
{
    bool clientSmoke, clientShot, clientShotMoved, clientShotFramed, clientShotWater, clientShotOverlays, clientShotOverlaysFocused;
    // --client-shot-sky=HOUR[,YAW_DEGREES[,LIFT_YARDS[,PITCH_DEGREES]]]: set the time of day and look out from a raised
    // camera instead of framing a building (defaults: 0, 40 yards, 12 degrees up).
    (double Hour, float YawDegrees, float Lift, float PitchDegrees)? clientShotSky;
    // --client-shot-map=DIRECTORY (for example Expansion01): open that map before the shot.
    string? clientShotMap;
    int clientShotFrames, clientShotCreatureStage, clientShotDifficulty, sharedVendorFrames;
    uint clientShotQuest;
    TileKey? clientShotTile;
    double clientSmokeTime;

    /// <summary>
    /// World-space center of the vertices of the liquid mesh with the most vertices among the loaded
    /// tiles (--client-shot-water), or null when none is loaded. Averaging vertices rather than taking
    /// bounds keeps the target over the water when the batch spans the whole tile.
    /// </summary>
    Vector3? LargestLiquid()
    {
        Vector3? best = null;
        int bestCount = 0;
        foreach (var tile in loaded.Values)
        {
            if (tile.GetNodeOrNull<Node3D>("Liquids") is not { } container)
            {
                continue;
            }

            foreach (var mesh in container.GetChildren().OfType<MeshInstance3D>())
            {
                var vertices = mesh.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                if (vertices.Length > bestCount)
                {
                    bestCount = vertices.Length;
                    best = mesh.GlobalTransform * (vertices.Aggregate(Vector3.Zero, (a, v) => a + v) / vertices.Length);
                }
            }
        }

        return best;
    }

    /// <summary>Innkeeper Farley in Goldshire: a TrinityCore creature with a gossip menu and vendor items.</summary>
    const uint ShotCreature = 295;

    /// <summary>
    /// The last --client-shot-overlays step: open the creature window on Innkeeper Farley, check that
    /// it lists the selected dialect's pages and loaded his read model, and save the gossip, template, vendor, trainer and quest
    /// pages (artifacts/client-shot-creature-*.png).
    /// </summary>
    void ClientShotCreature()
    {
        switch (clientShotCreatureStage)
        {
            case 1:
                if (creatures.Records.FirstOrDefault(s => s.TemplateId == ShotCreature) is not { } farley)
                {
                    SmokeFail($"CLIENT_SHOT_CREATURE_MISSING no spawn of creature {ShotCreature} near Goldshire");
                    return;
                }

                // Select it in the overlay too, as a click does: the window follows the overlay's selection.
                creatures.Select(farley.Guid);
                creatureProps.Open(farley, "gossip");
                clientShotFrames = 0;
                clientShotCreatureStage = 2;
                return;
            case 2:
                if (creatureProps.Context.View == null)
                {
                    if (clientShotFrames++ > 600)
                    {
                        SmokeFail("CLIENT_SHOT_CREATURE_NOT_LOADED " + creatureProps.Context.Status);
                    }

                    return;
                }

                if (clientShotFrames++ < 60)
                {
                    return;
                }

                bool retail = creatureProps.Context.Dialect == Foundation.WorldDialect.TrinityCore;
                if ((creatureProps.Section("gossip") is TcGossipSection) != retail || creatureProps.Section("behavior") is null || creatureProps.Section("speech") is null)
                {
                    SmokeFail("CLIENT_SHOT_CREATURE_SECTIONS the window does not list the selected dialect's pages");
                    return;
                }

                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-creature-gossip.png"));
                if (creatureProps.Context.Editable)
                {
                    var openMenu = creatureProps.Section("gossip")!.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.StartsWith("Open menu "));
                    if (openMenu == null) { SmokeFail("CLIENT_SHOT_GOSSIP_SUBMENU_MISSING"); return; }
                    openMenu.EmitSignal(Button.SignalName.Pressed);
                    clientShotFrames = 0;
                    clientShotCreatureStage = 18;
                    return;
                }
                creatureProps.ShowSection("template", null);
                clientShotFrames = 0;
                clientShotCreatureStage = 3;
                return;
            case 18:
                if (clientShotFrames++ < 30) return;
                var gossipSection = creatureProps.Section("gossip")!;
                var menuPicker = gossipSection.FindChild("GossipMenuPicker", true, false) as OptionButton;
                var goBack = gossipSection.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == "Back");
                if (menuPicker == null || !menuPicker.GetItemText(menuPicker.Selected).Contains("submenu") || goBack == null || goBack.Disabled)
                { SmokeFail("CLIENT_SHOT_GOSSIP_NAVIGATION"); return; }
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-creature-submenu.png"));
                goBack.EmitSignal(Button.SignalName.Pressed);
                clientShotFrames = 0;
                clientShotCreatureStage = 19;
                return;
            case 19:
                if (clientShotFrames++ < 30) return;
                var rootPicker = creatureProps.Section("gossip")!.FindChild("GossipMenuPicker", true, false) as OptionButton;
                if (rootPicker == null || !rootPicker.GetItemText(rootPicker.Selected).Contains("root"))
                { SmokeFail("CLIENT_SHOT_GOSSIP_BACK"); return; }
                var addOption = creatureProps.Section("gossip")!.FindChild("AddGossipOption", true, false) as Button;
                if (addOption == null) { SmokeFail("CLIENT_SHOT_GOSSIP_ADD_MISSING"); return; }
                addOption.EmitSignal(Button.SignalName.Pressed);
                clientShotFrames = 0;
                clientShotCreatureStage = 20;
                return;
            case 20:
                if (clientShotFrames++ < 60) return;
                var gossipContext = creatureProps.Context;
                var gossipPage = gossipContext.Schema.GossipPage;
                var newOption = gossipContext.Effective.All(gossipPage.Options.Table).FirstOrDefault(r => r.T("OptionText") == "New option");
                if (newOption == null) { SmokeFail("CLIENT_SHOT_GOSSIP_ADD " + gossipContext.Status); return; }
                if (gossipContext.Commit("Invalid gossip code", Foundation.ItemListSpec.Change(gossipPage.Options.Table, newOption, newOption.With("BoxCoded", 2u))))
                { SmokeFail("CLIENT_SHOT_GOSSIP_VALIDATION"); return; }
                if (!gossipContext.Commit("Change option text", gossipPage.ChangeText(newOption, "Visual gossip check, $N.")))
                { SmokeFail("CLIENT_SHOT_GOSSIP_TEXT " + gossipContext.Status); return; }
                gossipContext.Drafts.History(gossipContext, false);
                gossipContext.Rebuild();
                if (!gossipContext.Effective.All(gossipPage.Options.Table).Any(r => r.T("OptionText") == "New option"))
                { SmokeFail("CLIENT_SHOT_GOSSIP_UNDO"); return; }
                gossipContext.Drafts.History(gossipContext, true);
                gossipContext.Rebuild();
                if (!gossipContext.Effective.All(gossipPage.Options.Table).Any(r => r.T("OptionText") == "Visual gossip check, $N."))
                { SmokeFail("CLIENT_SHOT_GOSSIP_REDO"); return; }
                var editGreeting = creatureProps.Section("gossip")!.FindChild("EditGreetingVariants", true, false) as Button;
                if (editGreeting == null) { SmokeFail("CLIENT_SHOT_GREETING_MISSING"); return; }
                editGreeting.EmitSignal(Button.SignalName.Pressed);
                clientShotFrames = 0;
                clientShotCreatureStage = 21;
                return;
            case 21:
                if (clientShotFrames++ < 60) return;
                var section = creatureProps.Section("gossip")!;
                var greetingWeight = section.FindChild("GreetingWeight0", true, false) as LineEdit;
                var greetingSave = section.FindChild("SaveGreeting0", true, false) as Button;
                if (greetingWeight == null || greetingSave == null) { SmokeFail("CLIENT_SHOT_GREETING_EDITOR_MISSING"); return; }
                greetingWeight.Text = "0.75";
                greetingSave.EmitSignal(Button.SignalName.Pressed);
                clientShotFrames = 0;
                clientShotCreatureStage = 22;
                return;
            case 22:
                if (clientShotFrames++ < 60) return;
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-creature-greeting.png"));
                var gc = creatureProps.Context;
                if (!gc.Effective.All(gc.Schema.GossipPage.TextTable).Any(r => r.F("Probability0") == .75f))
                { SmokeFail("CLIENT_SHOT_GREETING_SAVE " + gc.Status); return; }
                var gossipSql = Foundation.SqlExport.Build(gc.SourceKey, [], gc.Draft!.NetEdits().Select(e => e.Edit).ToArray(), schema: gc.Schema);
                if (!gossipSql.Apply.Contains("Probability0") || !gossipSql.Revert.Contains("gossip_menu_option"))
                { SmokeFail("CLIENT_SHOT_GOSSIP_EXPORT"); return; }
                // Restore all three successful commands before continuing the other service checks.
                gc.Drafts.History(gc, false);
                gc.Drafts.History(gc, false);
                gc.Drafts.History(gc, false);
                gc.Rebuild();
                // A menu shared with other creatures can be copied for this one, and the copy undone.
                var copyMenu = creatureProps.Section("gossip")!.FindChild("CopyMenu", true, false) as Button;
                if (copyMenu == null) { SmokeFail("CLIENT_SHOT_MENU_COPY button missing"); return; }
                uint copyEntry = gc.TcView!.Entry;
                var rootBefore = gc.Effective.All(Foundation.TcTables.CreatureGossip).Single(r => r.U("CreatureID") == copyEntry).U("MenuID");
                copyMenu.EmitSignal(Button.SignalName.Pressed);
                var rootAfter = gc.Effective.All(Foundation.TcTables.CreatureGossip).Single(r => r.U("CreatureID") == copyEntry).U("MenuID");
                if (rootAfter == rootBefore || !gc.Effective.All(Foundation.TcTables.GossipOption).Any(r => r.U("MenuID") == rootAfter))
                { SmokeFail("CLIENT_SHOT_MENU_COPY " + gc.Status); return; }
                gc.Drafts.History(gc, false);
                gc.Rebuild();
                if (creatureProps.Section("gossip")!.FindChild("NewMenu", true, false) is not Button)
                { SmokeFail("CLIENT_SHOT_NEW_MENU control missing"); return; }
                // A greeting typed instead of picked becomes a new broadcast text in the hotfixes database.
                var gossipBox = creatureProps.Section("gossip")!;
                if (gossipBox.FindChild("NewMenuText", true, false) is not LineEdit typedGreeting || gossipBox.FindChild("NewMenu", true, false) is not Button newMenuButton)
                { SmokeFail("CLIENT_SHOT_NEW_TEXT control missing"); return; }
                typedGreeting.Text = "Smoke greeting text.";
                newMenuButton.EmitSignal(Button.SignalName.Pressed);
                if (!gc.Effective.All(Foundation.TcTables.BroadcastText).Any(r => r.T("Text") == "Smoke greeting text.") || !gc.Effective.All(Foundation.TcTables.HotfixData).Any())
                { SmokeFail("CLIENT_SHOT_NEW_TEXT " + gc.Status); return; }
                var textSql = Foundation.SqlExport.Build(gc.SourceKey, [], gc.Draft!.NetEdits().Select(e => e.Edit).ToArray(), schema: gc.Schema);
                if (!textSql.Apply.Contains("INSERT INTO `" + Foundation.SqlExport.HotfixesDatabase + "`.`broadcast_text`"))
                { SmokeFail("CLIENT_SHOT_NEW_TEXT_EXPORT"); return; }
                gc.Drafts.History(gc, false);
                gc.Rebuild();
                if (gc.Effective.All(Foundation.TcTables.BroadcastText).Any())
                { SmokeFail("CLIENT_SHOT_NEW_TEXT_UNDO"); return; }
                GD.Print("CLIENT_SHOT_GOSSIP_EDIT_OK submenu/back/add/text/validation/greeting/undo/redo/export");
                creatureProps.ShowSection("template", null);
                clientShotFrames = 0;
                clientShotCreatureStage = 3;
                return;
            case 3:
                if (clientShotFrames++ < 30)
                {
                    return;
                }

                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-creature-template.png"));
                if (creatureProps.Context.Editable)
                {
                    var addDifficulty = creatureProps.Section("template")!.FindChild("AddDifficulty", true, false) as OptionButton;
                    if (addDifficulty == null || addDifficulty.ItemCount < 2) { SmokeFail("CLIENT_SHOT_DIFFICULTY_ADD_MISSING"); return; }
                    clientShotDifficulty = addDifficulty.GetItemId(1);
                    addDifficulty.Select(1);
                    addDifficulty.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
                    clientShotFrames = 0;
                    clientShotCreatureStage = 30;
                    return;
                }
                creatureProps.ShowSection("vendor", null);
                clientShotFrames = 0;
                clientShotCreatureStage = 4;
                return;
            case 30:
                if (clientShotFrames++ < 60) return;
                var dc = creatureProps.Context;
                var difficulties = dc.Schema.TemplatePage.Difficulty!;
                if (!difficulties.Select(dc.Effective, ShotCreature).Any(r => r.I(difficulties.IdColumn) == clientShotDifficulty))
                { SmokeFail("CLIENT_SHOT_DIFFICULTY_ADD " + dc.Status); return; }
                if (creatureProps.Section("template")!.FindChild("DifficultyPicker", true, false) is not OptionButton shownDifficulty
                    || shownDifficulty.GetSelectedId() != clientShotDifficulty)
                { SmokeFail("CLIENT_SHOT_DIFFICULTY_NOT_SHOWN"); return; }
                // The picker sits in the Combat group, below the first screen of the page.
                for (Node? node = shownDifficulty; node != null; node = node.GetParent())
                {
                    if (node is ScrollContainer scroll)
                    {
                        scroll.ScrollVertical = (int)(shownDifficulty.GlobalPosition.Y - scroll.GlobalPosition.Y) + scroll.ScrollVertical - 40;
                        break;
                    }
                }
                clientShotFrames = 0;
                clientShotCreatureStage = 31;
                return;
            case 31:
                if (clientShotFrames++ < 20) return;
                dc = creatureProps.Context;
                difficulties = dc.Schema.TemplatePage.Difficulty!;
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-difficulty-add.png"));
                if (creatureProps.Section("template")!.FindChild("RemoveDifficulty", true, false) is not Button removeDifficulty)
                { SmokeFail("CLIENT_SHOT_DIFFICULTY_REMOVE_MISSING"); return; }
                removeDifficulty.EmitSignal(BaseButton.SignalName.Pressed);
                if (difficulties.Select(dc.Effective, ShotCreature).Any(r => r.I(difficulties.IdColumn) == clientShotDifficulty))
                { SmokeFail("CLIENT_SHOT_DIFFICULTY_REMOVE " + dc.Status); return; }
                dc.Drafts.History(dc, false);
                dc.Rebuild();
                if (!difficulties.Select(dc.Effective, ShotCreature).Any(r => r.I(difficulties.IdColumn) == clientShotDifficulty))
                { SmokeFail("CLIENT_SHOT_DIFFICULTY_REMOVE_UNDO"); return; }
                dc.Drafts.History(dc, false);
                dc.Rebuild();
                if (difficulties.Select(dc.Effective, ShotCreature).Any(r => r.I(difficulties.IdColumn) == clientShotDifficulty))
                { SmokeFail("CLIENT_SHOT_DIFFICULTY_UNDO"); return; }
                GD.Print($"CLIENT_SHOT_DIFFICULTY_EDIT_OK add={clientShotDifficulty}/shown/remove/undo");
                creatureProps.ShowSection("vendor", null);
                clientShotFrames = 0;
                clientShotCreatureStage = 4;
                return;
            case 4:
                if (clientShotFrames++ < 30)
                {
                    return;
                }

                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-creature-vendor.png"));
                GD.Print($"CLIENT_SHOT_CREATURE entry={creatureProps.Entry} dialect={creatureProps.Context.Dialect} vendor={creatureProps.Context.Schema.VendorPage.Select(creatureProps.Context.Effective, ShotCreature).Count()}");
                if (creatureProps.Context.Editable)
                {
                    var search = creatureProps.Section("vendor")!.FindChild("Search_item", true, false) as LineEdit;
                    if (search == null) { SmokeFail("CLIENT_SHOT_VENDOR_SEARCH_MISSING"); return; }
                    search.Text = "2589"; // Linen Cloth, not one of Farley's drinks/food.
                    search.EmitSignal(LineEdit.SignalName.TextSubmitted, search.Text);
                    clientShotFrames = 0;
                    clientShotCreatureStage = 40;
                    return;
                }
                OpenShotCreature(514, "trainer", 5); // Smith Argus, Goldshire's blacksmith trainer.
                return;
            case 40:
                var addItem = creatureProps.Section("vendor")!.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.StartsWith("+ ") && b.Text.EndsWith("(2589)"));
                if (addItem == null)
                {
                    if (clientShotFrames++ > 1800) SmokeFail("CLIENT_SHOT_VENDOR_SEARCH " + creatureProps.Context.Status);
                    return;
                }
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-vendor-search.png"));
                addItem.EmitSignal(Button.SignalName.Pressed);
                clientShotFrames = 0;
                clientShotCreatureStage = 41;
                return;
            case 41:
                var context = creatureProps.Context;
                var vendor = context.Schema.VendorPage;
                var added = vendor.Select(context.Effective, ShotCreature).FirstOrDefault(r => r.I("item") == 2589);
                if (added == null)
                {
                    if (clientShotFrames++ > 600) SmokeFail("CLIENT_SHOT_VENDOR_ADD " + context.Status);
                    return;
                }
                // A stock-only change is invalid; the paired change, undo and redo must work.
                if (context.Commit("Invalid vendor stock", Foundation.ItemListSpec.Change(vendor.Table, added, added.With("maxcount", 5u))))
                { SmokeFail("CLIENT_SHOT_VENDOR_VALIDATION accepted stock without restock"); return; }
                if (!context.Commit("Set vendor stock", Foundation.ItemListSpec.Change(vendor.Table, added, added.With("maxcount", 5u).With("incrtime", 60u))))
                { SmokeFail("CLIENT_SHOT_VENDOR_STOCK " + context.Status); return; }
                context.Drafts.History(context, false);
                context.Rebuild();
                if (vendor.Select(context.Effective, ShotCreature).First(r => r.I("item") == 2589).U("maxcount") != 0)
                { SmokeFail("CLIENT_SHOT_VENDOR_UNDO"); return; }
                context.Drafts.History(context, true);
                context.Rebuild();
                if (vendor.Select(context.Effective, ShotCreature).First(r => r.I("item") == 2589).U("maxcount") != 5)
                { SmokeFail("CLIENT_SHOT_VENDOR_REDO"); return; }
                // The client tables behind the reference checks load, and an unknown extended cost is refused.
                var tables = context.Lookup();
                if (tables == null || !tables.Exists(Foundation.ClientRecord.Difficulty, 1) || !tables.Exists(Foundation.ClientRecord.SkillLine, 164))
                { SmokeFail("CLIENT_SHOT_VENDOR_CLIENT_TABLES difficulty/skill line missing"); return; }
                foreach (var kind in new[] { Foundation.ClientRecord.Currency, Foundation.ClientRecord.ExtendedCost, Foundation.ClientRecord.PlayerCondition, Foundation.ClientRecord.BonusList })
                {
                    if (tables.Exists(kind, 4000000000u)) { SmokeFail("CLIENT_SHOT_VENDOR_CLIENT_TABLES " + kind); return; }
                }
                var stocked = vendor.Select(context.Effective, ShotCreature).First(r => r.I("item") == 2589);
                if (context.Commit("Unknown extended cost", Foundation.ItemListSpec.Change(vendor.Table, stocked, stocked.With("ExtendedCost", 4000000000u))))
                { SmokeFail("CLIENT_SHOT_VENDOR_VALIDATION accepted an unknown extended cost"); return; }
                context.Drafts.History(context, false);
                context.Drafts.History(context, false);
                context.Rebuild();
                // A row that points at a shared vendor list shows the list's items once the creature reloads.
                if (!context.Commit("Add shared vendor list", [vendor.Add(context.Effective, ShotCreature, ("item", -(int)ShotCreature), ("type", 1u))]))
                { SmokeFail("CLIENT_SHOT_VENDOR_SHARED " + context.Status); return; }
                clientShotFrames = 0;
                clientShotCreatureStage = 42;
                return;
            case 42:
                var sharedLabel = creatureProps.Section("vendor")!.FindChildren("*", "Label", true, false).OfType<Label>().FirstOrDefault(l => l.Text.StartsWith($"Shared vendor list {ShotCreature} · ", StringComparison.Ordinal));
                if (sharedLabel == null || creatureProps.Context.Snapshot!.Lookup.ReferenceVendor(ShotCreature).Count == 0)
                {
                    if (clientShotFrames++ > 600) SmokeFail("CLIENT_SHOT_VENDOR_SHARED list not shown " + creatureProps.Context.Status);
                    return;
                }
                if (sharedVendorFrames++ == 0)
                {
                    // Scroll the list into view and let a few frames draw before the screenshot.
                    for (Node ancestor = sharedLabel.GetParent(); ancestor != null; ancestor = ancestor.GetParent())
                    {
                        if (ancestor is ScrollContainer scroller) { scroller.EnsureControlVisible(sharedLabel); break; }
                    }
                }

                if (sharedVendorFrames < 15) return;
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-vendor-shared.png"));
                creatureProps.Context.Drafts.History(creatureProps.Context, false);
                creatureProps.Context.Rebuild();
                GD.Print("CLIENT_SHOT_VENDOR_EDIT_OK search/add/validation/stock/undo/redo/shared");
                OpenShotCreature(514, "trainer", 5);
                return;
            case 5:
                if (!ShotCreatureReady(514)) return;
                bool hasSpells = creatureProps.Context.AcView?.Trainer?.Spells.Length > 0 || creatureProps.Context.TcView?.Trainers.Any(t => t.Spells.Length > 0) == true;
                if (!hasSpells)
                {
                    SmokeFail("CLIENT_SHOT_TRAINER_EMPTY Smith Argus has no trainer spells");
                    return;
                }
                if (creatureProps.Context.Editable && creatureProps.Context.TcView?.Trainers.FirstOrDefault(t => t.Spells.Length > 0) is { } tcTrainer)
                {
                    var trainerCtx = creatureProps.Context;
                    var spells = trainerCtx.Schema.TrainerPage.Spells;
                    var first = spells.Select(trainerCtx.Effective, tcTrainer.TrainerId).First();
                    if (!trainerCtx.Commit("Change trainer cost", Foundation.ItemListSpec.Change(spells.Table, first, first.With("MoneyCost", first.U("MoneyCost") + 1))))
                    { SmokeFail("CLIENT_SHOT_TRAINER_EDIT " + trainerCtx.Status); return; }
                    if (trainerCtx.Commit("Invalid trainer level", Foundation.ItemListSpec.Change(spells.Table, first, first.With("ReqLevel", 256u))))
                    { SmokeFail("CLIENT_SHOT_TRAINER_VALIDATION accepted level 256"); return; }
                    GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-trainer-edit.png"));
                    trainerCtx.Drafts.History(trainerCtx, false);
                    trainerCtx.Rebuild();
                    if (spells.Select(trainerCtx.Effective, tcTrainer.TrainerId).First(r => r.U("SpellId") == first.U("SpellId")).U("MoneyCost") != first.U("MoneyCost"))
                    { SmokeFail("CLIENT_SHOT_TRAINER_UNDO"); return; }
                    // Trainer bindings: the section offers them, and a trainer that does not exist is refused.
                    if (!creatureProps.Section("trainer")!.FindChildren("*", "Button", true, false).OfType<Button>().Any(b => b.Text == "Remove binding"))
                    { SmokeFail("CLIENT_SHOT_TRAINER_BINDING controls missing"); return; }
                    var binding = trainerCtx.Effective.All(Foundation.TcTables.CreatureTrainer).First(r => r.U("TrainerID") == tcTrainer.TrainerId);
                    if (trainerCtx.Commit("Unknown trainer", Foundation.ItemListSpec.Change(Foundation.TcTables.CreatureTrainer, binding, binding.With("TrainerID", 4000000000u))))
                    { SmokeFail("CLIENT_SHOT_TRAINER_BINDING accepted an unknown trainer"); return; }
                    GD.Print("CLIENT_SHOT_TRAINER_EDIT_OK edit/validation/undo");
                }
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-creature-trainer.png"));
                OpenShotCreature(240, "stats", 7); // Marshal Dughan, a quest giver in Goldshire.
                return;
            case 7:
                if (!ShotCreatureReady(240)) return;
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-creature-stats.png"));
                if (creatureProps.Context.Editable)
                {
                    var sc = creatureProps.Context;
                    var captions = creatureProps.Section("stats")!.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text).ToList();
                    if (!captions.Contains("Health") || !captions.Any(t => t.Length > 6 && t.StartsWith("Level ", StringComparison.Ordinal) && char.IsDigit(t[6])))
                    { SmokeFail("CLIENT_SHOT_STATS_MISSING " + string.Join("|", captions.Take(40))); return; }
                    // The base values come from creature_classlevelstats: the level's attack power must be on the page.
                    uint statsLevel = uint.Parse(captions.First(t => t.Length > 6 && t.StartsWith("Level ", StringComparison.Ordinal) && char.IsDigit(t[6]))[6..]);
                    uint statsClass = sc.Effective.Get(Foundation.TcTables.CreatureTemplate, "240")!.U("unit_class");
                    if (!sc.Snapshot!.Lookup.TcClassStats.TryGetValue((statsLevel, statsClass), out var baseStats))
                    { SmokeFail($"CLIENT_SHOT_STATS_CLASS_ROW no creature_classlevelstats row for level {statsLevel} class {statsClass}"); return; }
                    if (!captions.Contains(baseStats.AttackPower.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)))
                    { SmokeFail($"CLIENT_SHOT_STATS_ATTACK_POWER expected {baseStats.AttackPower} on the page: " + string.Join("|", captions.Take(60))); return; }
                    var difficulty = sc.Schema.TemplatePage.Difficulty!.Select(sc.Effective, 240).First();
                    if (sc.Commit("Invalid scaling expansion", Foundation.ItemListSpec.Change(Foundation.TcTables.CreatureDifficulty, difficulty, difficulty.With("HealthScalingExpansion", 12))))
                    { SmokeFail("CLIENT_SHOT_STATS_VALIDATION accepted expansion 12"); return; }
                    if (!sc.Commit("Change level delta", Foundation.ItemListSpec.Change(Foundation.TcTables.CreatureDifficulty, difficulty, difficulty.With("LevelScalingDeltaMax", 2))))
                    { SmokeFail("CLIENT_SHOT_STATS_EDIT " + sc.Status); return; }
                    sc.Drafts.History(sc, false);
                    sc.Rebuild();
                    if (sc.Schema.TemplatePage.Difficulty!.Select(sc.Effective, 240).First().I("LevelScalingDeltaMax") != difficulty.I("LevelScalingDeltaMax"))
                    { SmokeFail("CLIENT_SHOT_STATS_UNDO"); return; }
                    GD.Print($"CLIENT_SHOT_STATS_EDIT_OK attackPower={baseStats.AttackPower} mana={baseStats.BaseMana} levels={captions.First(t => t.Length > 6 && t.StartsWith("Level ", StringComparison.Ordinal) && char.IsDigit(t[6]))}/validation/undo");
                }
                OpenShotCreature(240, "quests", 6);
                return;
            case 50:
                if (questEditor.IsLoading || questEditor.QuestId != clientShotQuest || questEditor.SmokeQuest is not { } loadedQuest)
                {
                    if (clientShotFrames++ > 1200) SmokeFail("CLIENT_SHOT_QUEST_EDITOR_NOT_LOADED " + questEditor.SmokeStatus);
                    return;
                }
                if (loadedQuest.T("LogTitle").Length == 0) { SmokeFail("CLIENT_SHOT_QUEST_EDITOR_TITLE"); return; }
                questEditor.ShowTab(QuestPanel.Tab.Objectives);
                int before = questEditor.SmokeEffective.All(TcTables.QuestObjectives).Count(o => o.U("QuestID") == clientShotQuest);
                if (!questEditor.SmokePress("+ Add objective")) { SmokeFail("CLIENT_SHOT_QUEST_EDITOR_ADD_MISSING"); return; }
                if (questEditor.SmokeEffective.All(TcTables.QuestObjectives).Count(o => o.U("QuestID") == clientShotQuest) != before + 1)
                { SmokeFail("CLIENT_SHOT_QUEST_EDITOR_ADD " + questEditor.SmokeStatus); return; }
                if (!questEditor.SmokeIssues.Any(i => !i.Error && i.Message.Contains("no target", StringComparison.Ordinal)))
                { SmokeFail("CLIENT_SHOT_QUEST_EDITOR_VALIDATION an objective without a target raised no warning: " + string.Join("; ", questEditor.SmokeIssues.Select(i => i.Message))); return; }
                questEditor.SmokeHistory(false);
                if (questEditor.SmokeEffective.All(TcTables.QuestObjectives).Count(o => o.U("QuestID") == clientShotQuest) != before)
                { SmokeFail("CLIENT_SHOT_QUEST_EDITOR_UNDO"); return; }
                if (!questEditor.SmokeSet(TcTables.QuestTemplate, "Rename quest", ("LogTitle", "Smoke quest")) || questEditor.SmokeQuest?.T("LogTitle") != "Smoke quest")
                { SmokeFail("CLIENT_SHOT_QUEST_EDITOR_RENAME " + questEditor.SmokeStatus); return; }
                if (questEditor.SmokeSet(TcTables.QuestTemplate, "Change reward item", ("RewardItem1", 4000000000u), ("RewardAmount1", 1u)))
                { SmokeFail("CLIENT_SHOT_QUEST_EDITOR_ITEM_VALIDATION accepted an unknown reward item"); return; }
                if (questEditor.SmokeSet(TcTables.QuestTemplate, "Change reward currency", ("RewardCurrencyID1", 4000000000u), ("RewardCurrencyQty1", 1u)))
                { SmokeFail("CLIENT_SHOT_QUEST_EDITOR_CURRENCY_VALIDATION accepted an unknown currency"); return; }
                questEditor.ShowTab(QuestPanel.Tab.Objectives);
                clientShotFrames = 0;
                clientShotCreatureStage = 51;
                return;
            case 51:
                if (clientShotFrames++ < 60) return;
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-quest-editor.png"));
                foreach (var tab in Enum.GetValues<QuestPanel.Tab>())
                {
                    questEditor.ShowTab(tab);
                }
                GD.Print($"CLIENT_SHOT_QUEST_EDITOR_OK quest={clientShotQuest} add/validation/undo/rename/references issues={questEditor.SmokeIssues.Count}");
                // A quest with a kill objective (the Hogger quest in TDB).
                questEditor.Open(176);
                clientShotFrames = 0;
                clientShotCreatureStage = 52;
                return;
            case 52:
                if (questEditor.IsLoading || questEditor.QuestId != 176 || questEditor.SmokeQuest == null)
                {
                    if (clientShotFrames++ > 1200) SmokeFail("CLIENT_SHOT_QUEST_EDITOR_OBJECTIVES_NOT_LOADED " + questEditor.SmokeStatus);
                    return;
                }
                questEditor.ShowTab(QuestPanel.Tab.Objectives);
                clientShotFrames = 0;
                clientShotCreatureStage = 53;
                return;
            case 53:
                if (clientShotFrames++ < 60) return;
                if (questEditor.SmokeEffective.All(TcTables.QuestObjectives).All(o => o.U("QuestID") != 176))
                { SmokeFail("CLIENT_SHOT_QUEST_EDITOR_OBJECTIVES the quest has no objective rows"); return; }
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-quest-objectives.png"));
                GD.Print("CLIENT_SHOT_QUEST_OBJECTIVES_OK quest=176");
                clientShotFrames = 0;
                clientShotCreatureStage = 60;
                return;
            case 60:
                // Creature Templates toolbar button: search by name, pick, and the creature window opens on it.
                creatureProps.Visible = false;
                OpenCreatureTemplateSearch();
                if (GetChildren().OfType<PickerPopup>().LastOrDefault(p => p.Visible) is not { } templatePicker) { SmokeFail("CLIENT_SHOT_TEMPLATE_SEARCH_MISSING"); return; }
                templatePicker.SmokeSearch("Farley");
                clientShotFrames = 0;
                clientShotCreatureStage = 61;
                return;
            case 61:
                var picker = GetChildren().OfType<PickerPopup>().LastOrDefault(p => p.Visible);
                if (picker == null) { SmokeFail("CLIENT_SHOT_TEMPLATE_SEARCH_CLOSED"); return; }
                if (picker.ResultCount == 0 || picker.ShownTerm != "Farley")
                {
                    if (clientShotFrames++ > 1200) SmokeFail("CLIENT_SHOT_TEMPLATE_SEARCH_EMPTY");
                    return;
                }
                picker.Choose(0);
                clientShotFrames = 0;
                clientShotCreatureStage = 62;
                return;
            case 62:
                if (!creatureProps.Visible || creatureProps.Context.Target is not { } found || !found.Name.Contains("Farley", StringComparison.Ordinal))
                {
                    if (clientShotFrames++ > 600) SmokeFail("CLIENT_SHOT_TEMPLATE_SEARCH_OPEN the creature window did not open on the pick");
                    return;
                }
                GD.Print($"CLIENT_SHOT_TEMPLATE_SEARCH_OK entry={found.TemplateId} name={found.Name}");
                if (creatureProps.Context.Editable)
                {
                    // The behavior editor on Hogger (4 rules, one linked, speech linked to broadcast text).
                    questEditor.Visible = false;
                    creatureProps.OpenEntry(448, "Hogger", "behavior");
                    clientShotFrames = 0;
                    clientShotCreatureStage = 70;
                    return;
                }

                GD.Print($"CLIENT_SMOKE_OK kind={store!.Kind} database={databaseEnabled} overlays=True graveyardsEditable=True creatureWindow=True");
                GetTree().Quit();
                return;
            case 70:
                var behavior = creatureProps.Context.Behavior;
                var hoggerScope = new SmartScope(448, SmartScope.Creature);
                if (behavior.IsLoading || behavior.Entry != 448 || behavior.SmokeRules(hoggerScope).Count == 0)
                {
                    if (clientShotFrames++ > 1200) SmokeFail("CLIENT_SHOT_BEHAVIOR_NOT_LOADED " + behavior.SmokeStatus);
                    return;
                }

                int rulesBefore = behavior.SmokeRules(hoggerScope).Count;
                if (rulesBefore != 4 || behavior.SmokeAiName != "SmartAI" || !behavior.SmokeRules(hoggerScope).Any(r => behavior.SmokeDescribe(r).Contains("enters combat", StringComparison.Ordinal)))
                { SmokeFail($"CLIENT_SHOT_BEHAVIOR_RULES rules={rulesBefore} ai={behavior.SmokeAiName}: " + string.Join(" | ", behavior.SmokeRules(hoggerScope).Select(behavior.SmokeDescribe))); return; }
                behavior.SmokeAddRule(0);
                if (behavior.SmokeRules(hoggerScope).Count != rulesBefore + 1)
                { SmokeFail("CLIENT_SHOT_BEHAVIOR_ADD " + behavior.SmokeStatus); return; }
                var addedRule = behavior.SmokeRules(hoggerScope).Last();
                if (behavior.SmokeUpdate(addedRule, "Invalid chance", ("event_chance", 101u)))
                { SmokeFail("CLIENT_SHOT_BEHAVIOR_VALIDATION accepted a chance of 101%"); return; }
                if (!behavior.SmokeUpdate(addedRule, "Change difficulties", ("Difficulties", "0,1")) || behavior.SmokeRules(hoggerScope).Last().Row.T("Difficulties") != "0,1")
                { SmokeFail("CLIENT_SHOT_BEHAVIOR_DIFFICULTIES " + behavior.SmokeStatus); return; }
                behavior.SmokeHistory(false);
                behavior.SmokeHistory(false);
                if (behavior.SmokeRules(hoggerScope).Count != rulesBefore)
                { SmokeFail("CLIENT_SHOT_BEHAVIOR_UNDO rules=" + behavior.SmokeRules(hoggerScope).Count); return; }
                clientShotFrames = 0;
                clientShotCreatureStage = 71;
                return;
            case 71:
                if (clientShotFrames++ < 60) return;
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-behavior.png"));
                // The expanded card of the first rule: the TrinityCore-only Difficulties field is on it.
                creatureProps.Context.Behavior.SmokeExpand(creatureProps.Context.Behavior.SmokeRules(new SmartScope(448, SmartScope.Creature))[0].Id);
                clientShotFrames = 0;
                clientShotCreatureStage = 73;
                return;
            case 73:
                if (clientShotFrames++ < 60) return;
                if (creatureProps.Context.Behavior.SmokeControl("Difficulties") == null)
                { SmokeFail("CLIENT_SHOT_BEHAVIOR_DIFFICULTIES_FIELD missing on the expanded rule"); return; }
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-behavior-rule.png"));
                creatureProps.ShowSection("speech", null);
                clientShotFrames = 0;
                clientShotCreatureStage = 72;
                return;
            case 72:
                if (clientShotFrames++ < 60) return;
                if (creatureProps.Context.Behavior.SmokeLineCount < 2)
                { SmokeFail("CLIENT_SHOT_SPEECH_LINES lines=" + creatureProps.Context.Behavior.SmokeLineCount); return; }
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-speech.png"));
                GD.Print($"CLIENT_SHOT_BEHAVIOR_OK entry=448 rules=4 lines={creatureProps.Context.Behavior.SmokeLineCount} add/validation/difficulties/undo");
                // The waypoint layer next: select a path point near Goldshire and edit it.
                creatureProps.Visible = false;
                clientShotFrames = 0;
                clientShotCreatureStage = 80;
                return;
            case 80:
                if (waypoints.SmokeQueryIdle && waypoints.SmokeNodeNear(camera) is { } near)
                {
                    waypoints.ShowAll = true;
                    waypoints.Select(near.Path.Id, near.Node.Point);
                    clientShotFrames = 0;
                    clientShotCreatureStage = 81;
                }
                else if (clientShotFrames++ > 600)
                { SmokeFail("CLIENT_SHOT_WAYPOINTS_NONE no followed path near Goldshire " + waypoints.QueryStatus); }
                return;
            case 81:
                if (clientShotFrames++ < 60) return;
                var fields = waypoints.SmokeFields;
                // TrinityCore stores facing and delay per node; the rest must not look editable.
                if (waypoints.SmokeSelected is null || fields.SmoothShown || fields.ActionShown || fields.VelocityEditable || fields.MoveTypeEditable || !fields.DelayEditable || !fields.FacingEditable)
                { SmokeFail($"CLIENT_SHOT_WAYPOINT_FIELDS {fields}"); return; }
                if (!waypoints.SmokeEditing())
                { SmokeFail("CLIENT_SHOT_WAYPOINT_EDIT"); return; }
                waypoints.SmokeScrollToEnd();
                clientShotFrames = 0;
                clientShotCreatureStage = 82;
                return;
            case 82:
                if (clientShotFrames++ < 15) return;
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-waypoint.png"));
                GD.Print("CLIENT_SHOT_WAYPOINT_OK move/delay/undo/redo/revert export waypoint_path_node; path-level fields read-only");
                GD.Print($"CLIENT_SMOKE_OK kind={store!.Kind} database={databaseEnabled} overlays=True graveyardsEditable=True creatureWindow=True");
                GetTree().Quit();
                return;
            default:
                if (!ShotCreatureReady(240)) return;
                int quests = creatureProps.Context.AcView is { } ac ? ac.Starts.Length + ac.Ends.Length
                    : creatureProps.Context.TcView is { } tc ? tc.Starts.Length + tc.Ends.Length : 0;
                if (quests == 0)
                {
                    SmokeFail("CLIENT_SHOT_QUESTS_EMPTY Marshal Dughan has no quest links");
                    return;
                }
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-creature-quests.png"));
                if (creatureProps.Context.Editable && creatureProps.Context.TcView is { Starts.Length: > 0 } dughan)
                {
                    // Unlink the first quest he starts, then undo: the link must come back unchanged.
                    var qc = creatureProps.Context;
                    var links = qc.Schema.QuestLinksPage;
                    uint quest = dughan.Starts[0].Quest;
                    bool ends = dughan.Ends.Any(q => q.Quest == quest);
                    if (!qc.Commit($"Unlink quest {quest}", links.SetRoles(qc.Effective, 240, quest, false, ends)))
                    { SmokeFail("CLIENT_SHOT_QUEST_UNLINK " + qc.Status); return; }
                    if (links.Starts.Select(qc.Effective, 240).Any(r => r.U("quest") == quest))
                    { SmokeFail("CLIENT_SHOT_QUEST_UNLINK still linked"); return; }
                    if (qc.Commit("Invalid quest link", links.SetRoles(qc.Effective, 240, 0, true, false)))
                    { SmokeFail("CLIENT_SHOT_QUEST_VALIDATION accepted quest 0"); return; }
                    qc.Drafts.History(qc, false);
                    qc.Rebuild();
                    if (!links.Starts.Select(qc.Effective, 240).Any(r => r.U("quest") == quest))
                    { SmokeFail("CLIENT_SHOT_QUEST_UNDO"); return; }
                    if (creatureProps.Section("quests")!.FindChild("Search_quest", true, false) == null)
                    { SmokeFail("CLIENT_SHOT_QUEST_SEARCH_MISSING"); return; }
                    GD.Print($"CLIENT_SHOT_QUEST_EDIT_OK unlink={quest}/validation/undo");
                    clientShotQuest = quest;
                }
                GD.Print($"CLIENT_SHOT_SERVICES trainer=514 quests=240 links={quests}");
                if (clientShotQuest != 0)
                {
                    // The TrinityCore quest editor on a quest Marshal Dughan offers.
                    creatureProps.Visible = false;
                    questEditor.Open(clientShotQuest);
                    clientShotFrames = 0;
                    clientShotCreatureStage = 50;
                    return;
                }
                clientShotFrames = 0;
                clientShotCreatureStage = 60;
                return;
        }
    }

    void OpenShotCreature(uint entry, string section, int stage)
    {
        if (creatures.Records.FirstOrDefault(s => s.TemplateId == entry) is not { } spawn)
        {
            SmokeFail($"CLIENT_SHOT_CREATURE_MISSING no spawn of creature {entry} near Goldshire");
            return;
        }
        creatures.Select(spawn.Guid);
        creatureProps.Open(spawn, section);
        clientShotFrames = 0;
        clientShotCreatureStage = stage;
    }

    bool ShotCreatureReady(uint entry)
    {
        if (creatureProps.Context.View?.Entry != entry)
        {
            if (clientShotFrames++ > 600) SmokeFail("CLIENT_SHOT_CREATURE_NOT_LOADED " + creatureProps.Context.Status);
            return false;
        }
        return clientShotFrames++ >= 60;
    }

    void ClientSmoke(double delta)
    {
        clientSmokeTime += delta;
        if (clientSmokeTime > 400)
        {
            SmokeFail($"CLIENT_SMOKE_TIMEOUT settled={TerrainSettled} loaded={loaded.Count}/{wanted.Count} modelsBusy={models.Busy} {message}");
            return;
        }

        if (busy)
        {
            return;
        }

        if (store == null || mapList.Count == 0)
        {
            // LoadClient reports its failure through the status message and then goes idle.
            if (clientSmokeTime > 5 && !message.StartsWith("Opening", StringComparison.Ordinal) && !message.StartsWith("Reading", StringComparison.Ordinal))
            {
                SmokeFail("CLIENT_SMOKE_NO_CLIENT " + message);
            }

            return;
        }

        if (world == null)
        {
            if (message.StartsWith("Map error", StringComparison.Ordinal))
            {
                SmokeFail("CLIENT_SMOKE_MAP_FAILED " + message);
            }

            return;
        }

        // A terrain map must also stream its first tile (worker decode plus main-thread upload); the
        // dummy headless renderer accepts the uploads, so this exercises the whole path but not the output.
        if (world.Tiles.Count > 0 && loaded.Count == 0)
        {
            if (message.StartsWith("Tile error", StringComparison.Ordinal) || message.StartsWith("Render error", StringComparison.Ordinal))
            {
                SmokeFail("CLIENT_SMOKE_TILE_FAILED " + message);
            }

            return;
        }

        // --client-shot additionally waits for the visible terrain to finish, lets a few frames render and
        // saves artifacts/client-shot.png for inspection. It needs a real window (not --headless).
        if (clientShot && DisplayServer.GetName() != "headless")
        {
            if (clientShotMap != null)
            {
                int mapIndex = mapList.FindIndex(m => m.Directory.Equals(clientShotMap, StringComparison.OrdinalIgnoreCase));
                if (mapIndex >= 0 && currentMapId != (ushort)mapList[mapIndex].Id)
                {
                    if (maps.Selected != mapIndex)
                    {
                        maps.Select(mapIndex);
                        _ = OpenMap();
                    }

                    return;
                }
            }

            // --client-shot-tile=X,Y moves the camera over that tile first, then waits for it to finish.
            if (!clientShotMoved)
            {
                clientShotMoved = true;
                if (clientShotTile is { } target)
                {
                    Teleport(target);
                    return;
                }
            }

            if (pending != null || building != null || !wanted.All(loaded.ContainsKey) || models.Busy || clientShotFrames++ < 120)
            {
                return;
            }

            // --client-shot-overlays keeps the interface and shows the database layers over Goldshire:
            // creatures, GameObjects, graveyards and AreaTriggers, all read from the world database.
            if (clientShotOverlays)
            {
                if (!clientShotOverlaysFocused)
                {
                    // The poses below are Eastern Kingdoms coordinates; open that map first if another is showing.
                    if (currentMapId != 0)
                    {
                        int azeroth = mapList.FindIndex(m => m.Directory == "Azeroth");
                        if (azeroth >= 0 && maps.Selected != azeroth)
                        {
                            maps.Select(azeroth);
                            tileX.Value = 31;
                            tileY.Value = 49;
                            _ = OpenMap();
                        }

                        return;
                    }

                    clientShotOverlaysFocused = true;
                    areaTriggers.SetLayerEnabled(true);
                    FocusPose(new(0, -9465.5, 74, 56.8, 0));
                    clientShotFrames = 0;
                    return;
                }

                if (clientShotCreatureStage > 0)
                {
                    ClientShotCreature();
                    return;
                }

                bool ready = creatures.Records.Count > 0 && gameObjects.Records.Count > 0 && graveyards.Sites.Count > 0 && areaTriggers.Triggers.Count > 0;
                if (!ready || creatures.IsLoading || gameObjects.IsLoading || areaTriggers.IsLoading || models.Busy || clientShotFrames++ < 240)
                {
                    return;
                }

                GD.Print($"CLIENT_SHOT_OVERLAYS creatures={creatures.Records.Count} models={creatures.ModelCreatureCount} gameobjects={gameObjects.Records.Count} models={gameObjects.ModelObjectCount} graveyards={graveyards.Sites.Count} triggers={areaTriggers.Triggers.Count}");
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot-overlays.png"));
                // TrinityCore graveyards export world_safe_locs and team-condition statements, so they are editable.
                graveyards.Select(graveyards.Sites[0].Record.Id);
                if (!graveyards.CanEdit)
                {
                    SmokeFail("CLIENT_SHOT_GRAVEYARD_READONLY a TrinityCore graveyard refused edits");
                    return;
                }

                graveyards.Deselect();
                clientShotCreatureStage = 1;
                return;
            }

            // Frame the largest decoded building close up, and hide the interface so it does not cover the scene.
            if (!clientShotFramed)
            {
                clientShotFramed = true;
                if (clientShotSky is { } skyShot)
                {
                    sky.SetTimeOfDay(skyShot.Hour);
                    camera.Position += new Vector3(0, skyShot.Lift, 0);
                    camera.Rotation = new Vector3(Mathf.DegToRad(skyShot.PitchDegrees), Mathf.DegToRad(skyShot.YawDegrees), 0);
                    yaw = camera.Rotation.Y;
                    pitch = camera.Rotation.X;
                    GD.Print($"CLIENT_SHOT_SKY hour={skyShot.Hour} yaw={skyShot.YawDegrees} camera={camera.Position} fog={sky.ClientFogValues} height={sky.ClientFogHeightValues} debanding={GetViewport().UseDebanding} lights=[{sky.ActiveLights}]");
                    clientShotFrames = 0;
                }
                else if (clientShotWater && LargestLiquid() is { } lake)
                {
                    GD.Print($"CLIENT_SHOT_WATER center={lake}");
                    camera.Position = lake + new Vector3(0, 90, 140);
                    camera.LookAt(lake);
                    yaw = camera.Rotation.Y;
                    pitch = camera.Rotation.X;
                    clientShotFrames = 0;
                }
                else if (objects.ReadyWmoPlacements().Where(w => !w.BoundsMinimum.HasValue || !w.BoundsMaximum.HasValue || (w.BoundsMaximum.Value - w.BoundsMinimum.Value).Length() < 150).OrderByDescending(w => w.BoundsMinimum.HasValue && w.BoundsMaximum.HasValue ? (w.BoundsMaximum.Value - w.BoundsMinimum.Value).Length() : 0).FirstOrDefault() is { } building)
                {
                    var at = new Vector3(building.Position.X, building.Position.Y, building.Position.Z);
                    var size = building.BoundsMinimum.HasValue && building.BoundsMaximum.HasValue ? (building.BoundsMaximum.Value - building.BoundsMinimum.Value).Length() : 60;
                    var center = building.BoundsMinimum.HasValue && building.BoundsMaximum.HasValue ? (building.BoundsMaximum.Value + building.BoundsMinimum.Value) / 2 : building.Position;
                    var target = new Vector3(center.X, center.Y, center.Z);
                    GD.Print($"CLIENT_SHOT_BUILDING {building.Path} id={building.Id} size={size:F0} at={at}");
                    camera.Position = target + new Vector3(0, size * 0.5f, size * 1.1f);
                    camera.LookAt(target);
                    yaw = camera.Rotation.Y;
                    pitch = camera.Rotation.X;
                    clientShotFrames = 0;
                }

                foreach (var item in GetChildren().OfType<CanvasItem>())
                {
                    item.Visible = false;
                }

                foreach (var layer in GetChildren().OfType<CanvasLayer>())
                {
                    layer.Visible = false;
                }

                return;
            }

            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot.png"));
        }

        GD.Print($"CLIENT_SMOKE_OK kind={store.Kind} build={store.Build} maps={mapList.Count} map={mapList[Math.Max(maps.Selected, 0)].Directory} tiles={world.Tiles.Count} streamed={loaded.Count} textures={renderer.TextureCount} globalWmos={world.GlobalObjects.Count} database={databaseEnabled} catalogs={models.Catalog != null} message=\"{message}\"");
        GetTree().Quit();
    }
}
