using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Read-only snapshots for the TrinityCore behavior editor: a creature's SmartAI rules (its entry, one
/// spawn and the timed action lists they run), its AIName and its speech lines, plus every draft-touched
/// row. Creature, object and quest names come from the world database; items and spells come from the
/// client, and a speech line's broadcast text from the hotfixes database before the client's
/// BroadcastText.db2. AreaTrigger scripts are not read (TrinityCore keys them by AreaTrigger.db2).
/// </summary>
public sealed class TcBehaviorSource(WorldDatabase database, IClientLookup? client, TcHotfixSource hotfixes)
{
    static readonly SmartCatalog Smart = SmartCatalog.Trinity;

    public async Task<ContentSnapshot> ReadAsync(uint entry, ulong guid, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("smart_scripts.action_param7", "smart_scripts.Difficulties", "creature_template.AIName", "creature_text.SoundPlayType");
        var rows = new RowSet();
        string name = "", script = "";
        await using (var command = session.Command("SELECT entry,COALESCE(AIName,''),COALESCE(name,''),COALESCE(ScriptName,'') FROM creature_template WHERE entry=@e"))
        {
            command.Parameters.AddWithValue("@e", entry);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                throw new InvalidDataException($"Creature template {entry} does not exist.");
            }

            rows.Set(Smart.CreatureAi, Row.Of(("entry", reader.GetUInt32(0)), ("AIName", reader.GetString(1))));
            name = reader.GetString(2);
            script = reader.GetString(3);
        }

        await AcInteractionSource.Load(session, rows, Smart.Scripts, "entryorguid=@k0 AND source_type=0", [(long)entry], token);
        if (guid != 0)
        {
            await AcInteractionSource.Load(session, rows, Smart.Scripts, "entryorguid=@k0 AND source_type=0", [-(long)guid], token);
        }

        foreach (var (table, key) in draftKeys.Where(k => k.Table.Name is "smart_scripts" or "creature_ai" or "creature_text"))
        {
            var parts = key.Split('|');
            await AcInteractionSource.Load(session, rows, table, string.Join(" AND ", table.Key.Select((k, i) => $"`{k}`=@k{i}")), parts.Cast<object>().ToArray(), token);
        }

        await LoadActionLists(session, rows, effective, token);
        var names = new ContentNames(catalog, client);
        await ResolveSmartNames(session, effective(rows), names, token);
        names.Creatures.TryAdd(entry, name);
        await AcInteractionSource.Load(session, rows, Smart.CreatureText, "CreatureID=@k0", [entry], token);
        ContentNames.FillTexts(names, effective(rows), entry);
        await ReadBroadcasts(effective(rows), entry, names, token);
        if (guid != 0 && Convert.ToInt64(await AcInteractionSource.Scalar(session, "SELECT COUNT(*) FROM creature WHERE guid=@k0", [guid], token)) > 0)
        {
            names.Spawns.Add(guid);
        }

