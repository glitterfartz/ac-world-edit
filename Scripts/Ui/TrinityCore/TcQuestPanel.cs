using System.Globalization;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The TrinityCore quest editor: the overview, objectives, rewards and preview pages over
/// quest_template (content tuning, 64-bit race masks, currencies), quest_template_addon and the
/// quest_objectives rows that replace AzerothCore's RequiredNpcOrGo / RequiredItem columns. The shared
/// pages and the draft plumbing are in <see cref="QuestPanel"/>.
/// </summary>
public partial class TcQuestPanel : QuestPanel
{
    public override WorldDialect Dialect => WorldDialect.TrinityCore;

    static readonly (uint Value, string Name)[] Tiers = [(0, "None"), (1, "Tiny"), (2, "Very small"), (3, "Small"), (4, "Below normal"), (5, "Normal"), (6, "Above normal"), (7, "Large"), (8, "Very large"), (9, "Huge")];
    static readonly (uint Value, string Name)[] RacePresets = [(0, "Everyone"), (1, "Alliance only"), (2, "Horde only"), (3, "Specific races…")];

    protected override TableSchema Template => TcTables.QuestTemplate;
    protected override TableSchema Addon => TcTables.QuestAddon;
    protected override TableSchema Request => TcTables.QuestRequestItems;
    protected override TableSchema Offer => TcTables.QuestOfferReward;
    protected override TableSchema Starter => TcTables.QuestStarter;
    protected override TableSchema Ender => TcTables.QuestEnder;
    protected override TableSchema ObjectStarter => TcTables.GameObjectQuestStarter;
    protected override TableSchema ObjectEnder => TcTables.GameObjectQuestEnder;
    protected override TableSchema[] Tables => TcQuestRules.Tables;
    protected override Row[] NewQuestRows(uint id, string title) => TcQuestRules.NewQuest(id, title);

    protected override IEnumerable<InteractionIssue> Check(uint id, RowSet rows, IContentNames names)
    {
        var client = Lookup();
        return TcQuestRules.Check(id, rows, names, ZoneExists, client == null ? null : client.Exists);
    }

    protected override string Subtitle(Row q) => $"Quest {Key} · content tuning {q.U("ContentTuningID")}";

    protected override Task<IReadOnlyList<(uint Id, string Name)>> SearchSpells(string term) =>
        Task.FromResult<IReadOnlyList<(uint Id, string Name)>>(Lookup()?.SearchSpells(term, 60) ?? []);

    protected override void ChainExtras() =>
        Add("Script", "Name of a registered C++ quest script that runs for this quest. Leave blank when there is none.",
            EditorFields.Line(RowOf(Addon).T("ScriptName"), text => Set(Addon, "Change quest script", ("ScriptName", text.Trim())), "Name of a registered quest script, or empty", 0, "Name of a registered C++ quest script, or empty."));

