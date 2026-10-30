using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>One creature_template_difficulty row.</summary>
public sealed record TcDifficultyView(int DifficultyId, Row Row);

/// <summary>An npc_text variant: a broadcast text and its weight.</summary>
public sealed record TcTextVariant(uint BroadcastId, float Probability, string? Text);

/// <summary>One gossip_menu text (a menu can pick between several).</summary>
public sealed record TcMenuText(uint TextId, bool Missing, TcTextVariant[] Variants);

/// <summary>A gossip option; Trainer is the trainer this option opens (creature_trainer).</summary>
public sealed record TcOptionView(uint OptionId, int GossipOptionId, uint OptionNpc, string Text, uint ActionMenuId, uint ActionPoiId, bool Coded, ulong BoxMoney, string BoxText, int SpellId, uint? TrainerId);

/// <summary>A gossip menu. Root menus are the creature's own (creature_template_gossip); the others are reached through options.</summary>
public sealed record TcMenuView(uint MenuId, bool Root, TcMenuText[] Texts, TcOptionView[] Options);

public sealed record TcTrainerSpell(uint SpellId, string? Name, uint MoneyCost, uint ReqSkillLine, uint ReqSkillRank, uint ReqLevel, uint[] ReqAbilities);

/// <summary>A trainer reached from gossip option (MenuId, OptionId); 0/0 is the creature's default trainer.</summary>
public sealed record TcTrainerView(uint TrainerId, uint MenuId, uint OptionId, bool Missing, uint Type, string Greeting, TcTrainerSpell[] Spells, int SharedBy);

/// <summary>An npc_vendor row (Type 1 item, 2 currency).</summary>
public sealed record TcVendorItem(int Item, uint Type, ItemInfo? Info, int Slot, uint MaxCount, uint IncrTime, uint ExtendedCost, string BonusListIds, uint PlayerConditionId);

/// <summary>
/// Read model of one TrinityCore creature for the creature properties window, built from the rows of
/// <see cref="TcTables"/>: the template, its per-difficulty rows, every gossip menu (a creature can have
/// several), the trainers bound to gossip options, vendor items, quest links and spell clicks.
/// </summary>
public sealed record TcCreatureView(uint Entry, string Name, string Subname, ulong NpcFlags, Row Template, TcDifficultyView[] Difficulties, TcMenuView[] Menus, TcTrainerView[] Trainers, TcVendorItem[] Vendor, QuestLinkView[] Starts, QuestLinkView[] Ends, uint[] SpellClicks) : ICreatureView
{
    /// <summary>Menus reachable from the root menus are followed up to this many.</summary>
    public const int MaxMenus = 64;