        uint maxQuest = Convert.ToUInt32(await AcInteractionSource.Scalar(session, "SELECT COALESCE(MAX(ID),0) FROM quest_template", [], token));
        return new ContentSnapshot(rows, names, database.SourceKey, maxQuest, name, entry, script);
    }

    /// <summary>
    /// Load every timed action list (source_type 9) reachable through "Run action list" in the
    /// effective view, a bounded number of hops and lists per hop.
    /// </summary>
    static async Task LoadActionLists(WorldReadSession session, RowSet rows, Func<RowSet, RowSet> effective, CancellationToken token)
    {
        var loadedLists = new HashSet<uint>();
        for (int pass = 0; pass < 6; pass++)
        {
            var lists = effective(rows).All(Smart.Scripts).Select(r => new SmartRule(r))
                .Where(r => r.Action == SmartCatalog.RunActionList && r.ActionParam(1) != 0).Select(r => r.ActionParam(1)).Where(loadedLists.Add).Take(32).ToArray();
            if (lists.Length == 0)
            {
                break;
            }

            foreach (uint list in lists)
            {
                await AcInteractionSource.Load(session, rows, Smart.Scripts, "entryorguid=@k0 AND source_type=9", [(long)list], token);
            }
        }
    }

    /// <summary>
    /// Action list IDs already used in the database within [from, to] (bounded range), so a new
    /// list never collides with another script's list.
    /// </summary>
    public async Task<HashSet<int>> UsedActionListsAsync(int from, int to, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        await using var command = session.Command("SELECT DISTINCT entryorguid FROM smart_scripts WHERE source_type=9 AND entryorguid BETWEEN @k0 AND @k1 LIMIT 1000");
        command.Parameters.AddWithValue("@k0", from);
        command.Parameters.AddWithValue("@k1", to);
        var used = new HashSet<int>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            used.Add(Convert.ToInt32(reader.GetValue(0)));
        }

        return used;
    }

    /// <summary>Names for every catalog-typed SmartAI parameter in the view.</summary>
    async Task ResolveSmartNames(WorldReadSession session, RowSet view, ContentNames names, CancellationToken token)
    {
        List<uint> creatures = [], objects = [], quests = [];
        foreach (var rule in view.All(Smart.Scripts).Select(r => new SmartRule(r)))
        {
            foreach (var (type, param) in new[] { (Smart.Event(rule.Event), (Func<int, uint>)rule.EventParam), (Smart.Action(rule.Action), rule.ActionParam), (Smart.Target(rule.Target), i => i <= SmartCatalog.TargetParamCount ? rule.TargetParam(i) : 0) })
            {
                for (int i = 1; type != null && i <= type.Params.Length; i++)
                {
                    uint value = param(i);
                    switch (type.Params[i - 1].Kind)
                    {
                        case SmartParamKind.Creature:
                            creatures.Add(value);
                            break;
                        case SmartParamKind.GameObject:
                            objects.Add(value);
                            break;
                        case SmartParamKind.Quest:
                            quests.Add(value);
                            break;
                        case SmartParamKind.Item when value != 0 && client?.Item(value) is { } item:
                            names.Items[value] = item.Name;
                            break;
                    }
                }
            }
        }

        await Fill(session, "SELECT entry,COALESCE(name,'') FROM creature_template WHERE entry IN ({0})", creatures, names.Creatures, token);
        await Fill(session, "SELECT entry,COALESCE(name,'') FROM gameobject_template WHERE entry IN ({0})", objects, names.GameObjects, token);
        await Fill(session, "SELECT ID,COALESCE(LogTitle,'') FROM quest_template WHERE ID IN ({0})", quests, names.Quests, token);
    }

    static async Task Fill(WorldReadSession session, string sql, IEnumerable<uint> ids, Dictionary<uint, string> target, CancellationToken token)
    {
        var list = ids.Where(id => id != 0).Distinct().ToArray();
        if (list.Length == 0)
        {
            return;
        }

        await using var command = session.Command("");
        command.CommandText = string.Format(sql, AcInteractionSource.InList(command, list, "n"));
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            target[Convert.ToUInt32(reader.GetValue(0))] = reader.GetString(1);
        }
    }

    /// <summary>Texts of the broadcast lines the creature's speech links: the hotfixes table first, then the client's BroadcastText.db2.</summary>
    async Task ReadBroadcasts(RowSet view, uint entry, ContentNames names, CancellationToken token)
    {
        var ids = view.All(Smart.CreatureText).Where(r => r.U("CreatureID") == entry).Select(r => r.I("BroadcastTextId")).Where(id => id > 0).Select(id => (uint)id).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

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
                names.Broadcasts[id] = text.Male;
            }
            else if (client?.BroadcastText(id) is { } male)
            {
                names.Broadcasts[id] = male;
            }
        }
    }
}