    protected override void RenderOverviewPage(Row q)
    {
        Heading("Basics");
        Add("Title", "The quest title shown in the quest log, on the giver's dialog and in search results.",
            EditorFields.Line(q.T("LogTitle"), text => Set(Template, "Rename quest", ("LogTitle", text.Trim())), "What the quest log calls it", 0, "The quest title shown in the quest log."));
        Add("Kind", "The quest category shown by the client, such as Dungeon, Raid, PvP or Profession.",
            EditorFields.Choice(QuestInfoChoices(), q.U("QuestInfoID"), value => Set(Template, "Change quest kind", ("QuestInfoID", value)), "The quest category shown by the client."),
            ClientStyle.Tag("Group size"), UNumber(Template, "SuggestedGroupNum", "Change suggested group size", 50, "The suggested group size shown in the log; 0 means solo."));
        Add("Availability", "Normal quests are offered when their requirements pass; disabled quests are hidden; auto-complete quests finish the moment they are accepted.",
            EditorFields.Choice(TcQuestRules.Methods, q.U("QuestType"), value => Set(Template, "Change quest availability", ("QuestType", value)), "Disabled quests are hidden; auto-complete quests finish on accept."));
        ZoneRow(q);

        Heading("Levels", "Content tuning sets the quest's level range and scaling (0 uses the map's or area's tuning). Players outside it, or above the maximum, cannot accept it.");
        Add("Content tuning", "ContentTuning.db2 ID that sets the quest's level range and scaling; 0 uses the map's or area's tuning.",
            UNumber(Template, "ContentTuningID", "Change content tuning", 80, "ContentTuning ID; 0 uses the map's or area's tuning."),
            ClientStyle.Tag("Maximum level"), UNumber(Addon, "MaxLevel", "Change maximum level", 60, "The maximum player level that can accept it; 0 means no maximum."));
        Add("Time limit", "How long the player has to finish the quest, in seconds. 0 means untimed.",
            UNumber(Template, "TimeAllowed", "Change time limit", 80, "Seconds allowed to finish; 0 means untimed."), ClientStyle.Tag("seconds (0 = none)"));
        Add("Expansion", "The expansion this quest belongs to. 'Not set' stores -1.",
            ExpansionChoice(q));

        Heading("Who can take it");
        ulong races = q.UL("AllowableRaces");
        uint preset = races is ulong.MaxValue or 0 ? 0u : races == TcQuestRules.AllianceRaces ? 1u : races == TcQuestRules.HordeRaces ? 2u : 3u;
        Add("Faction", "Which races can accept the quest: everyone, one faction, or a custom race mask.",
            EditorFields.Choice(RacePresets, preset, value => Set(Template, "Change allowed races", ("AllowableRaces", value switch
        {
            0 => TcQuestRules.AllRaces,
            1 => TcQuestRules.AllianceRaces,
            2 => TcQuestRules.HordeRaces,
            _ => races is ulong.MaxValue or 0 ? NamedRaceMask() : races
        })), "Which races can accept the quest."));
        if (preset == 3)
        {
            page.AddChild(EditorFields.Flags64(RaceBits(), races, value => Set(Template, "Change allowed races", ("AllowableRaces", value)), 4,
                "Tick each race that can accept the quest. The raw mask keeps any bits the list does not name."));
        }

        uint classes = RowOf(Addon).U("AllowableClasses");
        var allClasses = new CheckBox
        {
            Text = "All classes",
            ButtonPressed = classes == 0,
            TooltipText = "Tick to allow every class. Untick to choose specific classes below."
        };
        allClasses.Toggled += on => Set(Addon, "Change allowed classes", ("AllowableClasses", on ? 0u : 1u));
        Add("Classes", "Which classes can accept the quest. 'All classes' allows everyone.", allClasses);
        if (classes != 0)
        {
            page.AddChild(EditorFields.Flags(TcQuestRules.Classes, classes, value => Set(Addon, "Change allowed classes", ("AllowableClasses", value)), 5, "Tick each class that can accept the quest."));
        }

        Heading("Behavior");
        page.AddChild(EditorFields.Flags(TcQuestRules.Flags, q.U("Flags"), value => Set(Template, "Change quest options", ("Flags", value)), 3, "Quest behaviour flags: failure on death, party accept, sharing, hidden rewards, daily or weekly, auto-accept and more."));
        page.AddChild(EditorFields.Flags(TcQuestRules.SpecialFlags, RowOf(Addon).U("SpecialFlags"), value => Set(Addon, "Change special options", ("SpecialFlags", value)), 3, "Additional server flags: repeatable, auto-accept, Dungeon Finder, monthly, and quests that are not counted for Loremaster."));
        Add("More flags", "Extra client quest flag masks (FlagsEx, FlagsEx2 and FlagsEx3). The editor has no named bits for these yet, so the raw numbers are edited.",
            ClientStyle.Tag("Ex"), UNumber(Template, "FlagsEx", "Change extra flags", 80, "Extra client quest flags (FlagsEx); raw value."),
            ClientStyle.Tag("Ex2"), UNumber(Template, "FlagsEx2", "Change extra flags 2", 80, "More client quest flags (FlagsEx2); raw value."),
            ClientStyle.Tag("Ex3"), UNumber(Template, "FlagsEx3", "Change extra flags 3", 80, "Further client quest flags (FlagsEx3); raw value."));
    }

