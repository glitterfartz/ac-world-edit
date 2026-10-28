namespace ACWorldEdit.Foundation;

/// <summary>
/// Friendly quest vocabulary (server QuestDef.h / SharedDefines.h) and the quest loader's rules
/// (ObjectMgr::LoadQuests) over editable quest rows. Engine- and database-independent.
/// </summary>
public static class QuestModel
{
    /// <summary>
    /// quest_template.QuestType is the quest "method".
    /// </summary>
    public static readonly (uint Value, string Name)[] Methods = [(2, "Normal"), (0, "Auto-complete"), (1, "Disabled")];
    /// <summary>
    /// QuestInfoID (QuestInfo.dbc).
    /// </summary>
    public static readonly (uint Value, string Name)[] Types = [(0, "Normal"), (1, "Group (elite)"), (21, "Life"), (41, "PvP"), (62, "Raid"), (81, "Dungeon"), (82, "World event"), (83, "Legendary"), (84, "Escort"), (85, "Heroic"), (88, "Raid (10)"), (89, "Raid (25)")];
    public static readonly (uint Bit, string Name)[] Flags =
    [
        (0x1, "Fails on death"), (0x2, "Party accept"), (0x8, "Shareable"), (0x200, "Hidden rewards"), (0x400, "Tracking (auto-rewarded, hidden)"),
        (0x1000, "Daily"), (0x2000, "Forces PvP"), (0x4000, "Unavailable"), (0x8000, "Weekly"), (0x10000, "Auto-complete"), (0x20000, "Usable item in tracker"),
        (0x40000, "Objective text as completion text"), (0x80000, "Auto-accept")
    ];
    /// <summary>
    /// quest_template_addon.SpecialFlags (QUEST_SPECIAL_FLAGS_DB_ALLOWED).
    /// </summary>
    public static readonly (uint Bit, string Name)[] SpecialFlags = [(0x1, "Repeatable"), (0x2, "Completed by exploration or event"), (0x4, "Auto-accept"), (0x8, "Dungeon Finder quest"), (0x10, "Monthly"), (0x20, "Credit by spell, not kill"), (0x40, "No reputation spillover"), (0x80, "Can fail in any state"), (0x100, "Not counted for Loremaster")];
    public static readonly (uint Bit, string Name)[] Races = [(1, "Human"), (2, "Orc"), (4, "Dwarf"), (8, "Night Elf"), (16, "Undead"), (32, "Tauren"), (64, "Gnome"), (128, "Troll"), (512, "Blood Elf"), (1024, "Draenei")];
    public static readonly (uint Bit, string Name)[] Classes = [(1, "Warrior"), (2, "Paladin"), (4, "Hunter"), (8, "Rogue"), (16, "Priest"), (32, "Death Knight"), (64, "Shaman"), (128, "Mage"), (256, "Warlock"), (1024, "Druid")];
    public const uint AllianceRaces = 1 | 4 | 8 | 64 | 1024, HordeRaces = 2 | 16 | 32 | 128 | 512;
    /// <summary>
    /// Text placeholders the client substitutes.
    /// </summary>
    public static readonly (string Token, string Meaning)[] Placeholders = [("$N", "player name"), ("$C", "class"), ("$R", "race"), ("$B", "new line"), ("$G man:woman;", "gendered word")];

    /// <summary>
    /// The four per-quest tables (all keyed by ID), in the order NewQuest returns their rows.
    /// </summary>
    public static readonly TableSchema[] Tables = [AcTables.QuestTemplate, AcTables.QuestAddon, AcTables.QuestRequestItems, AcTables.QuestOfferReward];

    /// <summary>
    /// Complete default rows for a new quest (all whitelisted columns), ready to insert.
    /// </summary>
    public static Row[] NewQuest(uint id, string title)
    {
        Row Defaults(TableSchema table) => new(table.Columns.Select(c => new KeyValuePair<string, string>(c.Name, c.Name == table.Key[0] ? id.ToString() : c.Kind == ColumnKind.Text ? "" : c.Kind == ColumnKind.Float ? "0" : "0")));
        var template = Defaults(AcTables.QuestTemplate).With("QuestType", 2u).With("QuestLevel", 1).With("MinLevel", 1u).With("LogTitle", title).With("RewardXPDifficulty", 1u).With("Flags", 0x8u);
        return [template, Defaults(AcTables.QuestAddon), Defaults(AcTables.QuestRequestItems), Defaults(AcTables.QuestOfferReward)];
    }

