using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>
/// Engine- and database-independent checks of InteractionDraft (grouped row edits, undo/redo,
/// reopen, conflicts, whitelist) and of the view builder and server rules over a synthetic NPC.
/// </summary>
public class InteractionDraftTests
{
    [Fact]
    public void Passes() => Core();

    sealed class FakeLookup : IInteractionLookup
    {
        public ItemInfo? Item(uint id) => id is 2589 or 4540 ? new(id, id == 2589 ? "Linen Cloth" : "Tough Hunk of Bread", 1, 10, 0) : null;
        public QuestInfo? Quest(uint id) => id == 7 ? new(7, "Kobold Camp Cleanup", 3, 2, "", "", 0, []) : null;
        public string? SpellName(uint id) => id == 133 ? "Fireball (Rank 1)" : null;
        public bool ExtendedCostExists(uint id) => id == 2425;
        public (string Male, string Female)? BroadcastText(uint id) => id == 900 ? ("Broadcast hello", "Broadcast hello") : null;
        public bool PoiExists(uint id) => false;
        public bool MenuExists(uint menuId) => menuId == 50;
        public bool TextExists(uint textId) => false;
        public bool TrainerExists(uint id) => false;
        public IReadOnlyList<Row> ReferenceVendor(uint entry) => [];
        public int MenuUsers(uint menuId) => menuId == 50 ? 3 : 1;
        public int TextUsers(uint textId) => 1;
        public int TrainerUsers(uint trainerId) => 1;
    }

    static NpcInteraction View(RowSet snapshot, RowSet rows) => NpcInteraction.Build(42, 1, "Test NPC", "", "", 1, snapshot, rows, new FakeLookup(), [], new Dictionary<int, int>());

