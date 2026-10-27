using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Read-only snapshots for the TrinityCore quest editor, into the rows of <see cref="TcTables"/>: the
/// quest's template, addon, progress and turn-in texts, objectives and creature givers, plus every
/// draft-touched row. Creature, object and quest names come from the world database; items and
/// spells come from the client.
/// </summary>
public sealed class TcContentSource(WorldDatabase database, IClientLookup? client)
{
    static readonly TableSchema[] QuestTables = [TcTables.QuestTemplate, TcTables.QuestAddon, TcTables.QuestRequestItems, TcTables.QuestOfferReward];

    public async Task<ContentSnapshot> ReadQuestAsync(uint quest, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("quest_template.ContentTuningID", "quest_template_addon.ScriptName", "quest_request_items.EmoteOnCompleteDelay", "quest_offer_reward.RewardText", "quest_objectives.StorageIndex", "creature_queststarter.quest");
        var rows = new RowSet();
        foreach (var table in QuestTables)
        {
            await AcInteractionSource.Load(session, rows, table, "ID=@k0", [quest], token);
        }

        await AcInteractionSource.Load(session, rows, TcTables.QuestObjectives, "QuestID=@k0", [quest], token);
        await AcInteractionSource.Load(session, rows, TcTables.QuestStarter, "quest=@k0", [quest], token);
        await AcInteractionSource.Load(session, rows, TcTables.QuestEnder, "quest=@k0", [quest], token);
        foreach (var (table, key) in draftKeys.Where(k => QuestTables.Contains(k.Table) || k.Table == TcTables.QuestObjectives || k.Table == TcTables.QuestStarter || k.Table == TcTables.QuestEnder))
        {
            var parts = key.Split('|');
            await AcInteractionSource.Load(session, rows, table, string.Join(" AND ", table.Key.Select((k, i) => $"`{k}`=@k{i}")), parts.Cast<object>().ToArray(), token);
        }

        var names = new ContentNames(catalog, client);
        await ResolveNames(session, effective(rows), names, token);
        uint max = Convert.ToUInt32(await AcInteractionSource.Scalar(session, "SELECT COALESCE(MAX(ID),0) FROM quest_template", [], token));
        uint maxObjective = Convert.ToUInt32(await AcInteractionSource.Scalar(session, "SELECT COALESCE(MAX(ID),0) FROM quest_objectives", [], token));
        return new ContentSnapshot(rows, names, database.SourceKey, max, MaxObjectiveId: maxObjective);
    }

    /// <summary>
    /// The creature, object and quest names the quest's rows refer to (each by one bounded IN query);
    /// items come from the client, so an item the client lacks reads as missing.
    /// </summary>
    async Task ResolveNames(WorldReadSession session, RowSet view, ContentNames names, CancellationToken token)
    {
        HashSet<uint> creatures = [], objects = [], quests = [];
        foreach (var objective in view.All(TcTables.QuestObjectives))
        {
            uint id = (uint)Math.Max(objective.I("ObjectID"), 0);
            switch (objective.U("Type"))
            {
                case TcQuestRules.Monster or TcQuestRules.TalkTo:
                    creatures.Add(id);
                    break;
                case TcQuestRules.GameObject:
                    objects.Add(id);
                    break;
                case TcQuestRules.Item:
                    AddItem(names, id);
                    break;
            }
        }

        foreach (var q in view.All(TcTables.QuestTemplate))
        {
            quests.Add(q.U("ID"));
            quests.Add(q.U("RewardNextQuest"));
            AddItem(names, q.U("StartItem"));
            foreach (var (prefix, slots) in new[] { ("RewardItem", 4), ("RewardChoiceItemID", 6), ("ItemDrop", 4) })
            {
                for (int i = 1; i <= slots; i++)
                {
                    AddItem(names, q.U($"{prefix}{i}"));
                }
            }
        }

        foreach (var addon in view.All(TcTables.QuestAddon))
        {
            quests.Add((uint)Math.Abs(addon.I("PrevQuestID")));
            quests.Add(addon.U("NextQuestID"));
            quests.Add((uint)Math.Abs(addon.I("BreadcrumbForQuestId")));
        }

        creatures.UnionWith(view.All(TcTables.QuestStarter).Concat(view.All(TcTables.QuestEnder)).Select(r => r.U("id")));
        await Fill(session, "SELECT entry,COALESCE(name,'') FROM creature_template WHERE entry IN ({0})", creatures, names.Creatures, token);
        await Fill(session, "SELECT entry,COALESCE(name,'') FROM gameobject_template WHERE entry IN ({0})", objects, names.GameObjects, token);
        await Fill(session, "SELECT ID,COALESCE(LogTitle,'') FROM quest_template WHERE ID IN ({0})", quests, names.Quests, token);
    }

    void AddItem(ContentNames names, uint id)
    {
        if (id != 0 && client?.Item(id) is { } item)
        {
            names.Items[id] = item.Name;
        }
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

    /// <summary>Search creatures, objects and quests by name or ID in the world database; items and spells in the client.</summary>
    public async Task<IReadOnlyList<(uint Id, string Name)>> SearchAsync(SmartParamKind kind, string term, CancellationToken token)
    {
        switch (kind)
        {
            case SmartParamKind.Item:
                return client?.SearchItems(term, DbLimits.MaxSearch).Select(i => (i.Id, i.Name)).ToList() ?? [];
            case SmartParamKind.Spell:
                return client?.SearchSpells(term, DbLimits.MaxSearch) ?? [];
        }

        (string table, string id, string name) = kind switch
        {
            SmartParamKind.Creature => ("creature_template", "entry", "name"),
            SmartParamKind.GameObject => ("gameobject_template", "entry", "name"),
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