    /// <summary>
    /// Quest loader rules for one quest's effective rows. Errors are what the server rejects or
    /// cannot complete; warnings are suspicious but loadable.
    /// </summary>
    public static IEnumerable<InteractionIssue> Check(uint id, RowSet rows, IContentNames names, Func<uint, bool>? zoneExists)
    {
        var q = rows.Get(AcTables.QuestTemplate, id.ToString());
        if (q == null)
        {
            yield break;
        }

        string key = id.ToString();
        if (q.T("LogTitle").Trim().Length == 0)
        {
            yield return new(false, "The quest has no title.");
        }

        if (q.U("QuestType") > 2)
        {
            yield return new(true, "Quest method must be Normal, Auto-complete or Disabled.");
        }

        if (q.I("QuestLevel") > 0 && q.U("MinLevel") > q.I("QuestLevel"))
        {
            yield return new(false, $"Required level {q.U("MinLevel")} is above the quest level {q.I("QuestLevel")}.");
        }

        int sort = q.I("QuestSortID");
        if (sort > 0 && zoneExists?.Invoke((uint)sort) == false)
        {
            yield return new(true, $"Zone {sort} does not exist.");
        }

        for (int i = 1; i <= 4; i++)
        {
            int target = q.I($"RequiredNpcOrGo{i}");
            uint count = q.U($"RequiredNpcOrGoCount{i}");
            if (target > 0 && names.Creature((uint)target) == null)
            {
                yield return new(true, $"Objective {i}: creature {target} does not exist.");
            }
            else if (target < 0 && names.GameObject((uint)-target) == null)
            {
                yield return new(true, $"Objective {i}: object {-target} does not exist.");
            }

            if (target != 0 && count == 0)
            {
                yield return new(true, $"Objective {i} needs a count above 0.");
            }

            if (target == 0 && count != 0)
            {
                yield return new(false, $"Objective {i} has a count but no creature or object.");
            }
        }

        foreach (var (prefix, countPrefix, slots, label, error) in new[] { ("RequiredItemId", "RequiredItemCount", 6, "Required item", true), ("RewardItem", "RewardAmount", 4, "Reward item", true), ("RewardChoiceItemID", "RewardChoiceItemQuantity", 6, "Choice reward", true), ("ItemDrop", "ItemDropQuantity", 4, "Quest drop", false) })
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

        if (q.U("StartItem") != 0 && names.Item(q.U("StartItem")) == null)
        {
            yield return new(true, $"Starting item {q.U("StartItem")} does not exist.");
        }

        foreach (string column in new[] { "RewardSpell", "RewardDisplaySpell" })
        {
            int spell = q.I(column);
            if (spell > 0 && names.Spell((uint)spell) == null)
            {
                yield return new(true, $"{(column == "RewardSpell" ? "Reward spell" : "Displayed spell")} {spell} does not exist.");
            }
        }

        if (q.U("RewardNextQuest") != 0 && names.Quest(q.U("RewardNextQuest")) == null)
        {
            yield return new(true, $"Follow-up quest {q.U("RewardNextQuest")} does not exist.");
        }

        if (rows.Get(AcTables.QuestAddon, key) is { } addon)
        {
            int prev = addon.I("PrevQuestID");
            if (prev != 0 && names.Quest((uint)Math.Abs(prev)) == null)
            {
                yield return new(true, $"Previous quest {Math.Abs(prev)} does not exist.");
            }

            if (addon.U("NextQuestID") != 0 && names.Quest(addon.U("NextQuestID")) == null)
            {
                yield return new(true, $"Next quest {addon.U("NextQuestID")} does not exist.");
            }

            if (addon.U("SourceSpellID") != 0 && names.Spell(addon.U("SourceSpellID")) == null)
            {
                yield return new(true, $"Source spell {addon.U("SourceSpellID")} does not exist.");
            }

            if (addon.U("MaxLevel") != 0 && addon.U("MaxLevel") < q.U("MinLevel"))
            {
                yield return new(false, "Maximum level is below the required level; nobody can take this quest.");
            }
        }
        else
        {
            yield return new(false, "quest_template_addon row is missing; chain, class and skill settings use defaults.");
        }

        if (q.U("AllowableRaces") != 0 && (q.U("AllowableRaces") & ~(AllianceRaces | HordeRaces)) != 0)
        {
            yield return new(false, $"Race mask has unknown bits 0x{q.U("AllowableRaces") & ~(AllianceRaces | HordeRaces):X}.");
        }

        bool hasGiver = rows.All(AcTables.QuestStarter).Any(r => r.U("quest") == id);
        if (!hasGiver && q.U("StartItem") == 0 && (q.U("Flags") & 0x400) == 0)
        {
            yield return new(false, "No creature starts this quest (it may be started by an item, object or script).");
        }

        if (!rows.All(AcTables.QuestEnder).Any(r => r.U("quest") == id) && (q.U("Flags") & 0x10000) == 0)
        {
            yield return new(false, "No creature completes this quest.");
        }
    }
}