    static void Core()
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "interactions.json"));
        var snapshot = new RowSet();
        var template = Row.Of(("entry", 42u), ("npcflag", NpcFlags.Gossip), ("gossip_menu_id", 50u), ("subname", "Tester"));
        snapshot.Set(InteractionTables.CreatureTemplate, template);
        snapshot.Set(InteractionTables.GossipMenu, Row.Of(("MenuID", 50u), ("TextID", 60u)));
        var text = Row.Of(("ID", 60u), ("text0_0", "Hello $N.$BWelcome."), ("text0_1", ""), ("BroadcastTextID0", 900), ("Probability0", 1f));
        snapshot.Set(InteractionTables.NpcText, text);
        var option = Row.Of(("MenuID", 50u), ("OptionID", 0u), ("OptionIcon", 1u), ("OptionText", "Let me browse your goods."), ("OptionBroadcastTextID", 0), ("OptionType", 3u), ("OptionNpcFlag", NpcFlags.Vendor), ("ActionMenuID", 0u), ("ActionPoiID", 0u), ("BoxCoded", 0u), ("BoxMoney", 0u), ("BoxText", ""));
        snapshot.Set(InteractionTables.GossipOption, option);
        var bread = Row.Of(("entry", 42u), ("item", 4540), ("ExtendedCost", 0u), ("slot", 0), ("maxcount", 0u), ("incrtime", 0u));
        snapshot.Set(InteractionTables.Vendor, bread);

        // Builder: broadcast text wins over npc_text; option hidden while VENDOR flag is missing.
        var view = View(snapshot, snapshot);
        Check(view.Menus[50].Texts[0].Male == "Broadcast hello" && view.Menus[50].Texts[0].FromBroadcast, "Broadcast text precedence");
        Check(view.Menus[50].Options[0].HiddenByFlags && view.Menus[50].SharedBy == 3, "Option NpcFlag gate / shared count");
        Check(view.Issues.Any(i => !i.Error && i.Message.Contains("Vendor") && i.Fix != null), "Missing vendor flag warning");
        Check(NpcInteraction.Render("Hello $N.$BYou are a $g lad:lass;.") == "Hello <name>.\nYou are a lad.", "Placeholder rendering");

        using (var draft = new InteractionDraft(path))
        {
            // One command: new submenu (menu + text) and an option leading to it.
            var newText = Row.Of(("ID", 61u), ("text0_0", "Sub menu text"), ("text0_1", ""), ("BroadcastTextID0", 0), ("Probability0", 1f));
            var newMenu = Row.Of(("MenuID", 51u), ("TextID", 61u));
            var newOption = option.With("OptionID", 1u).With("OptionText", "Tell me more").With("OptionType", 1u).With("OptionIcon", 0u).With("OptionNpcFlag", NpcFlags.Gossip).With("ActionMenuID", 51u);
            draft.Execute("db", snapshot, "Add option", [new("npc_text", "61", null, newText), new("gossip_menu", "51|61", null, newMenu), new("gossip_menu_option", "50|1", null, newOption)]);
            var effective = draft.Effective("db", snapshot);
            view = View(snapshot, effective);
            Check(view.Menus.ContainsKey(51) && view.Menus[50].Options.Length == 2 && view.Menus[50].Options[1].Edited && view.Menus[51].Texts[0].Male == "Sub menu text", "Submenu command");
            Check(draft.MaxReserved("db", InteractionTables.GossipMenu, "MenuID") == 51, "Reserved ID");

            // Text edit clears the broadcast ID so the custom text applies in game.
            draft.Execute("db", snapshot, "Edit greeting", [new("npc_text", "60", text, text.With("text0_0", "Custom hi").With("BroadcastTextID0", 0))]);
            draft.Execute("db", snapshot, "Remove bread", [new("npc_vendor", "42|4540|0", bread, null)]);
            draft.Execute("db", snapshot, "Add linen", [new("npc_vendor", "42|2589|0", null, bread.With("item", 2589))]);
            view = View(snapshot, draft.Effective("db", snapshot));
            Check(view.Menus[50].Texts[0].Male == "Custom hi" && !view.Menus[50].Texts[0].FromBroadcast, "Greeting edit");
            Check(view.Vendor.Length == 1 && view.Vendor[0].Item == 2589, "Vendor remove/add");

            // Server-rule errors and the npcflag warning fix.
            draft.Execute("db", snapshot, "Link quest", [new("creature_queststarter", "42|7", null, Row.Of(("id", 42u), ("quest", 7u)))]);
            view = View(snapshot, draft.Effective("db", snapshot));
            var questWarning = view.Issues.Single(i => !i.Error && i.Message.Contains("Quest"));
            draft.Execute("db", snapshot, "Fix flag", questWarning.Fix!);
            view = View(snapshot, draft.Effective("db", snapshot));
            Check((view.NpcFlag & NpcFlags.QuestGiver) != 0 && !view.Issues.Any(i => i.Message.Contains("Quests are linked")), "Questgiver flag fix");
            var limited = View(snapshot, Apply(draft.Effective("db", snapshot), InteractionTables.Vendor, bread.With("item", 2589).With("maxcount", 5u)));
            Check(limited.Issues.Any(i => i.Error && i.Message.Contains("restock")), "maxcount without incrtime rule");
            var missingQuest = View(snapshot, Apply(draft.Effective("db", snapshot), InteractionTables.QuestStarter, Row.Of(("id", 42u), ("quest", 999u))));
            Check(missingQuest.Issues.Any(i => i.Error && i.Message.Contains("999")), "Missing quest rule");

            // Guards: whitelist, primary key, stale Before.
            Rejects(() => draft.Execute("db", snapshot, "bad", [new("npc_vendor", "42|1|0", null, bread.With("item", 1).With("VerifiedBuild", 1))]), "Unlisted column accepted");
            Rejects(() => draft.Execute("db", snapshot, "bad", [new("npc_vendor", "42|9|0", null, bread)]), "Key mismatch accepted");
            Rejects(() => draft.Execute("db", snapshot, "bad", [new("npc_text", "60", text, text.With("text0_0", "x"))]), "Stale Before accepted");
            Rejects(() => draft.Execute("db", snapshot, "bad", [new("quest_template", "1", null, Row.Of(("ID", 1u)))]), "Non-editable table accepted");

            draft.Undo();
            draft.Undo();
            Check((View(snapshot, draft.Effective("db", snapshot)).NpcFlag & NpcFlags.QuestGiver) == 0, "Undo command");
            draft.Redo();
            Check(draft.LastLabel == "Link quest", "Redo label");
        }

        using (var draft = new InteractionDraft(path))
        {
            view = View(snapshot, draft.Effective("db", snapshot));
            Check(view.Starts.Length == 1 && view.Vendor[0].Item == 2589 && view.Menus.ContainsKey(51), "Reopen");

            // Database changed under the draft: the edited text and a reserved ID now exist.
            var changed = snapshot.Clone();
            changed.Set(InteractionTables.NpcText, text.With("text0_0", "Changed in DB"));
            changed.Set(InteractionTables.GossipMenu, Row.Of(("MenuID", 51u), ("TextID", 61u)));
            var conflicts = draft.Conflicts("db", changed);
            Check(conflicts.Contains(("npc_text", "60")) && conflicts.Contains(("gossip_menu", "51|61")), "Conflict detection");
            Check(draft.Effective("db", changed).Get(InteractionTables.NpcText, "60")!.T("text0_0") == "Changed in DB", "Conflicting rows keep database value");
            Rejects(() => draft.Execute("db", changed, "blocked", [new("creature_template", "42", template, template.With("subname", "x"))]), "Edit over conflicts accepted");
        }

        Console.WriteLine("INTERACTION_DRAFT_CHECKS_PASSED command/submenu/greeting/vendor/quest-fix/rules/whitelist/key/stale/undo/redo/reopen/conflicts/broadcast/render");
    }

    static RowSet Apply(RowSet rows, TableSchema table, Row row)
    {
        var copy = rows.Clone();
        copy.Set(table, row);
        return copy;
    }
}
