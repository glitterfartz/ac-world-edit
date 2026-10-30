namespace ACWorldEdit.Foundation;

/// <summary>
/// Effective interaction view of one creature entry: database rows plus the local draft, with
/// names resolved. Rebuilt after every edit; never mutated.
/// </summary>
public sealed record AcCreatureView(
    uint Entry,
    ulong Guid,
    string Name,
    Row Template,
    string ScriptName,
    string AIName,
    int SpawnCount,
    IReadOnlyDictionary<uint, MenuView> Menus,
    QuestLinkView[] Starts,
    QuestLinkView[] Ends,
    VendorView[] Vendor,
    TrainerView? Trainer,
    SpellClickView[] SpellClicks,
    ScriptHook[] Scripts,
    IReadOnlyDictionary<int, int> Conditions,
    InteractionIssue[] Issues) : ICreatureView
{
    public uint NpcFlag => Template.U("npcflag");
    public uint GossipMenuId => Template.U("gossip_menu_id");
    public string Subname => Template.T("subname");

    /// <summary>
    /// Maximum menus followed through ActionMenuID, matching the source's bound.
    /// </summary>
    public const int MaxMenus = 32;

    /// <summary>
    /// Build the view from effective rows. Edited marks compare against the database snapshot.
    /// </summary>
    public static AcCreatureView Build(uint entry, ulong guid, string name, string scriptName, string aiName, int spawnCount, RowSet snapshot, RowSet rows, IInteractionLookup lookup, ScriptHook[] scripts, IReadOnlyDictionary<int, int> conditions)
    {
        bool Edited(TableSchema table, Row row) => snapshot.Get(table, table.KeyOf(row)) != row;
        var template = rows.Get(AcTables.CreatureTemplate, entry.ToString()) ?? throw new InvalidDataException($"Creature template {entry} is missing.");
        if (rows.Get(AcTables.CreatureDetails, entry.ToString()) is { } details)
        {
            name = details.T("name");
            scriptName = details.T("ScriptName");
        }

        uint flags = template.U("npcflag");
        var menus = new Dictionary<uint, MenuView>();
        var queue = new Queue<uint>();
        if (template.U("gossip_menu_id") != 0)
        {
            queue.Enqueue(template.U("gossip_menu_id"));
        }

        while (queue.Count > 0 && menus.Count < MaxMenus)
        {
            uint menu = queue.Dequeue();
            if (menus.ContainsKey(menu))
            {
                continue;
            }

            var texts = rows.All(AcTables.GossipMenu).Where(r => r.U("MenuID") == menu).Select(r =>
            {
                uint id = r.U("TextID");
                var text = rows.Get(AcTables.NpcText, id.ToString());
                if (text == null)
                {
                    return new TextView(id, $"(npc_text {id} not loaded)", "", false, 0, 0, false);
                }

                uint broadcast = (uint)Math.Max(0, text.I("BroadcastTextID0"));
                // The server sends broadcast_text instead of npc_text whenever the ID resolves.
                var fromBroadcast = broadcast != 0 ? lookup.BroadcastText(broadcast) : null;
                return new TextView(id, fromBroadcast?.Male ?? text.T("text0_0"), fromBroadcast?.Female ?? text.T("text0_1"), fromBroadcast != null, broadcast, text.F("Probability0"), Edited(AcTables.NpcText, text) || Edited(AcTables.GossipMenu, r));
            }).ToArray();
            var options = rows.All(AcTables.GossipOption).Where(r => r.U("MenuID") == menu).OrderBy(r => r.U("OptionID")).Select(r =>
            {
                uint broadcast = (uint)Math.Max(0, r.I("OptionBroadcastTextID"));
                var fromBroadcast = broadcast != 0 ? lookup.BroadcastText(broadcast) : null;
                uint action = r.U("ActionMenuID");
                if (action != 0)
                {
                    queue.Enqueue(action);
                }

                return new OptionView(r, r.U("OptionID"), r.U("OptionIcon"), fromBroadcast?.Male ?? r.T("OptionText"), fromBroadcast != null, r.U("OptionType"), r.U("OptionNpcFlag"), action, r.U("ActionPoiID"), r.U("BoxCoded") != 0, r.U("BoxMoney"), r.U("BoxBroadcastTextID") is var boxBroadcast and not 0 && lookup.BroadcastText(boxBroadcast) is { } boxSaid ? boxSaid.Male : r.T("BoxText"), (r.U("OptionNpcFlag") & flags) == 0, Edited(AcTables.GossipOption, r));
            }).ToArray();
            menus[menu] = new MenuView(menu, texts, options, lookup.MenuUsers(menu), texts.Any(t => t.Edited) || options.Any(o => o.Edited));
        }

        QuestLinkView[] Quests(TableSchema table) => rows.All(table).Where(r => r.U("id") == entry).OrderBy(r => r.U("quest")).Select(r => new QuestLinkView(r.U("quest"), lookup.Quest(r.U("quest")), Edited(table, r))).ToArray();

        List<VendorView> vendor = [];
        foreach (var row in rows.All(AcTables.Vendor).Where(r => r.U("entry") == entry).OrderBy(r => r.I("slot")).ThenBy(r => r.I("item")))
        {
            int item = row.I("item");
            if (item < 0)
            {
                // A negative item references another vendor's list; its rows are edited there.
                foreach (var referenced in lookup.ReferenceVendor((uint)-item).Where(r => r.I("item") > 0))
                {
                    vendor.Add(new VendorView(null, (uint)referenced.I("item"), referenced.U("ExtendedCost"), row.I("slot"), referenced.U("maxcount"), referenced.U("incrtime"), lookup.Item((uint)referenced.I("item")), (uint)-item, false));
                }

                continue;
            }

            vendor.Add(new VendorView(row, (uint)item, row.U("ExtendedCost"), row.I("slot"), row.U("maxcount"), row.U("incrtime"), lookup.Item((uint)item), 0, Edited(AcTables.Vendor, row)));
        }

        TrainerView? trainer = null;
        if (rows.Get(AcTables.DefaultTrainer, entry.ToString()) is { } link)
        {
            uint id = link.U("TrainerId");
            var info = rows.Get(AcTables.Trainer, id.ToString());
            var spells = rows.All(AcTables.TrainerSpell).Where(r => r.U("TrainerId") == id).OrderBy(r => r.U("ReqLevel")).ThenBy(r => r.U("SpellId"))
                .Select(r => new TrainerSpellView(r, r.U("SpellId"), lookup.SpellName(r.U("SpellId")) ?? $"Spell {r.U("SpellId")}", r.U("MoneyCost"), r.U("ReqSkillLine"), r.U("ReqSkillRank"), r.U("ReqLevel"), Edited(AcTables.TrainerSpell, r))).ToArray();
            trainer = new TrainerView(id, info?.U("Type") ?? 0, info?.U("Requirement") ?? 0, info?.T("Greeting") ?? "", spells, lookup.TrainerUsers(id), info != null || lookup.TrainerExists(id));
        }

        var clicks = rows.All(AcTables.SpellClick).Where(r => r.U("npc_entry") == entry).Select(r => new SpellClickView(r, r.U("spell_id"), lookup.SpellName(r.U("spell_id")) ?? $"Spell {r.U("spell_id")}", r.U("cast_flags"), r.U("user_type"), Edited(AcTables.SpellClick, r))).ToArray();
        var view = new AcCreatureView(entry, guid, name, template, scriptName, aiName, spawnCount, menus, Quests(AcTables.QuestStarter), Quests(AcTables.QuestEnder), vendor.ToArray(), trainer, clicks, scripts, conditions, []);
        return view with { Issues = AcInteractionRules.Check(view, rows, lookup).ToArray() };
    }
}