    IEnumerable<Row> ObjectivesOf() => effective.All(TcTables.QuestObjectives).Where(r => r.U("QuestID") == QuestId).OrderBy(r => r.U("Order")).ThenBy(r => r.U("ID"));

    protected override void RenderObjectivesPage(Row q)
    {
        Heading("Objectives", "What the player must do. Each objective fills one tracking slot; the log shows the description, or \"<name>: 0/N\" when it is empty.");
        var list = ObjectivesOf().ToList();
        foreach (var objective in list)
        {
            RenderObjective(objective);
        }

        if (list.Count == 0)
        {
            page.AddChild(ClientStyle.Text("No objectives: the quest completes as soon as the player talks to its ender.", 12, ClientStyle.Faded));
        }

        var addObjective = ButtonOf("+ Add objective", AddObjective);
        addObjective.TooltipText = "Add another tracking objective to this quest.";
        page.AddChild(addObjective);

        Heading("Quest drops", "Items this quest makes drop for players on it (the loot itself is set on the creature or object).");
        for (int i = 1; i <= 4; i++)
        {
            ItemSlot(q, $"{i}.", $"ItemDrop{i}", $"ItemDropQuantity{i}", "quest drop", "Change quest drop", "An item this quest makes drop for players on it. Set the loot itself on the creature or object.");
        }

        Heading("Given on accept", "An item the giver hands over when the quest is accepted.");
        uint startItem = q.U("StartItem");
        if (startItem == 0)
        {
            Add("Starting item", "An item handed to the player when the quest is accepted.",
                ButtonOf("Add starting item…", () => ChooseItem("Choose starting item", (id, _) => Set(Template, "Change starting item", ("StartItem", id)))));
        }
        else
        {
            Add("Starting item", "An item handed to the player when the quest is accepted.",
                ItemReference(startItem, "starting item", () => ChooseItem("Choose starting item",
                    (id, _) => Set(Template, "Change starting item", ("StartItem", id)), startItem),
                    () => Set(Template, "Clear starting item", ("StartItem", 0)), "The item handed over on accept."),
                ClientStyle.Tag("×"), UNumber(Addon, "ProvidedItemCount", "Change starting item count", 50, "How many starting items are given."));
        }

        Add("Cast on accept", "A spell cast on the player when the quest is accepted.",
            SpellReference(Addon, "SourceSpellID", "spell cast on accept", "Change spell cast on accept", "The spell cast on the player on accept. 0 means none."));
    }

    void RenderObjective(Row objective)
    {
        string key = objective.U("ID").ToString(CultureInfo.InvariantCulture);
        uint type = objective.U("Type");
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        var head = new HBoxContainer();
        head.AddChild(ClientStyle.Tag($"#{objective.I("StorageIndex")}"));
        head.AddChild(EditorFields.Choice(TcQuestRules.ObjectiveTypes, type, value => SetObjective(objective, "Change objective type", ("Type", value), ("ObjectID", 0)), "What the player must do for this objective, such as kill a creature, use an object, collect an item or learn a spell."));
        head.AddChild(ClientStyle.Tag("×"));
        head.AddChild(EditorFields.Number(objective.I("Amount"), amount => SetObjective(objective, "Change objective amount", ("Amount", (int)amount)), 60, "How many times the objective must be completed."));
        head.AddChild(ButtonOf("Remove", () => Commit("Remove objective", new RowEdit(TcTables.QuestObjectives.Name, key, objective, null))));
        EditorFields.Tip((Control)head.GetChild(0), "The tracking slot this objective fills in the quest log.");
        EditorFields.Tip((Control)head.GetChild(4), "Remove this objective from the quest.");
        box.AddChild(head);
        box.AddChild(EditorFields.Row("  target", "The creature, object, item or spell this objective refers to; the picker depends on the objective type.", ObjectTarget(objective, type)));
        box.AddChild(EditorFields.Row("  log text", "Optional quest-log text. Empty shows the default \"<name>: 0/N\" line.",
            EditorFields.Line(objective.T("Description"), text => SetObjective(objective, "Edit objective text", ("Description", text)), "Optional, e.g. \"Kobolds driven off\"", 0, "Optional quest-log text. Empty shows the default \"<name>: 0/N\" line.")));
        box.AddChild(EditorFields.Flags(TcQuestRules.ObjectiveFlags, objective.U("Flags"), value => SetObjective(objective, "Change objective options", ("Flags", value)), 3, "Objective tracking flags. The raw value keeps every bit."));
        box.AddChild(new HSeparator());
        page.AddChild(box);
    }

