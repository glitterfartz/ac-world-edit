namespace ACWorldEdit.Foundation;

/// <summary>
/// TrinityCore quest vocabulary (QuestDef.h) and the loader's checks over a quest's rows
/// (ObjectMgr::LoadQuests). Errors are what the server rejects or cannot complete; warnings are
/// suspicious but loadable. Engine- and database-independent.
/// </summary>
public static class TcQuestRules
{
    // quest_objectives.Type (QuestObjectiveType).
    public const uint Monster = 0, Item = 1, GameObject = 2, TalkTo = 3, Currency = 4, LearnSpell = 5, MinReputation = 6, MaxReputation = 7,
        Money = 8, PlayerKills = 9, AreaTrigger = 10, WinPetBattleAgainstNpc = 11, DefeatBattlePet = 12, WinPvpPetBattles = 13, CriteriaTree = 14,
        ProgressBar = 15, HaveCurrency = 16, ObtainCurrency = 17, IncreaseReputation = 18, AreaTriggerEnter = 19, AreaTriggerExit = 20, KillWithLabel = 21;

    public static readonly (uint Value, string Name)[] ObjectiveTypes =
    [
        (Monster, "Kill a creature"), (Item, "Collect an item"), (GameObject, "Use an object"), (TalkTo, "Talk to a creature"), (Currency, "Spend currency"),
        (LearnSpell, "Learn a spell"), (MinReputation, "Reach a reputation"), (MaxReputation, "Stay below a reputation"), (Money, "Hold money"),
        (PlayerKills, "Defeat players"), (AreaTrigger, "Enter an area"), (WinPetBattleAgainstNpc, "Win a pet battle against a creature"),
        (DefeatBattlePet, "Defeat a battle pet"), (WinPvpPetBattles, "Win PvP pet battles"), (CriteriaTree, "Complete a criteria tree"),
        (ProgressBar, "Fill a progress bar"), (HaveCurrency, "Hold currency"), (ObtainCurrency, "Obtain currency"), (IncreaseReputation, "Gain reputation"),
        (AreaTriggerEnter, "Enter an area trigger"), (AreaTriggerExit, "Leave an area trigger"), (KillWithLabel, "Kill creatures with a label")
    ];

    /// <summary>quest_objectives.Flags (QuestObjectiveFlags).</summary>
    public static readonly (uint Bit, string Name)[] ObjectiveFlags =
    [
        (0x1, "Tracked on minimap"), (0x2, "Sequenced"), (0x4, "Optional"), (0x8, "Hidden"), (0x10, "Hide credit message"),
        (0x20, "Keep quest items"), (0x40, "Part of progress bar"), (0x80, "Kill players of the same faction"), (0x100, "Progress not shared"), (0x200, "Ignore solo players")
    ];

    /// <summary>quest_template.QuestType.</summary>
    public static readonly (uint Value, string Name)[] Methods = [(2, "Normal"), (0, "Auto-complete"), (1, "Disabled"), (3, "World quest")];

    /// <summary>quest_template.Flags (QuestFlags) as TrinityCore documents them.</summary>
    public static readonly (uint Bit, string Name)[] Flags =
    [
        (0x1, "Fails on death"), (0x2, "Party accept"), (0x8, "Shareable"), (0x40, "Raid"), (0x200, "Hidden rewards"), (0x400, "Tracking (auto-rewarded, hidden)"),
        (0x1000, "Daily"), (0x2000, "Forces PvP"), (0x4000, "Unavailable"), (0x8000, "Weekly"), (0x10000, "Auto-complete"), (0x20000, "Usable item in tracker"),
        (0x40000, "Objective text as completion text"), (0x80000, "Auto-accept")
    ];

    /// <summary>quest_template_addon.SpecialFlags (QuestSpecialFlags).</summary>
    public static readonly (uint Bit, string Name)[] SpecialFlags = [(0x1, "Repeatable"), (0x2, "Completed by exploration or event"), (0x4, "Auto-accept"), (0x8, "Dungeon Finder quest"), (0x10, "Monthly"), (0x20, "Credit by spell, not kill"), (0x40, "No reputation spillover"), (0x80, "Can fail in any state"), (0x100, "Not counted for Loremaster")];

    /// <summary>The five per-quest tables keyed by ID, in the order <see cref="NewQuest"/> returns their rows.</summary>
    public static readonly TableSchema[] Tables = [TcTables.QuestTemplate, TcTables.QuestAddon, TcTables.QuestRequestItems, TcTables.QuestOfferReward];

    public static string TypeName(uint type) => ObjectiveTypes.Where(t => t.Value == type).Select(t => t.Name).FirstOrDefault() ?? $"Type {type}";