    /// <param name="snapshot">The database rows the edited marks compare against; without them nothing is marked edited.</param>
    public static TcCreatureView Build(uint entry, RowSet rows, IInteractionLookup lookup, RowSet? snapshot = null)
    {
        string key = entry.ToString(CultureInfo.InvariantCulture);
        var template = rows.Get(TcTables.CreatureTemplate, key) ?? throw new InvalidDataException($"Creature template {entry} is missing.");
        var difficulties = rows.All(TcTables.CreatureDifficulty).Where(r => r.U("Entry") == entry)
            .Select(r => new TcDifficultyView(r.I("DifficultyID"), r)).OrderBy(d => d.DifficultyId).ToArray();
        var trainerLinks = rows.All(TcTables.CreatureTrainer).Where(r => r.U("CreatureID") == entry).ToList();
        uint? TrainerOf(uint menu, uint option) => trainerLinks.FirstOrDefault(r => r.U("MenuID") == menu && r.U("OptionID") == option)?.U("TrainerID");

        var roots = rows.All(TcTables.CreatureGossip).Where(r => r.U("CreatureID") == entry).Select(r => r.U("MenuID")).Order().ToList();
        var menus = new List<TcMenuView>();
        var seen = new HashSet<uint>();
        var queue = new Queue<uint>(roots);
        while (queue.Count > 0 && menus.Count < MaxMenus)
        {
            uint menu = queue.Dequeue();
            if (!seen.Add(menu))
            {
                continue;
            }

            var texts = rows.All(TcTables.GossipMenu).Where(r => r.U("MenuID") == menu).Select(r => Text(rows, r.U("TextID"), lookup)).OrderBy(t => t.TextId).ToArray();
            var options = rows.All(TcTables.GossipOption).Where(r => r.U("MenuID") == menu).OrderBy(r => r.U("OptionID")).Select(r =>
            {
                uint broadcast = r.U("OptionBroadcastTextID");
                string text = broadcast != 0 && Said(rows, lookup, broadcast) is { } said ? said.Male : r.T("OptionText");
                uint boxBroadcast = r.U("BoxBroadcastTextID");
                string box = boxBroadcast != 0 && Said(rows, lookup, boxBroadcast) is { } boxSaid ? boxSaid.Male : r.T("BoxText");
                return new TcOptionView(r.U("OptionID"), r.I("GossipOptionID"), r.U("OptionNpc"), text, r.U("ActionMenuID"), r.U("ActionPoiID"), r.U("BoxCoded") != 0, r.UL("BoxMoney"), box, r.I("SpellID"), TrainerOf(menu, r.U("OptionID")));
            }).ToArray();
            foreach (var option in options.Where(o => o.ActionMenuId != 0))
            {
                queue.Enqueue(option.ActionMenuId);
            }

            menus.Add(new TcMenuView(menu, roots.Contains(menu), texts, options));
        }

        var trainers = trainerLinks.OrderBy(r => r.U("MenuID")).ThenBy(r => r.U("OptionID")).Select(link =>
        {
            uint id = link.U("TrainerID");
            var trainer = rows.Get(TcTables.Trainer, id.ToString(CultureInfo.InvariantCulture));
            var spells = rows.All(TcTables.TrainerSpell).Where(s => s.U("TrainerId") == id).OrderBy(s => s.U("ReqLevel")).ThenBy(s => s.U("SpellId"))
                .Select(s => new TcTrainerSpell(s.U("SpellId"), lookup.SpellName(s.U("SpellId")), s.U("MoneyCost"), s.U("ReqSkillLine"), s.U("ReqSkillRank"), s.U("ReqLevel"),
                    new[] { s.U("ReqAbility1"), s.U("ReqAbility2"), s.U("ReqAbility3") }.Where(a => a != 0).ToArray())).ToArray();
            return new TcTrainerView(id, link.U("MenuID"), link.U("OptionID"), trainer == null, trainer?.U("Type") ?? 0, trainer?.T("Greeting") ?? "", spells, lookup.TrainerUsers(id));
        }).ToArray();

        var vendor = rows.All(TcTables.Vendor).Where(r => r.U("entry") == entry).OrderBy(r => r.I("slot")).ThenBy(r => r.I("item"))
            .Select(r => new TcVendorItem(r.I("item"), r.U("type"), r.U("type") == 1 && r.I("item") > 0 ? lookup.Item((uint)r.I("item")) : null, r.I("slot"), r.U("maxcount"), r.U("incrtime"), r.U("ExtendedCost"), r.T("BonusListIDs"), r.U("PlayerConditionID"))).ToArray();
        QuestLinkView[] Quests(TableSchema table) => rows.All(table).Where(r => r.U("id") == entry).OrderBy(r => r.U("quest")).Select(r => new QuestLinkView(r.U("quest"), lookup.Quest(r.U("quest")), snapshot != null && snapshot.Get(table, table.KeyOf(r)) != r)).ToArray();
        var clicks = rows.All(TcTables.SpellClick).Where(r => r.U("npc_entry") == entry).Select(r => r.U("spell_id")).Order().ToArray();
        return new TcCreatureView(entry, template.T("name"), template.T("subname"), template.UL("npcflag"), template, difficulties, menus.ToArray(), trainers, vendor, Quests(TcTables.QuestStarter), Quests(TcTables.QuestEnder), clicks);
    }

    /// <summary>A broadcast text: one written in this draft, else the database's or client's.</summary>
    static (string Male, string Female)? Said(RowSet rows, IInteractionLookup lookup, uint id) => TcBroadcastText.Staged(rows, id) ?? lookup.BroadcastText(id);

    static TcMenuText Text(RowSet rows, uint textId, IInteractionLookup lookup)
    {
        var row = rows.Get(TcTables.NpcText, textId.ToString(CultureInfo.InvariantCulture));
        if (row == null)
        {
            return new TcMenuText(textId, true, []);
        }

        var variants = Enumerable.Range(0, 8)
            .Select(i => (Id: row.U($"BroadcastTextID{i}"), Weight: row.F($"Probability{i}")))
            .Where(v => v.Id != 0)
            .Select(v => new TcTextVariant(v.Id, v.Weight, Said(rows, lookup, v.Id)?.Male))
            .ToArray();
        return new TcMenuText(textId, false, variants);
    }