    /// <summary>The picker for what the objective's ObjectID names, by objective type.</summary>
    Control ObjectTarget(Row objective, uint type)
    {
        uint id = (uint)Math.Max(objective.I("ObjectID"), 0);
        void Choose(uint picked) => SetObjective(objective, "Change objective target", ("ObjectID", (int)picked));
        Action? clear = id == 0 ? null : () => SetObjective(objective, "Clear objective target", ("ObjectID", 0));
        if (type == TcQuestRules.Item)
        {
            return ItemReference(id, "item", () => ChooseItem("Choose item", (picked, _) => Choose(picked), id), clear, "The item this objective counts.");
        }

        if (type == TcQuestRules.LearnSpell)
        {
            return EditorFields.Reference(id == 0 ? "Choose spell…" : SpellNameOf(id) is { Length: > 0 } spell ? $"{spell}  ·  {id}" : $"Missing #{id}",
                () => ChooseSpell("Choose spell", Choose, id), clear, id != 0 && SpellNameOf(id) == null,
                "The spell the player must learn.", IconOfPath(SpellIconOf(id)), SpellTipOf(id));
        }

        if (type is TcQuestRules.Currency or TcQuestRules.HaveCurrency or TcQuestRules.ObtainCurrency)
        {
            return CurrencyReferenceFor(id, clear, "The currency this objective counts.", Choose);
        }

        if (type is TcQuestRules.MinReputation or TcQuestRules.MaxReputation or TcQuestRules.IncreaseReputation)
        {
            return EditorFields.Reference(id == 0 ? "Choose faction…" : FactionNameOf(id) is { Length: > 0 } faction ? $"{faction}  ·  {id}" : $"Missing #{id}",
                () => ChooseFaction("Choose faction", Choose, id), clear, id != 0 && FactionNameOf(id) == null,
                "The reputation faction this objective counts.", null, FactionTip(id));
        }

        SmartParamKind? kind = type switch
        {
            TcQuestRules.Monster or TcQuestRules.TalkTo => SmartParamKind.Creature,
            TcQuestRules.GameObject => SmartParamKind.GameObject,
            _ => null
        };
        if (kind is not { } known)
        {
            // Area triggers and the rest are IDs of tables the editor has no search for.
            return EditorFields.Number(objective.I("ObjectID"), value => SetObjective(objective, "Change objective target", ("ObjectID", (int)value)), 100, "The ID in the table this objective type refers to (an area trigger, for example).");
        }

        string what = known == SmartParamKind.Creature ? "creature" : "object";
        return EditorFields.Reference(Named(known, id, $"Choose {what}…"), () => Pick(known, $"Choose {what}", Choose), clear, Missing(known, id), $"The {what} this objective counts.");
    }

    /// <summary>A currency reference for a quest_objectives row, whose target is an action rather than a table column.</summary>
    Control CurrencyReferenceFor(uint id, Action? clear, string tooltip, Action<uint> choose) =>
        EditorFields.Reference(id == 0 ? "Choose currency…" : CurrencyNameOf(id) is { Length: > 0 } name ? $"{name}  ·  {id}" : $"Missing #{id}",
            () => ChooseCurrency("Choose currency", choose, id), clear, id != 0 && CurrencyNameOf(id) == null,
            tooltip, IconOfPath(CurrencyIconOf(id)), CurrencyTipOf(id));

