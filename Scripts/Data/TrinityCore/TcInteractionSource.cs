using System.Globalization;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Read-only creature reads for the TrinityCore world database, into the rows of <see cref="TcTables"/>:
/// the template and its per-difficulty rows, every gossip menu (creature_template_gossip) and the menus
/// their options lead to, the trainers bound to gossip options (creature_trainer), vendor items, quest
/// links and spell clicks. Names come from quest_template, the hotfixes broadcast_text table and the
/// client's DB2 tables (items, spells, broadcast text), since the world database has no item or text
/// tables. Draft-touched rows are loaded too, so the same snapshot can back editing later.
/// </summary>
public sealed class TcInteractionSource(WorldDatabase database, IClientLookup? client, TcHotfixSource hotfixes)
{
    static readonly string[] RequiredColumns =
    [
        "creature_template.npcflag", "creature_template.Classification", "creature_template_difficulty.ContentTuningID",
        "creature_template_gossip.MenuID", "gossip_menu.TextID", "npc_text.BroadcastTextID0", "gossip_menu_option.OptionNpc",
        "creature_trainer.TrainerID", "trainer.Type", "trainer_spell.ReqAbility1", "npc_vendor.type",
        "creature_queststarter.quest", "creature_questender.quest", "npc_spellclick_spells.spell_id", "quest_template.LogTitle"
    ];

    public async Task<InteractionSnapshot> ReadAsync(uint entry, ulong guid, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require(RequiredColumns);
        var rows = new RowSet();
        await AcInteractionSource.Load(session, rows, TcTables.CreatureTemplate, "entry=@k0", [entry], token);
        if (rows.Get(TcTables.CreatureTemplate, entry.ToString(CultureInfo.InvariantCulture)) is not { } template)
        {
            throw new DatabaseFeatureException($"Creature template {entry} does not exist.");
        }

        await AcInteractionSource.Load(session, rows, TcTables.CreatureDifficulty, "Entry=@k0", [entry], token);
        await AcInteractionSource.Load(session, rows, TcTables.CreatureGossip, "CreatureID=@k0", [entry], token);
        await AcInteractionSource.Load(session, rows, TcTables.CreatureTrainer, "CreatureID=@k0", [entry], token);
        await AcInteractionSource.Load(session, rows, TcTables.Vendor, "entry=@k0", [entry], token);
        await AcInteractionSource.Load(session, rows, TcTables.QuestStarter, "id=@k0", [entry], token);
        await AcInteractionSource.Load(session, rows, TcTables.QuestEnder, "id=@k0", [entry], token);
        await AcInteractionSource.Load(session, rows, TcTables.SpellClick, "npc_entry=@k0", [entry], token);
        foreach (var (table, key) in draftKeys)
        {
            var parts = key.Split('|');
            await AcInteractionSource.Load(session, rows, table, string.Join(" AND ", table.Key.Select((k, i) => $"`{k}`=@k{i}")), parts.Cast<object>().ToArray(), token);
        }

        // Follow menus (from the creature's own and from options), their texts and the bound trainers until nothing new loads.
        var loadedMenus = new HashSet<uint>();
        var loadedTexts = new HashSet<uint>();
        var loadedTrainers = new HashSet<uint>();
        for (int pass = 0; pass < 8; pass++)
        {
            var view = effective(rows);
            var menus = view.All(TcTables.CreatureGossip).Where(r => r.U("CreatureID") == entry).Select(r => r.U("MenuID"))
                .Concat(view.All(TcTables.GossipOption).Select(r => r.U("ActionMenuID")).Where(m => m != 0))
                .Distinct().Where(m => loadedMenus.Count < TcCreatureView.MaxMenus && loadedMenus.Add(m)).ToArray();
            foreach (uint menu in menus)
            {
                await AcInteractionSource.Load(session, rows, TcTables.GossipMenu, "MenuID=@k0", [menu], token);
                await AcInteractionSource.Load(session, rows, TcTables.GossipOption, "MenuID=@k0", [menu], token);
            }

            view = effective(rows);
            var texts = view.All(TcTables.GossipMenu).Select(r => r.U("TextID")).Distinct().Where(loadedTexts.Add).ToArray();
            foreach (uint text in texts)
            {
                await AcInteractionSource.Load(session, rows, TcTables.NpcText, "ID=@k0", [text], token);
            }

            var trainers = view.All(TcTables.CreatureTrainer).Select(r => r.U("TrainerID")).Distinct().Where(loadedTrainers.Add).ToArray();
            foreach (uint trainer in trainers)
            {
                await AcInteractionSource.Load(session, rows, TcTables.Trainer, "Id=@k0", [trainer], token);
                await AcInteractionSource.Load(session, rows, TcTables.TrainerSpell, "TrainerId=@k0", [trainer], token);
            }

            if (menus.Length == 0 && texts.Length == 0 && trainers.Length == 0)
            {
                break;
            }
        }

        var final = effective(rows);
        var lookup = new InteractionLookupData(catalog, client);
        await ReadQuestTitles(session, final, lookup, token);
        await ReadTexts(final, lookup, token);
        foreach (var item in final.All(TcTables.Vendor).Where(r => r.U("type") == 1 && r.I("item") > 0).Select(r => (uint)r.I("item")).Distinct())
        {
            if (client?.Item(item) is { } info)
            {
                lookup.Items[item] = new ItemInfo(info.Id, info.Name, (int)info.Quality, 0, 0);
            }
        }

        int spawns = Convert.ToInt32(await AcInteractionSource.Scalar(session, "SELECT COUNT(*) FROM creature WHERE id=@k0", [entry], token), CultureInfo.InvariantCulture);
        return new InteractionSnapshot(entry, guid, template.T("name"), template.T("ScriptName"), template.T("AIName"), spawns, rows, [], new(), lookup, database.SourceKey);
    }

