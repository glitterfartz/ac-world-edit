using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Quest editor pages. Each control reads the effective rows and commits one labelled command; the
/// page re-renders from the draft afterwards, so what is shown is always what is drafted. Column
/// names stay out of the UI: captions describe what the player experiences.
/// </summary>
public partial class QuestEditorWindow
{
    static readonly (uint Value, string Name)[] XpTiers = [(0, "None"), (1, "Tiny"), (2, "Very small"), (3, "Small"), (4, "Below normal"), (5, "Normal"), (6, "Above normal"), (7, "Large"), (8, "Very large"), (9, "Huge")];
    static readonly (uint Value, string Name)[] RacePresets = [(0, "Everyone"), (QuestModel.AllianceRaces, "Alliance only"), (QuestModel.HordeRaces, "Horde only"), (1, "Specific races…")];

    TableSchema Template => InteractionTables.QuestTemplate;
    TableSchema Addon => InteractionTables.QuestAddon;

    void Heading(string text, string help = "")
    {
        page.AddChild(ClientStyle.Text(text, 16, ClientStyle.Header));
        if (help.Length > 0)
        {
            var label = ClientStyle.Text(help, 11, ClientStyle.Faded);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            page.AddChild(label);
        }
    }

    void Add(string caption, params Control[] controls) => page.AddChild(EditorFields.Row(caption, controls));

    LineEdit UNumber(TableSchema table, string column, string label, int width = 80, string tooltip = "") =>
        EditorFields.Number(RowOf(table).L(column), value => Set(table, label, (column, value)), width, tooltip);

    string Named(SmartParamKind kind, uint id, string empty = "None")
    {
        if (id == 0)
        {
            return empty;
        }

        var names = snapshot!.Names;
        string? name = kind switch
        {
            SmartParamKind.Creature => names.Creature(id),
            SmartParamKind.GameObject => names.GameObject(id),
            SmartParamKind.Item => names.Item(id),
            SmartParamKind.Quest => names.Quest(id),
            _ => names.Spell(id)
        };
        return name == null ? $"Missing #{id}" : $"{name}  ·  {id}";
    }

    bool Missing(SmartParamKind kind, uint id) => id != 0 && Named(kind, id).StartsWith("Missing", StringComparison.Ordinal);

    /// <summary>
    /// A reference column edited through a picker, with a clear button.
    /// </summary>
    HBoxContainer Ref(TableSchema table, string column, SmartParamKind kind, string what, string label, bool signed = false)
    {
        var row = RowOf(table);
        uint id = signed ? (uint)Math.Abs(row.I(column)) : row.U(column);
        Action choose = kind == SmartParamKind.Spell
            ? () => PickSpell($"Choose {what}", picked => Set(table, label, (column, picked)))
            : () => Pick(kind, $"Choose {what}", picked => Set(table, label, (column, picked)));
        return EditorFields.Reference(Named(kind, id), choose, id == 0 ? null : () => Set(table, label, (column, 0)), Missing(kind, id));
    }

