using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The AzerothCore quest editor: the overview, objectives, rewards and preview pages over
/// quest_template (QuestLevel, RequiredNpcOrGo, RequiredItem, RewardMoney) and its addon tables. The
/// shared pages and the draft plumbing are in <see cref="QuestPanel"/>.
/// </summary>
public partial class AcQuestPanel : QuestPanel
{
    public override WorldDialect Dialect => WorldDialect.AzerothCore;

    static readonly (uint Value, string Name)[] XpTiers = [(0, "None"), (1, "Tiny"), (2, "Very small"), (3, "Small"), (4, "Below normal"), (5, "Normal"), (6, "Above normal"), (7, "Large"), (8, "Very large"), (9, "Huge")];
    static readonly (uint Value, string Name)[] RacePresets = [(0, "Everyone"), (AcQuestModel.AllianceRaces, "Alliance only"), (AcQuestModel.HordeRaces, "Horde only"), (1, "Specific races…")];

    protected override TableSchema Template => AcTables.QuestTemplate;
    protected override TableSchema Addon => AcTables.QuestAddon;
    protected override TableSchema Request => AcTables.QuestRequestItems;
    protected override TableSchema Offer => AcTables.QuestOfferReward;
    protected override TableSchema Starter => AcTables.QuestStarter;
    protected override TableSchema Ender => AcTables.QuestEnder;
    protected override TableSchema[] Tables => AcQuestModel.Tables;
    protected override Row[] NewQuestRows(uint id, string title) => AcQuestModel.NewQuest(id, title);

    protected override IEnumerable<InteractionIssue> Check(uint id, RowSet rows, IContentNames names) => AcQuestModel.Check(id, rows, names, ZoneExists);

    protected override string Subtitle(Row q) => $"Quest {Key} · level {q.I("QuestLevel")} · requires level {q.U("MinLevel")}";

    protected override void RenderObjectivesPage(Row q) => RenderObjectives(q);

    protected override void RenderOverviewPage(Row q) => RenderOverview(q);

    protected override void RenderRewardsPage(Row q) => RenderRewards(q);

    protected override void RenderPreviewPage(Row q) => RenderPreview(q);

    void RenderOverview(Row q)
    {
        Heading("Basics");
        Add("Title", EditorFields.Line(q.T("LogTitle"), text => Set(Template, "Rename quest", ("LogTitle", text.Trim())), "What the quest log calls it"));
        Add("Kind", EditorFields.Choice(QuestVocabulary.Types, q.U("QuestInfoID"), value => Set(Template, "Change quest kind", ("QuestInfoID", value))),
            ClientStyle.Tag("Group size"), UNumber(Template, "SuggestedGroupNum", "Change suggested group size", 50, "0 = solo"));
        Add("Availability", EditorFields.Choice(AcQuestModel.Methods, q.U("QuestType"), value => Set(Template, "Change quest availability", ("QuestType", value)), "Disabled quests are hidden; auto-complete quests finish on accept."));
        ZoneRow(q);

        Heading("Levels", "Quest level sets XP and the con colour; −1 scales with the player. Players outside required…maximum cannot accept it.");
        Add("Quest level", UNumber(Template, "QuestLevel", "Change quest level", 60),
            ClientStyle.Tag("Required"), UNumber(Template, "MinLevel", "Change required level", 60),
            ClientStyle.Tag("Maximum"), UNumber(Addon, "MaxLevel", "Change maximum level", 60, "0 = no maximum"));
        Add("Time limit", UNumber(Template, "TimeAllowed", "Change time limit", 80, "Seconds; 0 = untimed"), ClientStyle.Tag("seconds (0 = none)"));

        Heading("Who can take it");
        uint races = q.U("AllowableRaces");
        uint preset = races is 0 or AcQuestModel.AllianceRaces or AcQuestModel.HordeRaces ? races : 1;
        Add("Faction", EditorFields.Choice(RacePresets, preset, value => Set(Template, "Change allowed races", ("AllowableRaces", value == 1 ? AcQuestModel.AllianceRaces | AcQuestModel.HordeRaces : value))));
        if (preset == 1)
        {
            page.AddChild(EditorFields.Flags(AcQuestModel.Races, races, value => Set(Template, "Change allowed races", ("AllowableRaces", value)), 5));
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
            page.AddChild(EditorFields.Flags(AcQuestModel.Classes, classes, value => Set(Addon, "Change allowed classes", ("AllowableClasses", value)), 5));
        }

        Heading("Behavior");
        page.AddChild(EditorFields.Flags(AcQuestModel.Flags, q.U("Flags"), value => Set(Template, "Change quest options", ("Flags", value)), 3));
        page.AddChild(EditorFields.Flags(AcQuestModel.SpecialFlags, RowOf(Addon).U("SpecialFlags"), value => Set(Addon, "Change special options", ("SpecialFlags", value)), 3));
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
                row.AddChild(ButtonOf("Kill a creature…", () => Pick(SmartParamKind.Creature, "Choose creature to defeat", id => SetObjective(slot, (int)id))));
                row.AddChild(ButtonOf("Use an object…", () => Pick(SmartParamKind.GameObject, "Choose object to use", id => SetObjective(slot, -(int)id))));
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

    /// <summary>
    /// Client-style read-only rendering of the drafted quest, as the player would read it.
    /// </summary>
    void RenderPreview(Row q)
    {
        var request = RowOf(AcTables.QuestRequestItems);
        var offer = RowOf(AcTables.QuestOfferReward);
        void Text(string text, int size = 14, Color? color = null)
        {
            var label = ClientStyle.Text(InteractionText.Render(text), size, color ?? ClientStyle.Body);
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