    static async Task ReadQuestTitles(WorldReadSession session, RowSet view, InteractionLookupData lookup, CancellationToken token)
    {
        var quests = view.All(TcTables.QuestStarter).Concat(view.All(TcTables.QuestEnder)).Select(r => r.U("quest")).Distinct().ToArray();
        if (quests.Length == 0)
        {
            return;
        }

        await using var command = session.Command("");
        command.CommandText = $"SELECT ID,COALESCE(LogTitle,'') FROM quest_template WHERE ID IN ({AcInteractionSource.InList(command, quests, "q")})";
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            uint id = Convert.ToUInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
            lookup.Quests[id] = new QuestInfo(id, reader.GetString(1), 0, 0, "", "", 0, []);
        }
    }

    /// <summary>Broadcast texts of the menus and options: the hotfixes table first, then the client's BroadcastText.db2.</summary>
    async Task ReadTexts(RowSet view, InteractionLookupData lookup, CancellationToken token)
    {
        var ids = view.All(TcTables.NpcText).SelectMany(r => Enumerable.Range(0, 8).Select(i => r.U($"BroadcastTextID{i}")))
            .Concat(view.All(TcTables.GossipOption).SelectMany(r => new[] { r.U("OptionBroadcastTextID"), r.U("BoxBroadcastTextID") }))
            .Where(id => id != 0).Distinct().ToList();
        IReadOnlyDictionary<uint, (string Male, string Female)> server;
        try
        {
            server = await hotfixes.ReadBroadcastTextsAsync(ids, token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            server = new Dictionary<uint, (string, string)>();
        }

        foreach (uint id in ids)
        {
            if (server.TryGetValue(id, out var text))
            {
                lookup.Broadcasts[id] = text;
            }
            else if (client?.BroadcastText(id) is { } male)
            {
                lookup.Broadcasts[id] = (male, client.BroadcastText(id, true) ?? male);
            }
        }
    }
}