    void RenderOverview(Row q)
    {
        Heading("Basics");
        Add("Title", EditorFields.Line(q.T("LogTitle"), text => Set(Template, "Rename quest", ("LogTitle", text.Trim())), "What the quest log calls it"));
        Add("Kind", EditorFields.Choice(QuestModel.Types, q.U("QuestInfoID"), value => Set(Template, "Change quest kind", ("QuestInfoID", value))),
            ClientStyle.Tag("Group size"), UNumber(Template, "SuggestedGroupNum", "Change suggested group size", 50, "0 = solo"));
        Add("Availability", EditorFields.Choice(QuestModel.Methods, q.U("QuestType"), value => Set(Template, "Change quest availability", ("QuestType", value)), "Disabled quests are hidden; auto-complete quests finish on accept."));
        uint zone = q.I("QuestSortID") > 0 ? (uint)q.I("QuestSortID") : 0;
        string zoneText = q.I("QuestSortID") switch
        {
            0 => "No zone",
            < 0 => $"Quest log category {-q.I("QuestSortID")}",
            _ => Areas()?.Contains(zone) == true ? Areas()!.Describe(zone).Zone : $"Missing zone #{zone}"
        };
        Add("Quest log zone", EditorFields.Reference(zoneText, PickZone, q.I("QuestSortID") == 0 ? null : () => Set(Template, "Clear quest zone", ("QuestSortID", 0)), zoneText.StartsWith("Missing", StringComparison.Ordinal)));

        Heading("Levels", "Quest level sets XP and the con colour; −1 scales with the player. Players outside required…maximum cannot accept it.");
        Add("Quest level", UNumber(Template, "QuestLevel", "Change quest level", 60),
            ClientStyle.Tag("Required"), UNumber(Template, "MinLevel", "Change required level", 60),
            ClientStyle.Tag("Maximum"), UNumber(Addon, "MaxLevel", "Change maximum level", 60, "0 = no maximum"));
        Add("Time limit", UNumber(Template, "TimeAllowed", "Change time limit", 80, "Seconds; 0 = untimed"), ClientStyle.Tag("seconds (0 = none)"));

        Heading("Who can take it");
        uint races = q.U("AllowableRaces");
        uint preset = races is 0 or QuestModel.AllianceRaces or QuestModel.HordeRaces ? races : 1;
        Add("Faction", EditorFields.Choice(RacePresets, preset, value => Set(Template, "Change allowed races", ("AllowableRaces", value == 1 ? QuestModel.AllianceRaces | QuestModel.HordeRaces : value))));
        if (preset == 1)
        {
            page.AddChild(EditorFields.Flags(QuestModel.Races, races, value => Set(Template, "Change allowed races", ("AllowableRaces", value)), 5));
        }

        uint classes = RowOf(Addon).U("AllowableClasses");
        var allClasses = new CheckBox
        {
            Text = "All classes",
            ButtonPressed = classes == 0
        };
        allClasses.Toggled += on => Set(Addon, "Change allowed classes", ("AllowableClasses", on ? 0u : 1u));
        Add("Classes", allClasses);
        if (classes != 0)
        {
            page.AddChild(EditorFields.Flags(QuestModel.Classes, classes, value => Set(Addon, "Change allowed classes", ("AllowableClasses", value)), 5));
        }

        Heading("Behavior");
        page.AddChild(EditorFields.Flags(QuestModel.Flags, q.U("Flags"), value => Set(Template, "Change quest options", ("Flags", value)), 3));
        page.AddChild(EditorFields.Flags(QuestModel.SpecialFlags, RowOf(Addon).U("SpecialFlags"), value => Set(Addon, "Change special options", ("SpecialFlags", value)), 3));
    }

    void PickZone()
    {
        if (Areas() is not { } areas)
        {
            status = "Zone names need the client AreaTable; enter the zone ID in the search box instead.";
            UpdateStatus();
            return;
        }

        PickerPopup.Show(this, "Choose quest log zone", term => Task.FromResult<IReadOnlyList<(uint Id, string Name)>>(areas.Search(term, null, 60)
            .Select(a => (a.Id, a.Parent == null ? a.Name : $"{a.Name} ({a.Parent})")).ToList()),
            (id, _) => Set(Template, "Change quest zone", ("QuestSortID", (int)id)));
    }

