using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Read-only snapshots for the quest and behavior (SmartAI) editors. Rows use the same whitelisted
/// schemas as drafts; draft-touched rows are loaded too, and references reachable in the effective
/// view (action lists, linked quests, picked IDs) are resolved to names in the same transaction.
/// </summary>
public sealed class AcContentSource(WorldDatabase database)
{
    public AcContentSource(DatabaseSettings settings) : this(new WorldDatabase(settings))
    {
    }

    public string SourceKey => database.SourceKey;

    async Task LoadDraftKeys(WorldReadSession session, RowSet rows, IEnumerable<(TableSchema Table, string Key)> keys, Func<TableSchema, bool> relevant, CancellationToken token)
    {
        foreach (var (table, key) in keys.Where(k => relevant(k.Table)))
        {
            var parts = key.Split('|');
            await AcInteractionSource.Load(session, rows, table, string.Join(" AND ", table.Key.Select((k, i) => $"`{k}`=@k{i}")), parts.Cast<object>().ToArray(), token);
        }
    }

    public async Task<ContentSnapshot> ReadQuestAsync(uint quest, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("quest_template.LogTitle", "quest_template_addon.PrevQuestID", "quest_request_items.CompletionText", "quest_offer_reward.RewardText", "creature_queststarter.quest");
        var rows = new RowSet();
        foreach (var table in new[] { InteractionTables.QuestTemplate, InteractionTables.QuestAddon, InteractionTables.QuestRequestItems, InteractionTables.QuestOfferReward })
        {
            await AcInteractionSource.Load(session, rows, table, "ID=@k0", [quest], token);
        }

        await AcInteractionSource.Load(session, rows, InteractionTables.QuestStarter, "quest=@k0", [quest], token);
        await AcInteractionSource.Load(session, rows, InteractionTables.QuestEnder, "quest=@k0", [quest], token);
        await LoadDraftKeys(session, rows, draftKeys, t => t == InteractionTables.QuestTemplate || t == InteractionTables.QuestAddon || t == InteractionTables.QuestRequestItems || t == InteractionTables.QuestOfferReward || t == InteractionTables.QuestStarter || t == InteractionTables.QuestEnder, token);
        var names = new ContentNames(catalog);
        await ResolveQuestNames(session, effective(rows), quest, names, token);
        uint max = Convert.ToUInt32(await AcInteractionSource.Scalar(session, "SELECT COALESCE(MAX(ID),0) FROM quest_template", [], token));
        return new ContentSnapshot(rows, names, database.SourceKey, max);
    }

    async Task ResolveQuestNames(WorldReadSession session, RowSet view, uint quest, ContentNames names, CancellationToken token)
    {
        List<uint> creatures = [], objects = [], items = [], quests = [];
        foreach (var q in view.All(InteractionTables.QuestTemplate))
        {
            for (int i = 1; i <= 4; i++)
            {
                int target = q.I($"RequiredNpcOrGo{i}");
                (target > 0 ? creatures : objects).Add((uint)Math.Abs(target));
            }

            foreach (var (prefix, slots) in new[] { ("RequiredItemId", 6), ("RewardItem", 4), ("RewardChoiceItemID", 6), ("ItemDrop", 4) })
            {
                for (int i = 1; i <= slots; i++)
                {
                    items.Add(q.U($"{prefix}{i}"));
                }
            }

            items.Add(q.U("StartItem"));
            quests.Add(q.U("RewardNextQuest"));
            quests.Add(q.U("ID"));
        }

        foreach (var addon in view.All(InteractionTables.QuestAddon))
        {
            quests.Add((uint)Math.Abs(addon.I("PrevQuestID")));
            quests.Add(addon.U("NextQuestID"));
        }

        creatures.AddRange(view.All(InteractionTables.QuestStarter).Concat(view.All(InteractionTables.QuestEnder)).Select(r => r.U("id")));
        await ResolveNames(session, names, creatures, objects, items, quests, token);
    }

    /// <summary>
    /// Fill names for the given IDs (zeros ignored), each with one bounded IN query.
    /// </summary>
    static async Task ResolveNames(WorldReadSession session, ContentNames names, IEnumerable<uint> creatures, IEnumerable<uint> objects, IEnumerable<uint> items, IEnumerable<uint> quests, CancellationToken token)
    {
        async Task Fill(string sql, IEnumerable<uint> ids, Dictionary<uint, string> target)
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

        await Fill("SELECT entry,COALESCE(name,'') FROM creature_template WHERE entry IN ({0})", creatures, names.Creatures);
        await Fill("SELECT entry,COALESCE(name,'') FROM gameobject_template WHERE entry IN ({0})", objects, names.GameObjects);
        await Fill("SELECT entry,COALESCE(name,'') FROM item_template WHERE entry IN ({0})", items, names.Items);
        await Fill("SELECT ID,COALESCE(LogTitle,'') FROM quest_template WHERE ID IN ({0})", quests, names.Quests);
    }

