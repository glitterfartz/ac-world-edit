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
        "creature_template.npcflag", "creature_template.Classification", "creature_template_difficulty.ContentTuningID", "creature_template_difficulty.LevelScalingDeltaMin", "creature_classlevelstats.basemana",
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
        if (guid != 0)
        {
            // The spawn's cached zone and area tell the stats page where a creature without a content tuning stands.
            await AcInteractionSource.Load(session, rows, TcTables.SpawnCreature, "guid=@k0", [guid], token);
        }

        foreach (var (table, key) in draftKeys)
        {
            // Rows of another database (a new broadcast text) exist only in the draft; there is nothing to read back.
            if (table.SqlName.Contains('.'))
            {
                continue;
            }

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
        await AcInteractionSource.ReadCounts(session, "SELECT TrainerID,COUNT(*) FROM creature_trainer WHERE TrainerID IN ({0}) GROUP BY TrainerID", final.All(TcTables.CreatureTrainer).Select(r => r.U("TrainerID")).Distinct().ToArray(), lookup.TrainerUserCounts, token);
        await AcInteractionSource.ReadIds(session, "SELECT ID FROM points_of_interest WHERE ID IN ({0})", final.All(TcTables.GossipOption).Select(r => r.U("ActionPoiID")).Where(id => id != 0), lookup.Pois, token);
        await AcInteractionSource.ReadCounts(session, "SELECT MenuID,COUNT(*) FROM creature_template_gossip WHERE MenuID IN ({0}) GROUP BY MenuID", final.All(TcTables.CreatureGossip).Select(r => r.U("MenuID")).Distinct().ToArray(), lookup.MenuUserCounts, token);
        lookup.MaxMenuId = Convert.ToUInt32(await AcInteractionSource.Scalar(session, "SELECT GREATEST(COALESCE((SELECT MAX(MenuID) FROM gossip_menu),0),COALESCE((SELECT MAX(MenuID) FROM gossip_menu_option),0))", [], token), CultureInfo.InvariantCulture);
        lookup.MaxTextId = Convert.ToUInt32(await AcInteractionSource.Scalar(session, "SELECT COALESCE(MAX(ID),0) FROM npc_text", [], token), CultureInfo.InvariantCulture);
        lookup.MaxTrainerId = Convert.ToUInt32(await AcInteractionSource.Scalar(session, "SELECT COALESCE(MAX(Id),0) FROM trainer", [], token), CultureInfo.InvariantCulture);
        await ReadClassStats(session, lookup, token);
        await ReadQuestTitles(session, final, lookup, token);
        await AcInteractionSource.ReadCounts(session, "SELECT SourceEntry,COUNT(*) FROM conditions WHERE SourceTypeOrReferenceId=19 AND SourceEntry IN ({0}) GROUP BY SourceEntry", final.All(TcTables.QuestStarter).Concat(final.All(TcTables.QuestEnder)).Select(r => r.U("quest")).Distinct().ToArray(), lookup.QuestConditionCounts, token);
        await ReadTexts(final, lookup, token);
        try
        {
            (lookup.MaxBroadcastTextId, lookup.MaxHotfixId) = await hotfixes.ReadMaxIdsAsync(token).ConfigureAwait(false);
            // The client's own table loads under the hotfixes database's rows, so a new ID must clear both.
            if (lookup.MaxBroadcastTextId != 0 && client != null)
            {
                lookup.MaxBroadcastTextId = Math.Max(lookup.MaxBroadcastTextId, client.MaxBroadcastTextId());
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Without the hotfixes database a new text cannot be numbered; the editor says so when asked to write one.
            lookup.MaxBroadcastTextId = 0;
        }
        // Shared vendor lists (negative items), one level deep like the view.
        foreach (uint list in final.All(TcTables.Vendor).Where(r => r.I("item") < 0).Select(r => (uint)-r.I("item")).Distinct())
        {
            var listed = new RowSet();
            await AcInteractionSource.Load(session, listed, TcTables.Vendor, "entry=@k0", [list], token);
            lookup.ReferenceVendors[list] = listed.All(TcTables.Vendor).OrderBy(r => r.I("slot")).ThenBy(r => r.I("item")).ToList();
        }

        var vendorItems = final.All(TcTables.Vendor).Concat(lookup.ReferenceVendors.Values.SelectMany(l => l)).Where(r => r.U("type") == 1 && r.I("item") > 0).Select(r => (uint)r.I("item")).Distinct().ToList();
        IReadOnlyDictionary<uint, (string Name, int Quality)> overrides;
        try
        {
            overrides = await hotfixes.ReadItemOverridesAsync(vendorItems, token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            overrides = new Dictionary<uint, (string, int)>();
        }

        // The hotfixes database overrides the client's name and quality, as the server sends them.
        foreach (uint item in vendorItems)
        {
            if (client?.Item(item) is { } info)
            {
                var over = overrides.GetValueOrDefault(item);
                lookup.Items[item] = new ItemInfo(info.Id, over.Name is { Length: > 0 } name ? name : info.Name, overrides.ContainsKey(item) ? over.Quality : (int)info.Quality, 0, 0);
            }
        }

        int spawns = Convert.ToInt32(await AcInteractionSource.Scalar(session, "SELECT COUNT(*) FROM creature WHERE id=@k0", [entry], token), CultureInfo.InvariantCulture);
        return new InteractionSnapshot(entry, guid, template.T("name"), template.T("ScriptName"), template.T("AIName"), spawns, rows, [], new(), lookup, database.SourceKey);
    }

    /// <summary>
    /// Broadcast text for the pickers: an ID gives that text (hotfixes before client DB2); other text
    /// searches the hotfixes table and the client's BroadcastText.db2 by (partial) text, hotfixes first.
    /// </summary>
    public async Task<IReadOnlyList<(uint Id, string Name)>> FindBroadcastAsync(string term, CancellationToken token)
    {
        if (uint.TryParse(term, out uint id))
        {
            var texts = await hotfixes.ReadBroadcastTextsAsync(new[] { id }, token).ConfigureAwait(false);
            if (texts.TryGetValue(id, out var text)) return new[] { (id, text.Male) };
            if (client?.BroadcastText(id) is { } male) return new[] { (id, male) };
            return Array.Empty<(uint, string)>();
        }

        if (term.Trim().Length == 0)
            return Array.Empty<(uint, string)>();
        var found = new List<(uint Id, string Name)>(await hotfixes.SearchBroadcastTextsAsync(term.Trim(), DbLimits.MaxSearch, token).ConfigureAwait(false));
        foreach (var hit in client?.SearchBroadcastText(term.Trim(), DbLimits.MaxSearch) ?? [])
        {
            if (found.Count >= DbLimits.MaxSearch) break;
            if (found.All(f => f.Id != hit.Id)) found.Add((hit.Id, hit.Text));
        }

        return found;
    }

    /// <summary>
    /// Bounded picker search over gossip menus by ID or by what they say. A TrinityCore greeting is only
    /// broadcast IDs, so the text is searched in broadcast_text first (hotfixes, then the client) and the menus
    /// whose npc_text uses a match are listed; the name is the greeting's first text.
    /// </summary>
    public async Task<IReadOnlyList<(uint Id, string Name)>> SearchMenusAsync(string term, CancellationToken token)
    {
        term = term.Trim();
        var matches = term.Length == 0 ? [] : await FindBroadcastAsync(term, token).ConfigureAwait(false);
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        await using var command = session.Command("");
        string ids = AcInteractionSource.InList(command, matches.Select(m => m.Id), "b");
        string slots = string.Join(" OR ", Enumerable.Range(0, 8).Select(i => $"n.BroadcastTextID{i} IN ({ids})"));
        command.CommandText = $"SELECT g.MenuID,MIN(n.BroadcastTextID0) FROM gossip_menu g LEFT JOIN npc_text n ON n.ID=g.TextID WHERE g.MenuID=@id OR {slots} GROUP BY g.MenuID ORDER BY g.MenuID=@id DESC, g.MenuID LIMIT {DbLimits.MaxSearch}";
        command.Parameters.AddWithValue("@id", uint.TryParse(term, out uint number) ? number : 0);
        List<(uint Menu, uint Broadcast)> found = [];
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                found.Add((Convert.ToUInt32(reader.GetValue(0), CultureInfo.InvariantCulture), reader.IsDBNull(1) ? 0 : Convert.ToUInt32(reader.GetValue(1), CultureInfo.InvariantCulture)));
            }
        }

        var texts = matches.ToDictionary(m => m.Id, m => m.Name);
        var unknown = found.Select(f => f.Broadcast).Where(b => b != 0 && !texts.ContainsKey(b)).Distinct().ToArray();
        foreach (var (id, text) in await hotfixes.ReadBroadcastTextsAsync(unknown, token).ConfigureAwait(false))
        {
            texts[id] = text.Male;
        }

        return found.Select(f => (f.Menu, texts.TryGetValue(f.Broadcast, out string? said) ? said : client?.BroadcastText(f.Broadcast) ?? "")).ToList();
    }

    public async Task<bool> PoiExistsAsync(uint poi, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        return Convert.ToInt64(await AcInteractionSource.Scalar(session, "SELECT COUNT(*) FROM points_of_interest WHERE ID=@k0", [poi], token), CultureInfo.InvariantCulture) > 0;
    }

    public async Task<bool> MenuExistsAsync(uint menu, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        return Convert.ToInt32(await AcInteractionSource.Scalar(session,
            "SELECT EXISTS(SELECT 1 FROM gossip_menu WHERE MenuID=@k0) OR EXISTS(SELECT 1 FROM gossip_menu_option WHERE MenuID=@k0)",
            new object[] { menu }, token), CultureInfo.InvariantCulture) != 0;
    }

    public async Task<IReadOnlyList<(uint Id, string Title, int Level)>> SearchQuestsAsync(string term, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        // TrinityCore has no QuestLevel column; the level reads as 0.
        await using var command = session.Command($"SELECT ID,COALESCE(LogTitle,'') FROM quest_template WHERE LogTitle LIKE @s ESCAPE '=' OR ID=@id ORDER BY ID=@id DESC, CHAR_LENGTH(LogTitle), ID LIMIT {DbLimits.MaxSearch}");
        WorldReadSession.AddSearch(command, term, "@s", "@id");
        List<(uint, string, int)> quests = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            quests.Add((reader.GetUInt32(0), reader.GetString(1), 0));
        }

        return quests;
    }

    /// <summary>The base mana and attack power per level and class are small (about 4 classes x 123 levels), so read them all.</summary>
    static async Task ReadClassStats(WorldReadSession session, InteractionLookupData lookup, CancellationToken token)
    {
        await using var command = session.Command("SELECT level,class,basemana,attackpower,rangedattackpower FROM creature_classlevelstats LIMIT 5000");
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            lookup.TcClassStats[(Convert.ToUInt32(reader.GetValue(0), CultureInfo.InvariantCulture), Convert.ToUInt32(reader.GetValue(1), CultureInfo.InvariantCulture))] =
                new(Convert.ToUInt32(reader.GetValue(2), CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture), Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture));
        }
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