    bool SetObjective(Row objective, string label, params (string Column, object Value)[] values)
    {
        var row = objective;
        foreach (var (column, value) in values)
        {
            row = row.With(column, value);
        }

        return row == objective || Commit(label, new RowEdit(TcTables.QuestObjectives.Name, objective.U("ID").ToString(CultureInfo.InvariantCulture), objective, row));
    }

    /// <summary>
    /// The next objective ID: quest_objectives IDs are global, so it is above the database maximum and
    /// every ID the draft has reserved.
    /// </summary>
    uint NextObjectiveId() => Math.Max(snapshot!.MaxObjectiveId, Drafts.Draft?.MaxReserved(sourceKey, TcTables.QuestObjectives, "ID") ?? 0) + 1;

    void AddObjective()
    {
        if (snapshot == null || QuestId is not uint quest)
        {
            return;
        }

        var existing = ObjectivesOf().ToList();
        int slot = Enumerable.Range(0, 24).FirstOrDefault(i => existing.All(o => o.I("StorageIndex") != i), -1);
        if (slot < 0)
        {
            status = "A quest has at most 24 objectives.";
            UpdateStatus();
            return;
        }

        uint id = NextObjectiveId();
        var row = TcQuestRules.NewObjective(id, quest, slot, existing.Count == 0 ? 0 : existing.Max(o => o.U("Order")) + 1);
        Commit("Add objective", new RowEdit(TcTables.QuestObjectives.Name, id.ToString(CultureInfo.InvariantCulture), null, row));
    }

    protected override IEnumerable<RowEdit> CopyRelated(uint from, uint id, ContentSnapshot probe, RowSet view)
    {
        uint next = Math.Max(probe.MaxObjectiveId, Drafts.Draft?.MaxReserved(sourceKey, TcTables.QuestObjectives, "ID") ?? 0);
        foreach (var objective in view.All(TcTables.QuestObjectives).Where(r => r.U("QuestID") == from).OrderBy(r => r.U("Order")).ThenBy(r => r.U("ID")))
        {
            next++;
            yield return new RowEdit(TcTables.QuestObjectives.Name, next.ToString(CultureInfo.InvariantCulture), null, objective.With("ID", next).With("QuestID", id));
        }
    }