    /// <summary>
    /// Behavior snapshot for one creature: entry script, spawn script and every timed action list
    /// reachable through "Run action list" in the effective view (bounded), plus AIName and texts.
    /// </summary>
    public async Task<ContentSnapshot> ReadBehaviorAsync(uint entry, ulong guid, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("smart_scripts.action_type", "creature_template.AIName", "creature_text.Probability");
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

            rows.Set(InteractionTables.CreatureAI, Row.Of(("entry", reader.GetUInt32(0)), ("AIName", reader.GetString(1))));
            name = reader.GetString(2);
            script = reader.GetString(3);
        }

        await AcInteractionSource.Load(session, rows, InteractionTables.SmartScripts, "entryorguid=@k0 AND source_type=0", [(int)entry], token);
        if (guid != 0)
        {
            await AcInteractionSource.Load(session, rows, InteractionTables.SmartScripts, "entryorguid=@k0 AND source_type=0", [-(long)guid], token);
        }

        await LoadDraftKeys(session, rows, draftKeys, t => t == InteractionTables.SmartScripts || t == InteractionTables.CreatureAI || t == InteractionTables.CreatureText, token);
        await LoadActionLists(session, rows, effective, token);
        var names = new ContentNames(catalog);
        var view = effective(rows);
        await ResolveSmartNames(session, view, names, token);
        names.Creatures.TryAdd(entry, name);
        await AcInteractionSource.Load(session, rows, InteractionTables.CreatureText, "CreatureID=@k0", [entry], token);
        ContentNames.FillTexts(names, effective(rows), entry);
        await ReadBroadcasts(session, effective(rows), entry, names, token);
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
            var lists = effective(rows).All(InteractionTables.SmartScripts).Select(r => new SmartRule(r))
                .Where(r => r.Action == SmartCatalog.RunActionList && r.ActionParam(1) != 0).Select(r => r.ActionParam(1)).Where(loadedLists.Add).Take(32).ToArray();
            if (lists.Length == 0)
            {
                break;
            }