    /// <summary>
    /// TrinityCore GossipOptionNpc (GossipDef.h, master): what an option does when chosen. Values
    /// past the list show as numbers.
    /// </summary>
    public static readonly string[] OptionNpcNames =
    [
        "None", "Vendor", "Taxi node", "Trainer", "Spirit healer", "Binder", "Banker", "Petition vendor", "Guild tabard vendor", "Battlemaster",
        "Auctioneer", "Talent master", "Stable master", "Pet specialization master", "Guild banker", "Spell click", "Disable XP gain", "Enable XP gain", "Mailbox", "World PvP queue",
        "LFG dungeon", "Artifact respec", "Cemetery select", "Specialization master", "Glyph master", "Queue scenario", "Garrison architect", "Garrison mission NPC", "Shipment crafter", "Garrison tradeskill NPC",
        "Garrison recruitment", "Adventure map", "Garrison talent", "Contribution collector", "Transmogrify", "Azerite respec", "Islands mission NPC", "UI item interaction", "World map", "Soulbind",
        "Chromie time NPC", "Covenant preview NPC", "Runeforge legendary crafting", "New player guide", "Runeforge legendary upgrade", "Covenant renown NPC", "Black market auction house", "Perks program vendor", "Professions crafting order", "Professions open",
        "Professions customer order", "Trait system", "Barber's choice", "Major faction renown", "Personal tabard vendor", "Forge master", "Character banker", "Account banker", "Profession respec", "Rename neighborhood",
        "Housing create guild neighborhood", "Housing get neighborhood charter", "Guild rename", "Housing open charter confirmation", "Item upgrade", "House finder", "Tiered entrance", "Rewards shop"
    ];

    public static string OptionNpcName(uint value) => value < OptionNpcNames.Length ? OptionNpcNames[value] : $"Option NPC {value}";

    /// <summary>
    /// TrinityCore npcflag (UnitDefines.h, master): the low 32 bits are NPCFlags, the high 32 bits
    /// NPCFlags2 (Creature.cpp splits them that way).
    /// </summary>
    public static readonly (ulong Bit, string Name)[] NpcFlagBits =
    [
        (0x1, "Gossip"), (0x2, "Quest giver"), (0x4, "Account banker"), (0x10, "Trainer"), (0x20, "Class trainer"), (0x40, "Profession trainer"),
        (0x80, "Vendor"), (0x100, "Ammo vendor"), (0x200, "Food vendor"), (0x400, "Poison vendor"), (0x800, "Reagent vendor"), (0x1000, "Repair"),
        (0x2000, "Flight master"), (0x4000, "Spirit healer"), (0x8000, "Area spirit healer"), (0x10000, "Innkeeper"), (0x20000, "Banker"), (0x40000, "Petitioner"),
        (0x80000, "Tabard designer"), (0x100000, "Battlemaster"), (0x200000, "Auctioneer"), (0x400000, "Stable master"), (0x800000, "Guild banker"), (0x1000000, "Spell click"),
        (0x2000000, "Player vehicle"), (0x4000000, "Mailbox"), (0x8000000, "Artifact power respec"), (0x10000000, "Transmogrifier"), (0x20000000, "Void storage"), (0x40000000, "Wild battle pet"),
        (0x80000000, "Black market"),
        (0x1ul << 32, "Item upgrade master"), (0x2ul << 32, "Garrison architect"), (0x4ul << 32, "Steering"), (0x8ul << 32, "Individual area spirit healer"), (0x10ul << 32, "Shipment crafter"),
        (0x20ul << 32, "Garrison mission NPC"), (0x40ul << 32, "Tradeskill NPC"), (0x80ul << 32, "Black market view"), (0x200ul << 32, "Garrison talent NPC"), (0x400ul << 32, "Contribution collector"),
        (0x2000ul << 32, "Fast steering avoids obstacles"), (0x4000ul << 32, "Azerite respec"), (0x8000ul << 32, "Islands queue"), (0x10000ul << 32, "Suppress NPC sounds"), (0x80000ul << 32, "Perks vendor"),
        (0x200000ul << 32, "Personal tabard designer")
    ];

    /// <summary>The creature card's interaction summary, in the AzerothCore summary's layout.</summary>
    public string Summary()
    {
        var flags = DescribeNpcFlags(NpcFlags);
        string script = Template.T("ScriptName").Length > 0 ? $"\nC++ script: {Template.T("ScriptName")}" : "";
        return $"{(flags.Count == 0 ? "No NPC flags" : string.Join(" · ", flags))}\n" +
               $"Gossip menus {Menus.Count(m => m.Root)} · options {Menus.Sum(m => m.Options.Length)} · Quests {Starts.Length}/{Ends.Length} · Vendor {Vendor.Length} · Trainers {Trainers.Length}" + script;
    }

    /// <summary>The named flags set in npcflag, then any unnamed bits as hex.</summary>
    public static IReadOnlyList<string> DescribeNpcFlags(ulong flags)
    {
        var names = NpcFlagBits.Where(f => (flags & f.Bit) != 0).Select(f => f.Name).ToList();
        ulong unknown = flags & ~NpcFlagBits.Aggregate(0ul, (all, f) => all | f.Bit);
        if (unknown != 0)
        {
            names.Add($"0x{unknown:X}");
        }

        return names;
    }
}