    protected override void RenderRewardsPage(Row q)
    {
        Heading("Money and experience");
        long bonus = q.U("RewardBonusMoney");
        void SetBonus(long gold, long silver, long copper) =>
            Set(Template, "Change max-level money", ("RewardBonusMoney", (uint)Math.Clamp(gold * 10000 + silver * 100 + copper, 0, uint.MaxValue)));

        Add("Experience", "The experience tier, scaled by quest level through QuestXP.db2, then multiplied by the second value.",
            EditorFields.Choice(Tiers, q.U("RewardXPDifficulty"), value => Set(Template, "Change experience reward", ("RewardXPDifficulty", value)), "The experience tier, scaled by quest level (QuestXP.db2)."),
            ClientStyle.Tag("×"), EditorFields.Line(q.T("RewardXPMultiplier"), text => SetMultiplier("RewardXPMultiplier", "Change experience multiplier", text), "Multiplies the experience; 1 = as the tier says", 0, "Multiplies the experience from the tier; 1 keeps it unchanged."));
        Add("Money", "The money tier, scaled by quest level through QuestMoneyReward.db2, then multiplied by the second value.",
            EditorFields.Choice(Tiers, q.U("RewardMoneyDifficulty"), value => Set(Template, "Change money reward", ("RewardMoneyDifficulty", value)), "The money tier, scaled by quest level (QuestMoneyReward.db2)."),
            ClientStyle.Tag("×"), EditorFields.Line(q.T("RewardMoneyMultiplier"), text => SetMultiplier("RewardMoneyMultiplier", "Change money multiplier", text), "Multiplies the money; 1 = as the tier says", 0, "Multiplies the money from the tier; 1 keeps it unchanged."));
        Add("Extra at max level", "Extra money awarded when the player is at maximum level, in gold, silver and copper.",
            EditorFields.Number(bonus / 10000, g => SetBonus(g, bonus / 100 % 100, bonus % 100), 60, "Extra gold at maximum level."), ClientStyle.Tag("g"),
            EditorFields.Number(bonus / 100 % 100, s => SetBonus(bonus / 10000, s, bonus % 100), 40, "Extra silver at maximum level."), ClientStyle.Tag("s"),
            EditorFields.Number(bonus % 100, c => SetBonus(bonus / 10000, bonus / 100 % 100, c), 40, "Extra copper at maximum level."), ClientStyle.Tag("c"));

        Heading("Always given", "Every player receives these items.");
        for (int i = 1; i <= 4; i++)
        {
            ItemSlot(q, $"{i}.", $"RewardItem{i}", $"RewardAmount{i}", "reward item", "Change reward item", "An item every player receives when the quest is turned in.");
        }

        Heading("Choose one of", "The player picks one item from this list.");
        for (int i = 1; i <= 6; i++)
        {
            ItemSlot(q, $"{i}.", $"RewardChoiceItemID{i}", $"RewardChoiceItemQuantity{i}", "choice", "Change choice reward", "One of the items the player can choose from on the reward screen.");
        }

        Heading("Currencies", "Currencies the quest awards, with the client's icon and description.");
        for (int i = 1; i <= 4; i++)
        {
            Add($"{i}.", "A currency the quest awards.",
                CurrencyReference(Template, $"RewardCurrencyID{i}", "Change reward currency", "The currency awarded."),
                ClientStyle.Tag("×"), UNumber(Template, $"RewardCurrencyQty{i}", "Change reward currency amount", 60, "How much of the currency is awarded."));
        }

        Heading("Reputation", "Standard tier (1–9) uses the client's reward table; an exact amount overrides it.");
        for (int i = 1; i <= 5; i++)
        {
            if (q.U($"RewardFactionID{i}") == 0 && i > 1 && q.U($"RewardFactionID{i - 1}") == 0)
            {
                continue;
            }

            int slot = i;
            Add($"{slot}.", "A faction whose reputation the quest rewards.",
                FactionRef(Template, $"RewardFactionID{slot}", "Change reputation reward", "The faction whose reputation is rewarded."),
                ClientStyle.Tag("tier"), EditorFields.Number(q.I($"RewardFactionValue{slot}"), value => Set(Template, "Change reputation tier", ($"RewardFactionValue{slot}", (int)value)), 40, "Standard reputation tier from the client's reward table (1-9)."),
                ClientStyle.Tag("or exact"), EditorFields.Number(q.I($"RewardFactionOverride{slot}") / 100, value => Set(Template, "Change reputation amount", ($"RewardFactionOverride{slot}", (int)value * 100)), 60, "An exact reputation amount that overrides the tier, in reputation points."));
        }

        Heading("Other rewards");
        Add("Spell cast on player", "A spell cast on the player when the quest is turned in.",
            SpellReference(Template, "RewardSpell", "spell cast on the player", "Change reward spell", "The spell cast on the player on turn-in. 0 means none."));
        for (int i = 1; i <= 3; i++)
        {
            Add(i == 1 ? "Spells shown as reward" : "", "Spells shown on the reward screen as teasers; they are not actually cast.",
                SpellReference(Template, $"RewardDisplaySpell{i}", "displayed reward spell", "Change displayed spell", "A spell shown on the reward screen. 0 means none."));
        }

        Add("Honor", "Other rewards: honor points and arena points.",
            UNumber(Template, "RewardHonor", "Change honor reward", 60, "Honor points awarded."), ClientStyle.Tag("Arena points"), UNumber(Template, "RewardArenaPoints", "Change arena reward", 60, "Arena points awarded."));
        Add("Title", "A character title the quest awards. 0 means no title.", TitleReference(Template, "RewardTitle", "Change title reward", "The character title awarded by this quest."));
        Add("Skill", "A profession skill the quest rewards, and how many skill-ups it gives.",
            SkillReference(Template, "RewardSkillLineID", "Change skill reward", "The skill whose rank is increased."), ClientStyle.Tag("skill-ups"), UNumber(Template, "RewardNumSkillUps", "Change skill-ups", 40, "How many skill points are awarded."));
        Add("Mail afterwards", "Mail sent to the player after the quest is turned in.",
            MailReference(Addon, "RewardMailTemplateID", "Change reward mail", "The mail template sent after turn-in; 0 means no mail."),
            ClientStyle.Tag("after"), UNumber(Addon, "RewardMailDelay", "Change mail delay", 70, "Seconds after turn-in before the mail arrives."), ClientStyle.Tag("seconds"));
    }