            foreach (uint list in lists)
            {
                await AcInteractionSource.Load(session, rows, InteractionTables.SmartScripts, "entryorguid=@k0 AND source_type=9", [(int)list], token);
            }
        }
    }

    /// <summary>
    /// Behavior snapshot for one AreaTrigger: its source_type 2 rules, its areatrigger_scripts row
    /// (which decides whether the rules run) and reachable action lists. The trigger row must exist,
    /// as SmartAIMgr skips rules for unknown triggers. Talk texts belong to target creatures and are
    /// not loaded here.
    /// </summary>
    public async Task<ContentSnapshot> ReadAreaTriggerBehaviorAsync(AreaTriggerOwner owner, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token)
    {
        if (owner.IsEntity)
        {
            throw new DatabaseFeatureException("AzerothCore has no server-spawned AreaTriggers.");
        }

        uint trigger = owner.Id;
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("smart_scripts.action_type", "areatrigger.entry", "areatrigger_scripts.ScriptName");
        if (Convert.ToInt64(await AcInteractionSource.Scalar(session, "SELECT COUNT(*) FROM areatrigger WHERE entry=@k0", [trigger], token)) == 0)
        {
            throw new InvalidDataException($"AreaTrigger {trigger} does not exist.");
        }

        var rows = new RowSet();
        await AcInteractionSource.Load(session, rows, InteractionTables.SmartScripts, "entryorguid=@k0 AND source_type=2", [(int)trigger], token);
        await AcInteractionSource.Load(session, rows, InteractionTables.AreaTriggerScripts, "entry=@k0", [(int)trigger], token);
        await LoadDraftKeys(session, rows, draftKeys, t => t == InteractionTables.SmartScripts || t == InteractionTables.AreaTriggerScripts, token);
        await LoadActionLists(session, rows, effective, token);
        var names = new ContentNames(catalog);
        await ResolveSmartNames(session, effective(rows), names, token);
        string script = effective(rows).Get(InteractionTables.AreaTriggerScripts, trigger.ToString(System.Globalization.CultureInfo.InvariantCulture))?.T("ScriptName") ?? "";
        uint maxQuest = Convert.ToUInt32(await AcInteractionSource.Scalar(session, "SELECT COALESCE(MAX(ID),0) FROM quest_template", [], token));
        return new ContentSnapshot(rows, names, database.SourceKey, maxQuest, $"AreaTrigger {trigger}", trigger, script);
    }

    /// <summary>
    /// Spawns of a creature entry (guid, map, server position), bounded, for locating a creature
    /// that is not loaded in the viewer. AzerothCore stores the entry in creature.id1 (older schemas
    /// use id).
    /// </summary>
    public async Task<IReadOnlyList<(ulong Guid, uint Map, double X, double Y, double Z)>> FindSpawnsAsync(uint entry, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        string column = session.Has("creature.id1") ? "id1" : "id";
        await using var command = session.Command($"SELECT guid,map,position_x,position_y,position_z FROM creature WHERE {column}=@k0 ORDER BY guid LIMIT {DbLimits.MaxRows}");
        command.Parameters.AddWithValue("@k0", entry);
        List<(ulong, uint, double, double, double)> spawns = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            spawns.Add((Convert.ToUInt64(reader.GetValue(0)), Convert.ToUInt32(reader.GetValue(1)), Convert.ToDouble(reader.GetValue(2)), Convert.ToDouble(reader.GetValue(3)), Convert.ToDouble(reader.GetValue(4))));
        }

        return spawns;
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

    /// <summary>
    /// Names for every catalog-typed SmartAI parameter in the view.
    /// </summary>
    async Task ResolveSmartNames(WorldReadSession session, RowSet view, ContentNames names, CancellationToken token)
    {
        List<uint> creatures = [], objects = [], items = [], quests = [];
        foreach (var rule in view.All(InteractionTables.SmartScripts).Select(r => new SmartRule(r)))
        {
            foreach (var (type, param) in new[] { (SmartCatalog.Ac.Event(rule.Event), (Func<int, uint>)rule.EventParam), (SmartCatalog.Ac.Action(rule.Action), rule.ActionParam), (SmartCatalog.Ac.Target(rule.Target), i => i <= 4 ? rule.TargetParam(i) : 0) })
            {
                for (int i = 1; type != null && i <= type.Params.Length; i++)
                {
                    var list = type.Params[i - 1].Kind switch
                    {
                        SmartParamKind.Creature => creatures,
                        SmartParamKind.GameObject => objects,
                        SmartParamKind.Item => items,
                        SmartParamKind.Quest => quests,
                        _ => null
                    };
                    list?.Add(param(i));
                }
            }
        }

        await ResolveNames(session, names, creatures, objects, items, quests, token);
    }

    static async Task ReadBroadcasts(WorldReadSession session, RowSet view, uint entry, ContentNames names, CancellationToken token)
    {
        var ids = view.All(InteractionTables.CreatureText).Where(r => r.U("CreatureID") == entry).Select(r => r.I("BroadcastTextId")).Where(id => id > 0).Select(id => (uint)id).Distinct().ToArray();
        if (ids.Length == 0 || !session.Has("broadcast_text.ID"))
        {
            return;
        }

        await using var command = session.Command("");
        command.CommandText = $"SELECT ID,COALESCE(MaleText,''),COALESCE(FemaleText,'') FROM broadcast_text WHERE ID IN ({AcInteractionSource.InList(command, ids, "b")})";
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            string male = reader.GetString(1), female = reader.GetString(2);
            names.Broadcasts[reader.GetUInt32(0)] = male.Length > 0 ? male : female;
        }
    }

    /// <summary>
    /// Bounded picker search over broadcast_text by ID or by (escaped LIKE) male or female text.
    /// </summary>
    public async Task<IReadOnlyList<(uint Id, string Name)>> SearchBroadcastAsync(string term, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("broadcast_text.ID", "broadcast_text.MaleText", "broadcast_text.FemaleText");
        await using var command = session.Command($"SELECT ID,CASE WHEN COALESCE(MaleText,'')<>'' THEN MaleText ELSE COALESCE(FemaleText,'') END FROM broadcast_text WHERE (MaleText LIKE @s ESCAPE '=' OR FemaleText LIKE @s ESCAPE '=') OR ID=@id ORDER BY ID=@id DESC, CHAR_LENGTH(COALESCE(MaleText,'')), ID LIMIT {DbLimits.MaxSearch}");
        WorldReadSession.AddSearch(command, term, "@s", "@id");
        List<(uint, string)> results = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            results.Add((Convert.ToUInt32(reader.GetValue(0)), reader.GetString(1)));
        }

        return results;
    }

    /// <summary>
    /// Bounded picker search over creature, object, item or quest names (escaped LIKE or exact ID).
    /// Unnamed rows only match by ID, so an empty search lists real names first.
    /// </summary>
    public async Task<IReadOnlyList<(uint Id, string Name)>> SearchAsync(SmartParamKind kind, string term, CancellationToken token)
    {
        (string table, string id, string name) = kind switch
        {
            SmartParamKind.Creature => ("creature_template", "entry", "name"),
            SmartParamKind.GameObject => ("gameobject_template", "entry", "name"),
            SmartParamKind.Item => ("item_template", "entry", "name"),
            SmartParamKind.Quest => ("quest_template", "ID", "LogTitle"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        await using var command = session.Command($"SELECT {id},COALESCE({name},'') FROM {table} WHERE ({name} LIKE @s ESCAPE '=' AND {name}<>'') OR {id}=@id ORDER BY {id}=@id DESC, CHAR_LENGTH({name}), {id} LIMIT {DbLimits.MaxSearch}");
        WorldReadSession.AddSearch(command, term, "@s", "@id");
        List<(uint, string)> results = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            results.Add((Convert.ToUInt32(reader.GetValue(0)), reader.GetString(1)));
        }

        return results;
    }
}