    void RenderStory(Row q)
    {
        var request = RowOf(InteractionTables.QuestRequestItems);
        var offer = RowOf(InteractionTables.QuestOfferReward);
        page.AddChild(EditorFields.Placeholders(QuestModel.Placeholders));
        Heading("1 · When offered", "What the quest giver says before the player accepts.");
        page.AddChild(EditorFields.Area(q.T("QuestDescription"), text => Set(Template, "Edit quest text", ("QuestDescription", text)), 120));
        Heading("Objective summary", "One-line goal shown under \"Quest Objectives\" in the log.");
        page.AddChild(EditorFields.Area(q.T("LogDescription"), text => Set(Template, "Edit objective summary", ("LogDescription", text)), 50));
        Heading("2 · When the player returns early", "Shown when the player talks to the ender before finishing.");
        page.AddChild(EditorFields.Area(request.T("CompletionText"), text => Set(InteractionTables.QuestRequestItems, "Edit progress text", ("CompletionText", text)), 70));
        Add("Giver gesture", EmoteChoice(InteractionTables.QuestRequestItems, "EmoteOnIncomplete", "Change progress gesture"),
            ClientStyle.Tag("when ready"), EmoteChoice(InteractionTables.QuestRequestItems, "EmoteOnComplete", "Change ready gesture"));
        Heading("3 · When turned in", "The reward screen text.");
        page.AddChild(EditorFields.Area(offer.T("RewardText"), text => Set(InteractionTables.QuestOfferReward, "Edit turn-in text", ("RewardText", text)), 100));
        for (int i = 1; i <= 4; i++)
        {
            int slot = i;
            Add(i == 1 ? "Gestures" : "", EmoteChoice(InteractionTables.QuestOfferReward, $"Emote{slot}", "Change turn-in gesture"),
                ClientStyle.Tag("after"), UNumber(InteractionTables.QuestOfferReward, $"EmoteDelay{slot}", "Change gesture delay", 60), ClientStyle.Tag("ms"));
        }

        Heading("Completion log line", "Quest log text once the objectives are done (optional).");
        page.AddChild(EditorFields.Area(q.T("QuestCompletionLog"), text => Set(Template, "Edit completion log text", ("QuestCompletionLog", text)), 40));
    }

    OptionButton EmoteChoice(TableSchema table, string column, string label)
    {
        var emotes = (catalog?.All("Emote") ?? []).Where(e => e.Name.Length > 0).Prepend((0u, "none")).DistinctBy(e => e.Item1).ToList();
        return EditorFields.Choice(emotes, RowOf(table).U(column), value => Set(table, label, (column, value)));
    }

    void RenderObjectives(Row q)
    {
        Heading("Defeat or use", "Kill a creature, or use a game object, a number of times. Custom text replaces \"<name> slain\".");
        for (int i = 1; i <= 4; i++)
        {
            int slot = i;
            int target = q.I($"RequiredNpcOrGo{slot}");
            var row = new HBoxContainer();
            row.AddChild(ClientStyle.Tag($"{slot}."));
            if (target == 0)
            {
                row.AddChild(SmallButton("Kill a creature…", () => Pick(SmartParamKind.Creature, "Choose creature to defeat", id => SetObjective(slot, (int)id))));
                row.AddChild(SmallButton("Use an object…", () => Pick(SmartParamKind.GameObject, "Choose object to use", id => SetObjective(slot, -(int)id))));
            }
            else
            {
                var kind = target > 0 ? SmartParamKind.Creature : SmartParamKind.GameObject;
                row.AddChild(ClientStyle.Tag(target > 0 ? "Defeat" : "Use"));
                row.AddChild(EditorFields.Number(q.U($"RequiredNpcOrGoCount{slot}"), count => Set(Template, "Change objective count", ($"RequiredNpcOrGoCount{slot}", count)), 50));
                row.AddChild(ClientStyle.Tag("×"));
                row.AddChild(EditorFields.Reference(Named(kind, (uint)Math.Abs(target)), () => Pick(kind, "Choose target", id => SetObjective(slot, target > 0 ? (int)id : -(int)id)), () => Set(Template, "Remove objective", ($"RequiredNpcOrGo{slot}", 0), ($"RequiredNpcOrGoCount{slot}", 0), ($"ObjectiveText{slot}", "")), Missing(kind, (uint)Math.Abs(target))));
            }

            page.AddChild(row);
            if (target != 0)
            {
                Add("   custom text", EditorFields.Line(q.T($"ObjectiveText{slot}"), text => Set(Template, "Edit objective text", ($"ObjectiveText{slot}", text)), "Optional, e.g. \"Kobolds driven off\""));
            }
        }

        Heading("Collect items", "Items the player must hold when turning in; they are taken on completion.");
        for (int i = 1; i <= 6; i++)
        {
            ItemSlot(q, $"{i}.", $"RequiredItemId{i}", $"RequiredItemCount{i}", "required item", "Change required item");
        }

        Heading("Quest drops", "Items this quest makes drop for players on it (the loot itself is set on the creature or object).");
        for (int i = 1; i <= 4; i++)
        {
            ItemSlot(q, $"{i}.", $"ItemDrop{i}", $"ItemDropQuantity{i}", "quest drop", "Change quest drop");
        }

        Heading("Other goals");
        Add("Players defeated", UNumber(Template, "RequiredPlayerKills", "Change player kills", 60, "Honorable kills required"));
        for (int i = 1; i <= 2; i++)
        {
            int slot = i;
            Add(slot == 1 ? "Reach reputation" : "", FactionRef(Template, $"RequiredFactionId{slot}", "Change reputation goal"), ClientStyle.Tag("at least"), UNumber(Template, $"RequiredFactionValue{slot}", "Change reputation goal", 70));
        }

        Heading("Given on accept", "An item the giver hands over when the quest is accepted.");
        Add("Starting item", Ref(Template, "StartItem", SmartParamKind.Item, "starting item", "Change starting item"), ClientStyle.Tag("×"), UNumber(Addon, "ProvidedItemCount", "Change starting item count", 50));
        Add("Cast on accept", Ref(Addon, "SourceSpellID", SmartParamKind.Spell, "spell cast on accept", "Change spell cast on accept"));
    }

