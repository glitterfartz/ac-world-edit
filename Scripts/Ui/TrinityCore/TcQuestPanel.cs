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
    static readonly (uint Value, string Name)[] Tiers = [(0, "None"), (1, "Tiny"), (2, "Very small"), (3, "Small"), (4, "Below normal"), (5, "Normal"), (6, "Above normal"), (7, "Large"), (8, "Very large"), (9, "Huge")];
    static readonly (uint Value, string Name)[] RacePresets = [(0, "Everyone"), (1, "Alliance only"), (2, "Horde only"), (3, "Specific races (mask)…")];

    protected override TableSchema Template => TcTables.QuestTemplate;
    protected override TableSchema Addon => TcTables.QuestAddon;
    protected override TableSchema Request => TcTables.QuestRequestItems;
    protected override TableSchema Offer => TcTables.QuestOfferReward;
    protected override TableSchema Starter => TcTables.QuestStarter;
    protected override TableSchema Ender => TcTables.QuestEnder;
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
        Add("Script", EditorFields.Line(RowOf(Addon).T("ScriptName"), text => Set(Addon, "Change quest script", ("ScriptName", text.Trim())), "Name of a registered quest script, or empty"));

    protected override void RenderOverviewPage(Row q)
    {
        Heading("Basics");
        Add("Title", EditorFields.Line(q.T("LogTitle"), text => Set(Template, "Rename quest", ("LogTitle", text.Trim())), "What the quest log calls it"));
        Add("Kind", EditorFields.Choice(QuestModel.Types, q.U("QuestInfoID"), value => Set(Template, "Change quest kind", ("QuestInfoID", value))),
            ClientStyle.Tag("Group size"), UNumber(Template, "SuggestedGroupNum", "Change suggested group size", 50, "0 = solo"));
        Add("Availability", EditorFields.Choice(TcQuestRules.Methods, q.U("QuestType"), value => Set(Template, "Change quest availability", ("QuestType", value)), "Disabled quests are hidden; auto-complete quests finish on accept."));
        ZoneRow(q);

        Heading("Levels", "Content tuning sets the quest's level range and scaling (0 uses the map's or area's tuning). Players outside it, or above the maximum, cannot accept it.");
        Add("Content tuning", UNumber(Template, "ContentTuningID", "Change content tuning", 80, "ContentTuning ID; 0 = the map's or area's"),
            ClientStyle.Tag("Maximum level"), UNumber(Addon, "MaxLevel", "Change maximum level", 60, "0 = no maximum"));
        Add("Time limit", UNumber(Template, "TimeAllowed", "Change time limit", 80, "Seconds; 0 = untimed"), ClientStyle.Tag("seconds (0 = none)"));
        Add("Expansion", UNumber(Template, "Expansion", "Change expansion", 60, "Expansion the quest belongs to; -1 = unset"));

        Heading("Who can take it");
        ulong races = q.UL("AllowableRaces");
        uint preset = races is ulong.MaxValue or 0 ? 0u : races == TcQuestRules.AllianceRaces ? 1u : races == TcQuestRules.HordeRaces ? 2u : 3u;
        Add("Faction", EditorFields.Choice(RacePresets, preset, value => Set(Template, "Change allowed races", ("AllowableRaces", value switch
        {
            0 => TcQuestRules.AllRaces,
            1 => TcQuestRules.AllianceRaces,
            2 => TcQuestRules.HordeRaces,
            _ => races is ulong.MaxValue or 0 ? TcQuestRules.AllianceRaces | TcQuestRules.HordeRaces : races
        }))));
        if (preset == 3)
        {
            Add("Race mask", EditorFields.Line(races.ToString(CultureInfo.InvariantCulture), text =>
            {
                if (ulong.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong mask))
                {
                    Set(Template, "Change allowed races", ("AllowableRaces", mask));
                }
                else
                {
                    status = "The race mask is a whole number, one bit per race ID.";
                    UpdateStatus();
                }
            }, "One bit per ChrRaces ID (1 = Human, 2 = Orc, 4 = Dwarf, ...)"));
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
            page.AddChild(EditorFields.Flags(TcQuestRules.Classes, classes, value => Set(Addon, "Change allowed classes", ("AllowableClasses", value)), 5));
        }

        Heading("Behavior");
        page.AddChild(EditorFields.Flags(TcQuestRules.Flags, q.U("Flags"), value => Set(Template, "Change quest options", ("Flags", value)), 3));
        page.AddChild(EditorFields.Flags(TcQuestRules.SpecialFlags, RowOf(Addon).U("SpecialFlags"), value => Set(Addon, "Change special options", ("SpecialFlags", value)), 3));
        Add("More flags", ClientStyle.Tag("Ex"), UNumber(Template, "FlagsEx", "Change extra flags", 80), ClientStyle.Tag("Ex2"), UNumber(Template, "FlagsEx2", "Change extra flags 2", 80), ClientStyle.Tag("Ex3"), UNumber(Template, "FlagsEx3", "Change extra flags 3", 80));
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

        page.AddChild(ButtonOf("+ Add objective", AddObjective));

        Heading("Quest drops", "Items this quest makes drop for players on it (the loot itself is set on the creature or object).");
        for (int i = 1; i <= 4; i++)
        {
            ItemSlot(q, $"{i}.", $"ItemDrop{i}", $"ItemDropQuantity{i}", "quest drop", "Change quest drop");
        }

        Heading("Given on accept", "An item the giver hands over when the quest is accepted.");
        Add("Starting item", Ref(Template, "StartItem", SmartParamKind.Item, "starting item", "Change starting item"), ClientStyle.Tag("×"), UNumber(Addon, "ProvidedItemCount", "Change starting item count", 50));
        Add("Cast on accept", Ref(Addon, "SourceSpellID", SmartParamKind.Spell, "spell cast on accept", "Change spell cast on accept"));
    }

    void RenderObjective(Row objective)
    {
        string key = objective.U("ID").ToString(CultureInfo.InvariantCulture);
        uint type = objective.U("Type");
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        var head = new HBoxContainer();
        head.AddChild(ClientStyle.Tag($"#{objective.I("StorageIndex")}"));
        head.AddChild(EditorFields.Choice(TcQuestRules.ObjectiveTypes, type, value => SetObjective(objective, "Change objective type", ("Type", value), ("ObjectID", 0))));
        head.AddChild(ClientStyle.Tag("×"));
        head.AddChild(EditorFields.Number(objective.I("Amount"), amount => SetObjective(objective, "Change objective amount", ("Amount", (int)amount)), 60));
        head.AddChild(ButtonOf("Remove", () => Commit("Remove objective", new RowEdit(TcTables.QuestObjectives.Name, key, objective, null))));
        box.AddChild(head);
        box.AddChild(EditorFields.Row("  target", ObjectTarget(objective, type)));
        box.AddChild(EditorFields.Row("  log text", EditorFields.Line(objective.T("Description"), text => SetObjective(objective, "Edit objective text", ("Description", text)), "Optional, e.g. \"Kobolds driven off\"")));
        box.AddChild(EditorFields.Flags(TcQuestRules.ObjectiveFlags, objective.U("Flags"), value => SetObjective(objective, "Change objective options", ("Flags", value)), 3));
        box.AddChild(new HSeparator());
        page.AddChild(box);
    }

    /// <summary>The picker for what the objective's ObjectID names, by objective type.</summary>
    Control ObjectTarget(Row objective, uint type)
    {
        uint id = (uint)Math.Max(objective.I("ObjectID"), 0);
        SmartParamKind? kind = type switch
        {
            TcQuestRules.Monster or TcQuestRules.TalkTo => SmartParamKind.Creature,
            TcQuestRules.GameObject => SmartParamKind.GameObject,
            TcQuestRules.Item => SmartParamKind.Item,
            TcQuestRules.LearnSpell => SmartParamKind.Spell,
            _ => null
        };
        if (kind is not { } known)
        {
            // Currencies, reputations, area triggers and the rest are IDs of tables the editor has no search for.
            return EditorFields.Number(objective.I("ObjectID"), value => SetObjective(objective, "Change objective target", ("ObjectID", (int)value)), 100, "The ID in the table this objective type refers to");
        }

        string what = known switch
        {
            SmartParamKind.Creature => "creature",
            SmartParamKind.GameObject => "object",
            SmartParamKind.Item => "item",
            _ => "spell"
        };
        return EditorFields.Reference(Named(known, id, $"Choose {what}…"), () =>
        {
            if (known == SmartParamKind.Spell)
            {
                PickSpell("Choose spell", picked => SetObjective(objective, "Change objective target", ("ObjectID", (int)picked)));
            }
            else
            {
                Pick(known, $"Choose {what}", picked => SetObjective(objective, "Change objective target", ("ObjectID", (int)picked)));
            }
        }, id == 0 ? null : () => SetObjective(objective, "Clear objective target", ("ObjectID", 0)), Missing(known, id));
    }

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

        Add("Experience", EditorFields.Choice(Tiers, q.U("RewardXPDifficulty"), value => Set(Template, "Change experience reward", ("RewardXPDifficulty", value)), "Scaled by quest level (QuestXP.db2)"),
            ClientStyle.Tag("×"), EditorFields.Line(q.T("RewardXPMultiplier"), text => SetMultiplier("RewardXPMultiplier", "Change experience multiplier", text), "Multiplies the experience; 1 = as the tier says"));
        Add("Money", EditorFields.Choice(Tiers, q.U("RewardMoneyDifficulty"), value => Set(Template, "Change money reward", ("RewardMoneyDifficulty", value)), "Scaled by quest level (QuestMoneyReward.db2)"),
            ClientStyle.Tag("×"), EditorFields.Line(q.T("RewardMoneyMultiplier"), text => SetMultiplier("RewardMoneyMultiplier", "Change money multiplier", text), "Multiplies the money; 1 = as the tier says"));
        Add("Extra at max level",
            EditorFields.Number(bonus / 10000, g => SetBonus(g, bonus / 100 % 100, bonus % 100), 60), ClientStyle.Tag("g"),
            EditorFields.Number(bonus / 100 % 100, s => SetBonus(bonus / 10000, s, bonus % 100), 40), ClientStyle.Tag("s"),
            EditorFields.Number(bonus % 100, c => SetBonus(bonus / 10000, bonus / 100 % 100, c), 40), ClientStyle.Tag("c"));

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

        Heading("Currencies", "Currency IDs (CurrencyTypes); the client checks that each exists.");
        for (int i = 1; i <= 4; i++)
        {
            Add($"{i}.", UNumber(Template, $"RewardCurrencyID{i}", "Change reward currency", 80), ClientStyle.Tag("×"), UNumber(Template, $"RewardCurrencyQty{i}", "Change reward currency amount", 60));
        }

        Heading("Reputation", "Standard tier (1–9) uses the client's reward table; an exact amount overrides it.");
        for (int i = 1; i <= 5; i++)
        {
            if (q.U($"RewardFactionID{i}") == 0 && i > 1 && q.U($"RewardFactionID{i - 1}") == 0)
            {
                continue;
            }

            int slot = i;
            Add($"{slot}.", ClientStyle.Tag("Faction"), UNumber(Template, $"RewardFactionID{slot}", "Change reputation reward", 70),
                ClientStyle.Tag("tier"), EditorFields.Number(q.I($"RewardFactionValue{slot}"), value => Set(Template, "Change reputation tier", ($"RewardFactionValue{slot}", (int)value)), 40),
                ClientStyle.Tag("or exact"), EditorFields.Number(q.I($"RewardFactionOverride{slot}") / 100, value => Set(Template, "Change reputation amount", ($"RewardFactionOverride{slot}", (int)value * 100)), 60));
        }

        Heading("Other rewards");
        Add("Spell cast on player", Ref(Template, "RewardSpell", SmartParamKind.Spell, "spell cast on the player", "Change reward spell"));
        for (int i = 1; i <= 3; i++)
        {
            Add(i == 1 ? "Spells shown as reward" : "", Ref(Template, $"RewardDisplaySpell{i}", SmartParamKind.Spell, "displayed reward spell", "Change displayed spell"));
        }

        Add("Honor", UNumber(Template, "RewardHonor", "Change honor reward", 60), ClientStyle.Tag("Arena points"), UNumber(Template, "RewardArenaPoints", "Change arena reward", 60),
            ClientStyle.Tag("Title ID"), UNumber(Template, "RewardTitle", "Change title reward", 50));
        Add("Skill", UNumber(Template, "RewardSkillLineID", "Change skill reward", 60, "SkillLine ID"), ClientStyle.Tag("skill-ups"), UNumber(Template, "RewardNumSkillUps", "Change skill-ups", 40));
        Add("Mail afterwards", UNumber(Addon, "RewardMailTemplateID", "Change reward mail", 70, "Mail template ID; 0 = none"), ClientStyle.Tag("after"), UNumber(Addon, "RewardMailDelay", "Change mail delay", 70), ClientStyle.Tag("seconds"));
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
            var label = ClientStyle.Text(NpcInteraction.Render(text), size, color ?? ClientStyle.Body);
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