    /// <summary>Complete default rows for a new quest (all whitelisted columns), ready to insert.</summary>
    public static Row[] NewQuest(uint id, string title)
    {
        Row Defaults(TableSchema table) => new(table.Columns.Select(c => new KeyValuePair<string, string>(c.Name, c.Name == table.Key[0] ? id.ToString() : c.Kind == ColumnKind.Text ? "" : "0")));
        var template = Defaults(TcTables.QuestTemplate).With("QuestType", 2u).With("LogTitle", title).With("RewardXPDifficulty", 1u).With("Flags", 0x8u);
        return [template, Defaults(TcTables.QuestAddon), Defaults(TcTables.QuestRequestItems), Defaults(TcTables.QuestOfferReward)];
    }

    /// <summary>A new objective row: one creature kill by default.</summary>
    public static Row NewObjective(uint id, uint quest, int storageIndex, uint order) =>
        new Row(TcTables.QuestObjectives.Columns.Select(c => new KeyValuePair<string, string>(c.Name, c.Kind == ColumnKind.Text ? "" : "0")))
            .With("ID", id).With("QuestID", quest).With("Type", Monster).With("Order", order).With("StorageIndex", storageIndex).With("Amount", 1).With("Visible", 1u);

    /// <summary>
    /// Quest loader rules for one quest's effective rows. <paramref name="exists"/> answers the client's
    /// reference tables (currencies); null skips those checks.
    /// </summary>
    public static IEnumerable<InteractionIssue> Check(uint id, RowSet rows, IContentNames names, Func<uint, bool>? zoneExists, Func<ClientRecord, uint, bool>? exists)
    {
        var q = rows.Get(TcTables.QuestTemplate, id.ToString());
        if (q == null)
        {
            yield break;
        }

        string key = id.ToString();
        if (q.T("LogTitle").Trim().Length == 0)
        {
            yield return new(false, "The quest has no title.");
        }

        if (q.U("QuestType") > 3)
        {
            yield return new(true, "Quest method must be Normal, Auto-complete, Disabled or World quest.");
        }

        int sort = q.I("QuestSortID");
        if (sort > 0 && zoneExists?.Invoke((uint)sort) == false)
        {
            yield return new(true, $"Zone {sort} does not exist.");
        }

        foreach (var issue in CheckObjectives(id, rows, names, exists))
        {
            yield return issue;
        }

        foreach (var (prefix, countPrefix, slots, label, error) in new[] { ("RewardItem", "RewardAmount", 4, "Reward item", true), ("RewardChoiceItemID", "RewardChoiceItemQuantity", 6, "Choice reward", true), ("ItemDrop", "ItemDropQuantity", 4, "Quest drop", false) })
        {
            for (int i = 1; i <= slots; i++)
            {
                uint item = q.U($"{prefix}{i}");
                uint count = q.U($"{countPrefix}{i}");
                if (item != 0 && names.Item(item) == null)
                {
                    yield return new(error, $"{label} {i}: item {item} does not exist.");
                }

                if (item != 0 && count == 0 && prefix != "ItemDrop")
                {
                    yield return new(true, $"{label} {i} needs an amount above 0.");
                }

                if (item == 0 && count != 0)
                {
                    yield return new(false, $"{label} {i} has an amount but no item.");
                }
            }
        }

        for (int i = 1; i <= 4; i++)
        {
            uint currency = q.U($"RewardCurrencyID{i}");
            uint count = q.U($"RewardCurrencyQty{i}");
            if (currency != 0 && exists?.Invoke(ClientRecord.Currency, currency) == false)
            {
                yield return new(true, $"Reward currency {i}: currency {currency} does not exist.");
            }

            if (currency != 0 && count == 0)
            {
                yield return new(true, $"Reward currency {i} needs an amount above 0.");
            }

            if (currency == 0 && count != 0)
            {
                yield return new(false, $"Reward currency {i} has an amount but no currency.");
            }
        }

        if (q.U("StartItem") != 0 && names.Item(q.U("StartItem")) == null)
        {
            yield return new(true, $"Starting item {q.U("StartItem")} does not exist.");
        }

        foreach (var (column, label) in new[] { ("RewardSpell", "Reward spell"), ("RewardDisplaySpell1", "Displayed spell 1"), ("RewardDisplaySpell2", "Displayed spell 2"), ("RewardDisplaySpell3", "Displayed spell 3") })
        {
            uint spell = q.U(column);
            if (spell != 0 && names.Spell(spell) == null)
            {
                yield return new(true, $"{label} {spell} does not exist.");
            }
        }

        if (q.U("RewardNextQuest") != 0 && names.Quest(q.U("RewardNextQuest")) == null)
        {
            yield return new(true, $"Follow-up quest {q.U("RewardNextQuest")} does not exist.");
        }

        if (rows.Get(TcTables.QuestAddon, key) is { } addon)
        {
            foreach (var (column, label) in new[] { ("PrevQuestID", "Previous quest"), ("BreadcrumbForQuestId", "Breadcrumb target") })
            {
                int other = addon.I(column);
                if (other != 0 && names.Quest((uint)Math.Abs(other)) == null)
                {
                    yield return new(true, $"{label} {Math.Abs(other)} does not exist.");
                }
            }

            if (addon.U("NextQuestID") != 0 && names.Quest(addon.U("NextQuestID")) == null)
            {
                yield return new(true, $"Next quest {addon.U("NextQuestID")} does not exist.");
            }

            if (addon.U("SourceSpellID") != 0 && names.Spell(addon.U("SourceSpellID")) == null)
            {
                yield return new(true, $"Source spell {addon.U("SourceSpellID")} does not exist.");
            }
        }
        else
        {
            yield return new(false, "quest_template_addon row is missing; chain, class and skill settings use defaults.");
        }

        bool hasGiver = rows.All(TcTables.QuestStarter).Any(r => r.U("quest") == id);
        if (!hasGiver && q.U("StartItem") == 0 && (q.U("Flags") & 0x400) == 0)
        {
            yield return new(false, "No creature starts this quest (it may be started by an item, object or script).");
        }

        if (!rows.All(TcTables.QuestEnder).Any(r => r.U("quest") == id) && (q.U("Flags") & 0x10000) == 0)
        {
            yield return new(false, "No creature completes this quest.");
        }
    }