    void SetMultiplier(string column, string label, string text)
    {
        if (float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && value >= 0 && float.IsFinite(value))
        {
            Set(Template, label, (column, value));
        }
        else
        {
            status = "A multiplier is a number, 0 or more.";
            UpdateStatus();
        }
    }

    /// <summary>Client-style read-only rendering of the drafted quest, as the player would read it.</summary>
    protected override void RenderPreviewPage(Row q)
    {
        var request = RowOf(Request);
        var offer = RowOf(Offer);
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
        foreach (var objective in ObjectivesOf().Where(o => o.U("Flags") is var flags && (flags & 0x8) == 0))
        {
            uint target = (uint)Math.Max(objective.I("ObjectID"), 0);
            string name = objective.T("Description") is { Length: > 0 } custom ? custom : objective.U("Type") switch
            {
                TcQuestRules.Monster => (snapshot!.Names.Creature(target) ?? "?") + " slain",
                TcQuestRules.TalkTo => "Speak to " + (snapshot!.Names.Creature(target) ?? "?"),
                TcQuestRules.GameObject => snapshot!.Names.GameObject(target) ?? "?",
                TcQuestRules.Item => snapshot!.Names.Item(target) ?? "?",
                var other => TcQuestRules.TypeName(other)
            };
            Text($"  {name}: 0/{objective.I("Amount")}", 13);
        }

        Text("Rewards", 16, ClientStyle.Header);
        var rewards = Enumerable.Range(1, 4).Where(i => q.U($"RewardItem{i}") != 0).Select(i => $"{snapshot!.Names.Item(q.U($"RewardItem{i}")) ?? "?"} ×{q.U($"RewardAmount{i}")}").ToList();
        var choices = Enumerable.Range(1, 6).Where(i => q.U($"RewardChoiceItemID{i}") != 0).Select(i => $"{snapshot!.Names.Item(q.U($"RewardChoiceItemID{i}")) ?? "?"} ×{q.U($"RewardChoiceItemQuantity{i}")}").ToList();
        if (choices.Count > 0)
        {
            Text("You will be able to choose one of these rewards:\n  " + string.Join("\n  ", choices), 13);
        }

        if (rewards.Count > 0)
        {
            Text("You will receive:\n  " + string.Join("\n  ", rewards), 13);
        }

        if (q.U("RewardBonusMoney") != 0)
        {
            Text("Money at maximum level: " + ClientStyle.Money(q.U("RewardBonusMoney")), 13);
        }

        Text("Progress", 16, ClientStyle.Header);
        Text(request.T("CompletionText").Length > 0 ? request.T("CompletionText") : "(no progress text: the client skips this page)", 13, request.T("CompletionText").Length > 0 ? null : ClientStyle.Faded);
        Text("Completion", 16, ClientStyle.Header);
        Text(offer.T("RewardText"), 13);
        // Added directly: Text() would substitute the placeholders this note names.
        page.AddChild(ClientStyle.Text("Preview shows $N, $C and $R as <name>, <class>, <race> and uses the male $G form; conditions and scripts are not evaluated.", 11, ClientStyle.Faded));
    }
}