/// <summary>
/// Server loading rules (ObjectMgr Load*), as errors, and server-logged npcflag inconsistencies, as
/// warnings with fixes. Only rows in this NPC's effective scope are checked.
/// </summary>
public static class AcInteractionRules
{
    public static IEnumerable<InteractionIssue> Check(AcCreatureView view, RowSet rows, IInteractionLookup lookup)
    {
        uint entry = view.Entry, flags = view.NpcFlag;
        bool MenuExists(uint id) => rows.All(AcTables.GossipMenu).Any(r => r.U("MenuID") == id) || lookup.MenuExists(id);
        foreach (var menu in view.Menus.Values)
        {
            foreach (var text in menu.Texts.Where(t => rows.Get(AcTables.NpcText, t.TextId.ToString()) == null && !lookup.TextExists(t.TextId)))
            {
                yield return new(true, $"Menu {menu.MenuId} uses npc_text {text.TextId}, which does not exist.");
            }

            foreach (var option in menu.Options)
            {
                string where = $"Menu {menu.MenuId} option {option.OptionId}";
                if (option.Type >= AcNpcFlags.OptionTypes.Length)
                {
                    yield return new(true, $"{where}: option type {option.Type} is unknown; the server will not use it.");
                }

                if (option.Icon >= AcNpcFlags.OptionIcons.Length || option.Icon is 14 or 15)
                {
                    yield return new(true, $"{where}: icon {option.Icon} is invalid.");
                }

                if (option.ActionMenu != 0 && !MenuExists(option.ActionMenu))
                {
                    yield return new(true, $"{where}: submenu {option.ActionMenu} does not exist.");
                }

                if (option.Poi != 0 && !lookup.PoiExists(option.Poi))
                {
                    yield return new(true, $"{where}: point of interest {option.Poi} does not exist.");
                }
            }
        }

        foreach (var link in view.Starts.Concat(view.Ends).Where(q => q.Info == null))
        {
            yield return new(true, $"Quest {link.Quest} does not exist in quest_template.");
        }

        foreach (var item in view.Vendor.Where(v => v.Row != null))
        {
            if (item.Info == null)
            {
                yield return new(true, $"Vendor item {item.Item} does not exist in item_template.");
            }

            if (item.ExtendedCost != 0 && !lookup.ExtendedCostExists(item.ExtendedCost))
            {
                yield return new(true, $"Vendor item {item.Item}: extended cost {item.ExtendedCost} is not in ItemExtendedCost.dbc.");
            }

            if (item.MaxCount > 0 && item.IncrTime == 0)
            {
                yield return new(true, $"Vendor item {item.Item}: limited stock needs a restock time (incrtime).");
            }

            if (item.MaxCount == 0 && item.IncrTime != 0)
            {
                yield return new(true, $"Vendor item {item.Item}: a restock time needs a limited stock (maxcount).");
            }
        }

        foreach (var reference in rows.All(AcTables.Vendor).Where(r => r.U("entry") == entry && r.I("item") < 0 && lookup.ReferenceVendor((uint)-r.I("item")).Count == 0))
        {
            yield return new(true, $"Vendor reference {-reference.I("item")} has no items.");
        }

        if (view.Trainer is { } trainer)
        {
            if (!trainer.Exists)
            {
                yield return new(true, $"Trainer {trainer.Id} does not exist.");
            }

            foreach (var spell in trainer.Spells.Where(s => lookup.SpellName(s.SpellId) == null))
            {
                yield return new(true, $"Trainer spell {spell.SpellId} is not in Spell.dbc.");
            }
        }

        foreach (var click in view.SpellClicks.Where(c => lookup.SpellName(c.SpellId) == null))
        {
            yield return new(true, $"Spell click {click.SpellId} is not in Spell.dbc.");
        }

        // Server-logged inconsistencies: the data loads, but the client never offers it.
        InteractionIssue Missing(uint bit, string what) => new(false, $"{what}, but npcflag lacks {AcNpcFlags.Names(bit).First()}.", [new RowEdit(AcTables.CreatureTemplate.Name, entry.ToString(), view.Template, view.Template.With("npcflag", flags | bit))]);
        if ((view.Starts.Length > 0 || view.Ends.Length > 0) && (flags & AcNpcFlags.QuestGiver) == 0)
        {
            yield return Missing(AcNpcFlags.QuestGiver, "Quests are linked");
        }

        if (view.Vendor.Length > 0 && (flags & AcNpcFlags.VendorMask) == 0)
        {
            yield return Missing(AcNpcFlags.Vendor, "Vendor items exist");
        }

        if (view.Trainer != null && (flags & (AcNpcFlags.Trainer | AcNpcFlags.TrainerClass | AcNpcFlags.TrainerProfession)) == 0)
        {
            yield return Missing(AcNpcFlags.Trainer, "A trainer is assigned");
        }

        if (view.SpellClicks.Length > 0 && (flags & AcNpcFlags.SpellClick) == 0)
        {
            yield return Missing(AcNpcFlags.SpellClick, "Spell clicks exist");
        }

        if (view.GossipMenuId != 0 && (flags & AcNpcFlags.Gossip) == 0)
        {
            yield return Missing(AcNpcFlags.Gossip, "A gossip menu is set");
        }
    }
}