    /// <summary>The objectives of one quest: references that must exist and the storage slots they fill.</summary>
    public static IEnumerable<InteractionIssue> CheckObjectives(uint quest, RowSet rows, IContentNames names, Func<ClientRecord, uint, bool>? exists)
    {
        const int MaxStorage = 24;
        var mine = rows.All(TcTables.QuestObjectives).Where(r => r.U("QuestID") == quest).OrderBy(r => r.U("Order")).ThenBy(r => r.U("ID")).ToList();
        foreach (var group in mine.Where(r => r.I("StorageIndex") >= 0).GroupBy(r => r.I("StorageIndex")).Where(g => g.Count() > 1))
        {
            yield return new(true, $"Objectives {string.Join(" and ", group.Select(r => r.U("ID")))} share storage slot {group.Key}.");
        }

        foreach (var objective in mine)
        {
            uint type = objective.U("Type");
            uint target = (uint)Math.Max(objective.I("ObjectID"), 0);
            string label = $"Objective {objective.U("ID")} ({TypeName(type).ToLowerInvariant()})";
            if (type > KillWithLabel + 1)
            {
                yield return new(true, $"{label}: unknown objective type {type}.");
            }

            if (objective.I("StorageIndex") >= MaxStorage)
            {
                yield return new(true, $"{label}: storage slot {objective.I("StorageIndex")} is out of range (0 to {MaxStorage - 1}).");
            }

            switch (type)
            {
                case Monster or TalkTo when names.Creature(target) == null:
                    yield return new(true, $"{label}: creature {target} does not exist.");
                    break;
                case GameObject when names.GameObject(target) == null:
                    yield return new(true, $"{label}: object {target} does not exist.");
                    break;
                case Item when names.Item(target) == null:
                    yield return new(true, $"{label}: item {target} does not exist.");
                    break;
                case LearnSpell when names.Spell(target) == null:
                    yield return new(true, $"{label}: spell {target} does not exist.");
                    break;
                case Currency or HaveCurrency or ObtainCurrency when exists?.Invoke(ClientRecord.Currency, target) == false:
                    yield return new(true, $"{label}: currency {target} does not exist.");
                    break;
            }

            bool counted = type is Monster or Item or GameObject or TalkTo or Currency or LearnSpell or Money or PlayerKills or HaveCurrency or ObtainCurrency or KillWithLabel;
            if (counted && objective.I("Amount") <= 0)
            {
                yield return new(true, $"{label}: the amount must be above 0.");
            }

            if (objective.I("ParentObjectiveID") > 0 && mine.All(r => r.I("ID") != objective.I("ParentObjectiveID")))
            {
                yield return new(false, $"{label}: parent objective {objective.I("ParentObjectiveID")} is not one of this quest's objectives.");
            }
        }
    }
}