    void SetObjective(int slot, int target)
    {
        var q = Quest!;
        uint count = Math.Max(1u, q.U($"RequiredNpcOrGoCount{slot}"));
        Set(Template, "Set objective", ($"RequiredNpcOrGo{slot}", target), ($"RequiredNpcOrGoCount{slot}", count));
    }

    void ItemSlot(Row q, string caption, string itemColumn, string countColumn, string what, string label)
    {
        uint item = q.U(itemColumn);
        if (item == 0)
        {
            Add(caption, SmallButton($"Add {what}…", () => Pick(SmartParamKind.Item, $"Choose {what}", id => Set(Template, label, (itemColumn, id), (countColumn, Math.Max(1u, Quest!.U(countColumn)))))));
            return;
        }

        Add(caption, EditorFields.Number(q.U(countColumn), count => Set(Template, label, (countColumn, count)), 50), ClientStyle.Tag("×"),
            EditorFields.Reference(Named(SmartParamKind.Item, item), () => Pick(SmartParamKind.Item, $"Choose {what}", id => Set(Template, label, (itemColumn, id))), () => Set(Template, label, (itemColumn, 0), (countColumn, 0)), Missing(SmartParamKind.Item, item)));
    }

    static Button SmallButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text
        };
        button.Pressed += action;
        return button;
    }

    HBoxContainer FactionRef(TableSchema table, string column, string label)
    {
        uint faction = RowOf(table).U(column);
        string text = faction == 0 ? "None" : catalog?.FactionName(faction) is { } name ? name : $"Faction {faction}";
        return EditorFields.Reference(text, () => PickFromList("Choose faction", catalog?.All("Faction") ?? [], id => Set(table, label, (column, id))), faction == 0 ? null : () => Set(table, label, (column, 0)));
    }

    void RenderRewards(Row q)
    {
        Heading("Money and experience");
        int money = q.I("RewardMoney");
        long abs = Math.Abs((long)money);
        void SetMoney(long gold, long silver, long copper, bool pays)
        {
            long total = gold * 10000 + silver * 100 + copper;
            Set(Template, pays ? "Change money required" : "Change money reward", ("RewardMoney", (int)Math.Clamp(pays ? -total : total, int.MinValue, int.MaxValue)));
        }

        var pays = new CheckBox
        {
            Text = "Player pays instead",
            ButtonPressed = money < 0,
            TooltipText = "Negative money: the quest takes this amount on turn-in."
        };
        pays.Toggled += on => SetMoney(abs / 10000, abs / 100 % 100, abs % 100, on);
        Add("Money",
            EditorFields.Number(abs / 10000, g => SetMoney(g, abs / 100 % 100, abs % 100, money < 0), 60), ClientStyle.Tag("g"),
            EditorFields.Number(abs / 100 % 100, s => SetMoney(abs / 10000, s, abs % 100, money < 0), 40), ClientStyle.Tag("s"),
            EditorFields.Number(abs % 100, c => SetMoney(abs / 10000, abs / 100 % 100, c, money < 0), 40), ClientStyle.Tag("c"), pays);
        Add("Experience", EditorFields.Choice(XpTiers, q.U("RewardXPDifficulty"), value => Set(Template, "Change experience reward", ("RewardXPDifficulty", value)), "Scaled by quest level (QuestXP.dbc)"),
            ClientStyle.Tag("Money at max level"), EditorFields.Choice(XpTiers, q.U("RewardMoneyDifficulty"), value => Set(Template, "Change max-level money", ("RewardMoneyDifficulty", value))));

        Heading("Always given", "Every player receives these items.");
        for (int i = 1; i <= 4; i++)
        {
            ItemSlot(q, $"{i}.", $"RewardItem{i}", $"RewardAmount{i}", "reward item", "Change reward item");
        }

        Heading("Choose one of", "The player picks one item from this list.");
        for (int i = 1; i <= 6; i++)
        {
            ItemSlot(q, $"{i}.", $"RewardChoiceItemID{i}", $"RewardChoiceItemQuantity{i}", "choice", "Change choice reward");
        }

        Heading("Reputation", "Standard tier (1–9) uses the client's reward table; an exact amount overrides it.");
        for (int i = 1; i <= 5; i++)
        {
            int slot = i;
            if (q.U($"RewardFactionID{slot}") == 0 && slot > 1 && q.U($"RewardFactionID{slot - 1}") == 0)
            {
                continue;
            }

            Add($"{slot}.", FactionRef(Template, $"RewardFactionID{slot}", "Change reputation reward"),
                ClientStyle.Tag("tier"), UNumber(Template, $"RewardFactionValue{slot}", "Change reputation tier", 40),
                ClientStyle.Tag("or exact"), EditorFields.Number(q.I($"RewardFactionOverride{slot}") / 100, value => Set(Template, "Change reputation amount", ($"RewardFactionOverride{slot}", value * 100)), 60));
        }

        Heading("Other rewards");
        Add("Spell cast on player", Ref(Template, "RewardSpell", SmartParamKind.Spell, "spell cast on the player", "Change reward spell"));
        Add("Spell shown as reward", Ref(Template, "RewardDisplaySpell", SmartParamKind.Spell, "displayed reward spell", "Change displayed spell"));
        Add("Honor", UNumber(Template, "RewardHonor", "Change honor reward", 60), ClientStyle.Tag("Arena points"), UNumber(Template, "RewardArenaPoints", "Change arena reward", 60),
            ClientStyle.Tag("Talents"), UNumber(Template, "RewardTalents", "Change talent reward", 40), ClientStyle.Tag("Title ID"), UNumber(Template, "RewardTitle", "Change title reward", 50));
        Add("Mail afterwards", UNumber(Addon, "RewardMailTemplateID", "Change reward mail", 70, "Mail template ID; 0 = none"), ClientStyle.Tag("after"), UNumber(Addon, "RewardMailDelay", "Change mail delay", 70), ClientStyle.Tag("seconds"));
    }

    void RenderChain(Row q)
    {
        var addon = RowOf(Addon);
        int prev = addon.I("PrevQuestID");
        uint next = addon.U("NextQuestID");
        string Arrow(uint id) => id == 0 ? "—" : Named(SmartParamKind.Quest, id);
        var chain = ClientStyle.Text($"{Arrow((uint)Math.Abs(prev))}   →   {q.T("LogTitle")}   →   {Arrow(next != 0 ? next : q.U("RewardNextQuest"))}", 15, ClientStyle.Ink);
        chain.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        page.AddChild(chain);
        Heading("Before this quest");
        Add("Previous quest", Ref(Addon, "PrevQuestID", SmartParamKind.Quest, "previous quest", "Change previous quest", true),
            EditorFields.Choice([(0, "must be completed"), (1, "must be in progress")], prev < 0 ? 1u : 0u, value => Set(Addon, "Change previous-quest rule", ("PrevQuestID", value == 1 ? -Math.Abs(prev) : Math.Abs(prev)))));
        Add("Breadcrumb to", Ref(Addon, "BreadcrumbForQuestId", SmartParamKind.Quest, "quest this leads to", "Change breadcrumb target"));
        Heading("After this quest");
        Add("Unlocks", Ref(Addon, "NextQuestID", SmartParamKind.Quest, "next quest", "Change next quest"));
        Add("Offered right away", Ref(Template, "RewardNextQuest", SmartParamKind.Quest, "quest offered on turn-in", "Change follow-up quest"));
        Heading("Exclusive group", "Quests sharing a positive group: completing one hides the rest. Negative group: all must be done to unlock the next quest. 0 = none.");
        Add("Group", UNumber(Addon, "ExclusiveGroup", "Change exclusive group", 80));

        Heading("Requirements");
        Add("Skill", UNumber(Addon, "RequiredSkillID", "Change required skill", 60, "SkillLine ID, e.g. 197 Tailoring"), ClientStyle.Tag("at least"), UNumber(Addon, "RequiredSkillPoints", "Change required skill", 60));
        Add("Reputation at least", FactionRef(Addon, "RequiredMinRepFaction", "Change reputation requirement"), UNumber(Addon, "RequiredMinRepValue", "Change reputation requirement", 70));
        Add("Reputation below", FactionRef(Addon, "RequiredMaxRepFaction", "Change reputation limit"), UNumber(Addon, "RequiredMaxRepValue", "Change reputation limit", 70));
        page.AddChild(ClientStyle.Text("Conditions (table conditions) and scripted availability are not edited here.", 11, ClientStyle.Faded));
    }

    void RenderGivers()
    {
        GiverList(InteractionTables.QuestStarter, "Who offers it", "quest giver", "Add quest giver");
        GiverList(InteractionTables.QuestEnder, "Who completes it", "quest ender", "Add quest ender");
        var starters = effective.All(InteractionTables.QuestStarter).Where(r => r.U("quest") == QuestId).Select(r => r.U("id")).ToList();
        var enders = effective.All(InteractionTables.QuestEnder).Where(r => r.U("quest") == QuestId).Select(r => r.U("id")).ToHashSet();
        var missing = starters.Where(s => !enders.Contains(s)).ToList();
        if (missing.Count > 0)
        {
            page.AddChild(SmallButton("Givers also complete it", () => Commit("Givers also complete the quest", missing.Select(id =>
                new RowEdit(InteractionTables.QuestEnder.Name, $"{id}|{QuestId}", null, Row.Of(("id", id), ("quest", QuestId!.Value)))).ToArray())));
        }

        page.AddChild(ClientStyle.Text("Creatures also need the Quest Giver flag (Interactions window). Object and item starters are not edited here.", 11, ClientStyle.Faded));
    }

    void GiverList(TableSchema table, string heading, string what, string label)
    {
        Heading(heading);
        foreach (var link in effective.All(table).Where(r => r.U("quest") == QuestId).OrderBy(r => r.U("id")))
        {
            uint entry = link.U("id");
            var row = new HBoxContainer();
            row.AddChild(ClientStyle.Text(Named(SmartParamKind.Creature, entry), 14, Missing(SmartParamKind.Creature, entry) ? ClientStyle.Error : null));
            row.AddChild(SmallButton("Fly to", async () =>
            {
                status = $"Locating {snapshot!.Names.Creature(entry) ?? $"creature {entry}"}…";
                UpdateStatus();
                status = await FlyToEntry(entry, snapshot.Names.Creature(entry) ?? $"Creature {entry}");
                UpdateStatus();
            }));
            row.AddChild(SmallButton("Behavior…", () => BehaviorRequested(entry)));
            var linkRow = link;
            row.AddChild(SmallButton("Remove", () => Commit($"Remove {what}", new RowEdit(table.Name, table.KeyOf(linkRow), linkRow, null))));
            page.AddChild(row);
        }

        page.AddChild(SmallButton($"+ Add {what}…", () => Pick(SmartParamKind.Creature, $"Choose {what}", id =>
        {
            var row = Row.Of(("id", id), ("quest", QuestId!.Value));
            if (effective.Get(table, table.KeyOf(row)) == null)
            {
                Commit(label, new RowEdit(table.Name, table.KeyOf(row), null, row));
            }
        })));
    }

    /// <summary>
    /// Client-style read-only rendering of the drafted quest, as the player would read it.
    /// </summary>
    void RenderPreview(Row q)
    {
        var request = RowOf(InteractionTables.QuestRequestItems);
        var offer = RowOf(InteractionTables.QuestOfferReward);
        void Text(string text, int size = 14, Color? color = null)
        {
            var label = ClientStyle.Text(NpcInteraction.Render(text), size, color ?? ClientStyle.Ink);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            page.AddChild(label);
        }

        Text(q.T("LogTitle"), 19, ClientStyle.Header);
        Text(q.T("QuestDescription"));
        Text("Quest Objectives", 16, ClientStyle.Header);
        Text(q.T("LogDescription"));
        for (int i = 1; i <= 4; i++)
        {
            int target = q.I($"RequiredNpcOrGo{i}");
            if (target != 0)
            {
                string name = q.T($"ObjectiveText{i}") is { Length: > 0 } custom ? custom : (target > 0 ? snapshot!.Names.Creature((uint)target) + " slain" : snapshot!.Names.GameObject((uint)-target)) ?? "?";
                Text($"  {name}: 0/{q.U($"RequiredNpcOrGoCount{i}")}", 13);
            }
        }

        for (int i = 1; i <= 6; i++)
        {
            if (q.U($"RequiredItemId{i}") is var item && item != 0)
            {
                Text($"  {snapshot!.Names.Item(item) ?? "?"}: 0/{q.U($"RequiredItemCount{i}")}", 13);
            }
        }

        Text("Rewards", 16, ClientStyle.Header);
        var rewards = Enumerable.Range(1, 4).Where(i => q.U($"RewardItem{i}") != 0).Select(i => $"{snapshot!.Names.Item(q.U($"RewardItem{i}"))} ×{q.U($"RewardAmount{i}")}").ToList();
        var choices = Enumerable.Range(1, 6).Where(i => q.U($"RewardChoiceItemID{i}") != 0).Select(i => $"{snapshot!.Names.Item(q.U($"RewardChoiceItemID{i}"))} ×{q.U($"RewardChoiceItemQuantity{i}")}").ToList();
        if (choices.Count > 0)
        {
            Text("You will be able to choose one of these rewards:\n  " + string.Join("\n  ", choices), 13);
        }

        if (rewards.Count > 0)
        {
            Text("You will receive:\n  " + string.Join("\n  ", rewards), 13);
        }

        if (q.I("RewardMoney") != 0)
        {
            Text((q.I("RewardMoney") > 0 ? "Money: " : "Costs: ") + ClientStyle.Money(Math.Abs((long)q.I("RewardMoney"))), 13);
        }

        Text("Progress", 16, ClientStyle.Header);
        Text(request.T("CompletionText").Length > 0 ? request.T("CompletionText") : "(no progress text: the client skips this page)", 13, request.T("CompletionText").Length > 0 ? null : ClientStyle.Faded);
        Text("Completion", 16, ClientStyle.Header);
        Text(offer.T("RewardText"), 13);
        // Added directly: Text() would substitute the placeholders this note names.
        page.AddChild(ClientStyle.Text("Preview shows $N, $C and $R as <name>, <class>, <race> and uses the male $G form; conditions and scripts are not evaluated.", 11, ClientStyle.Faded));
    }
}
