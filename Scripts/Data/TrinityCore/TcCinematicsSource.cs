using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;
using System.Globalization;

namespace ACWorldEdit.Data;

/// <summary>
/// TrinityCore cinematics reads: a player choice with its responses and rewards, a conversation with
/// its actors, client line chain and overrides, the condition rows that gate a choice or a line, and
/// the searches the editors list. Scenes join in the last stage. Every call opens its own read-only
/// snapshot transaction.
/// </summary>
public sealed class TcCinematicsSource(WorldDatabase database, IClientLookup? client, TcHotfixSource hotfixes)
{
    static readonly TableSchema[] ChoiceTables =
        [TcTables.PlayerChoice, TcTables.PlayerChoiceResponse, TcTables.PlayerChoiceResponseReward, TcTables.PlayerChoiceResponseRewardItem];

    static readonly TableSchema[] ConversationTables =
        [TcTables.ConversationTemplate, TcTables.ConversationActors, TcTables.ConversationLineTemplate];

    public async Task<PlayerChoiceSnapshot> ReadPlayerChoiceAsync(uint choiceId, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("playerchoice.ChoiceId", "playerchoice_response.ResponseId", "playerchoice_response_reward.Money", "playerchoice_response_reward_item.BonusListIDs");
        var rows = new RowSet();
        foreach (var table in ChoiceTables)
        {
            await AcInteractionSource.Load(session, rows, table, "ChoiceId=@k0", [choiceId], token);
        }

        // A staged response or reward still names its parent, so reload it by its own key.
        foreach (var (table, key) in draftKeys.Where(k => ChoiceTables.Contains(k.Table)))
        {
            var parts = key.Split('|');
            await AcInteractionSource.Load(session, rows, table, string.Join(" AND ", table.Key.Select((k, i) => $"`{k}`=@k{i}")), parts.Cast<object>().ToArray(), token);
        }

        var names = new ContentNames(catalog, client);
        await ResolveNames(session, effective(rows), names, token);
        uint max = Convert.ToUInt32(await AcInteractionSource.Scalar(session, "SELECT COALESCE(MAX(ChoiceId),0) FROM playerchoice", [], token), CultureInfo.InvariantCulture);
        return new PlayerChoiceSnapshot(rows, names, database.SourceKey, max);
    }

    /// <summary>
    /// One conversation: the template, its actors and line overrides, the client chain it plays (with
    /// the server's hotfix lines and broadcast texts ahead of the client), and the source-29
    /// conditions grouped by line ID.
    /// </summary>
    public async Task<ConversationSnapshot> ReadConversationAsync(uint id, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, InteractionCatalog? catalog, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("conversation_template.FirstLineId", "conversation_actors.Idx", "conversation_line_template.ActorIdx");
        var rows = new RowSet();
        await AcInteractionSource.Load(session, rows, TcTables.ConversationTemplate, "Id=@k0", [id], token);
        await AcInteractionSource.Load(session, rows, TcTables.ConversationActors, "ConversationId=@k0", [id], token);
        foreach (var (table, key) in draftKeys.Where(k => ConversationTables.Contains(k.Table)))
        {
            var parts = key.Split('|');
            await AcInteractionSource.Load(session, rows, table, string.Join(" AND ", table.Key.Select((k, i) => $"`{k}`=@k{i}")), parts.Cast<object>().ToArray(), token);
        }

        uint firstLine = rows.Get(TcTables.ConversationTemplate, id.ToString())?.U("FirstLineId") ?? 0;
        var (chain, clientAvailable) = await ReadChainAsync(firstLine, token);
        if (chain.Count > 0)
        {
            string lines = string.Join(',', chain.Select(line => line.Id));
            await AcInteractionSource.Load(session, rows, TcTables.ConversationLineTemplate, $"Id IN ({lines})", [], token);
            await AcInteractionSource.Load(session, rows, TcTables.Conditions,
                $"SourceTypeOrReferenceId=29 AND SourceGroup=0 AND SourceEntry IN ({lines})", [], token);
        }

        var view = effective(rows);
        var names = new ContentNames(catalog, client);
        await ResolveConversationNames(session, view, names, token);
        var conditions = view.All(TcTables.Conditions)
            .GroupBy(row => (uint)Math.Max(row.I("SourceEntry"), 0))
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Row>)group
                .OrderBy(row => row.U("ElseGroup")).ThenBy(row => row.I("ConditionTypeOrReference")).ToList());
        var resolved = chain.Select(line => line with { Override = view.Get(TcTables.ConversationLineTemplate, line.Id.ToString()) }).ToList();
        uint max = Convert.ToUInt32(await AcInteractionSource.Scalar(session, "SELECT COALESCE(MAX(Id),0) FROM conversation_template", [], token), CultureInfo.InvariantCulture);
        return new ConversationSnapshot(rows, names, database.SourceKey, max, resolved, conditions, clientAvailable);
    }

    /// <summary>
    /// Walk the client chain from its first line, preferring the server's hotfix line and hotfix
    /// broadcast text. A cycle or an absurdly long chain ends the walk; ClientAvailable says whether
    /// any line at all could be read.
    /// </summary>
    async Task<(IReadOnlyList<ConversationLineView> Chain, bool ClientAvailable)> ReadChainAsync(uint firstLineId, CancellationToken token)
    {
        var hotfixLines = await hotfixes.ReadConversationLinesAsync(token).ConfigureAwait(false);
        List<ClientConversationLine> steps = [];
        HashSet<uint> seen = [];
        uint? next = firstLineId == 0 ? null : firstLineId;
        bool available = firstLineId != 0 && (hotfixLines.ContainsKey(firstLineId) || client?.ConversationLine(firstLineId) != null);
        while (next is uint lineId && lineId != 0 && seen.Add(lineId) && steps.Count < 512)
        {
            if ((hotfixLines.TryGetValue(lineId, out var overridden) ? overridden : client?.ConversationLine(lineId)) is not { } line)
            {
                break;
            }

            steps.Add(line);
            next = line.NextLineId == 0 ? null : line.NextLineId;
        }

        var broadcasts = steps.Select(step => step.BroadcastTextId).Where(broadcast => broadcast != 0).Distinct().ToArray();
        var hotfixTexts = await hotfixes.ReadBroadcastTextsAsync(broadcasts, token).ConfigureAwait(false);
        var chain = steps.Select(step => new ConversationLineView(step.Id, step.NextLineId, step.BroadcastTextId, step.AnimKitId, step.AdditionalDuration,
            hotfixTexts.TryGetValue(step.BroadcastTextId, out var text) ? text.Male : client?.BroadcastText(step.BroadcastTextId) ?? "", null)).ToList();
        return (chain, available);
    }

    /// <summary>One scene: its template and the client script package it names.</summary>
    public async Task<SceneSnapshot> ReadSceneAsync(uint id, IReadOnlyList<(TableSchema Table, string Key)> draftKeys, Func<RowSet, RowSet> effective, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("scene_template.ScriptPackageID");
        var rows = new RowSet();
        await AcInteractionSource.Load(session, rows, TcTables.SceneTemplate, "SceneId=@k0", [id], token);
        foreach (var (table, key) in draftKeys.Where(k => k.Table == TcTables.SceneTemplate))
        {
            await AcInteractionSource.Load(session, rows, table, "SceneId=@k0", [key], token);
        }

        var view = effective(rows);
        var package = view.Get(TcTables.SceneTemplate, id.ToString()) is { } scene ? client?.SceneScriptPackage(scene.U("ScriptPackageID")) : null;
        uint max = Convert.ToUInt32(await AcInteractionSource.Scalar(session, "SELECT COALESCE(MAX(SceneId),0) FROM scene_template", [], token), CultureInfo.InvariantCulture);
        return new SceneSnapshot(rows, database.SourceKey, max, package);
    }

    /// <summary>Scenes by ID or script name (bounded); the name is the script name, or "Scene N" when it has none.</summary>
    public async Task<IReadOnlyList<(uint Id, string Name)>> SearchScenesAsync(string term, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("scene_template.ScriptPackageID");
        await using var command = session.Command($"SELECT SceneId,COALESCE(ScriptName,'') FROM scene_template WHERE SceneId=@id OR ScriptName LIKE @s ESCAPE '=' ORDER BY SceneId=@id DESC, SceneId LIMIT {DbLimits.MaxSearch}");
        WorldReadSession.AddSearch(command, term, "@s", "@id");
        List<(uint, string)> scenes = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            uint id = reader.GetUInt32(0);
            string name = reader.GetString(1);
            scenes.Add((id, name.Length > 0 ? name : $"Scene {id}"));
        }

        return scenes;
    }

    /// <summary>The conversation search: by ID, or by the text of the first line (hotfix text first, then the client).</summary>
    public async Task<IReadOnlyList<(uint Id, string Name)>> SearchConversationsAsync(string term, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("conversation_template.FirstLineId");
        await using var command = session.Command($"SELECT Id,FirstLineId FROM conversation_template ORDER BY Id LIMIT {DbLimits.MaxRows}");
        List<(uint Id, uint First)> templates = [];
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                templates.Add((reader.GetUInt32(0), reader.GetUInt32(1)));
            }
        }

        term = term.Trim();
        bool byId = uint.TryParse(term, out uint number);
        var hotfixLines = await hotfixes.ReadConversationLinesAsync(token).ConfigureAwait(false);
        uint BroadcastOf(uint first) =>
            hotfixLines.TryGetValue(first, out var line) ? line.BroadcastTextId : client?.ConversationLine(first)?.BroadcastTextId ?? 0;
        var hotfixTexts = await hotfixes.ReadBroadcastTextsAsync(templates.Select(t => BroadcastOf(t.First)).Where(b => b != 0).Distinct().ToArray(), token).ConfigureAwait(false);

        List<(uint, string)> found = [];
        foreach (var (id, first) in templates)
        {
            uint broadcast = BroadcastOf(first);
            string text = hotfixTexts.TryGetValue(broadcast, out var hotfixText) ? hotfixText.Male : client?.BroadcastText(broadcast) ?? "";
            if ((byId && id == number) || (!byId && term.Length > 0 && text.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                found.Add((id, text.Length > 0 ? text : $"Conversation {id}"));
                if (found.Count >= DbLimits.MaxSearch)
                {
                    break;
                }
            }
        }

        return found;
    }

    /// <summary>The actor creature names and which actor spawn GUIDs exist, for the rules and the cards.</summary>
    async Task ResolveConversationNames(WorldReadSession session, RowSet view, ContentNames names, CancellationToken token)
    {
        var creatures = view.All(TcTables.ConversationActors).Select(actor => actor.U("CreatureId")).ToHashSet();
        await Fill(session, "SELECT entry,COALESCE(name,'') FROM creature_template WHERE entry IN ({0})", creatures, names.Creatures, token);
        var guids = view.All(TcTables.ConversationActors).Select(actor => actor.UL("ConversationActorGuid")).Where(guid => guid != 0).Distinct().ToArray();
        if (guids.Length == 0)
        {
            return;
        }

        await using var command = session.Command("");
        command.CommandText = $"SELECT guid FROM creature WHERE guid IN ({WorldReadSession.InList(command, guids, "g")})";
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            names.Spawns.Add(Convert.ToUInt64(reader.GetValue(0), CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// The condition rows on one source. Player-choice responses use source 36 with the choice as
    /// SourceGroup and a null entry (every response); conversation lines use source 29 with group 0
    /// and the line as SourceEntry.
    /// </summary>
    public async Task<IReadOnlyList<Row>> ReadCinematicConditionsAsync(int sourceType, int sourceGroup, int? sourceEntry, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        session.Require("conditions.SourceTypeOrReferenceId", "conditions.SourceGroup", "conditions.SourceEntry");
        var rows = new RowSet();
        string predicate = sourceEntry is null
            ? "SourceTypeOrReferenceId=@k0 AND SourceGroup=@k1"
            : "SourceTypeOrReferenceId=@k0 AND SourceGroup=@k1 AND SourceEntry=@k2";
        object[] values = sourceEntry is null ? [sourceType, sourceGroup] : [sourceType, sourceGroup, sourceEntry.Value];
        await AcInteractionSource.Load(session, rows, TcTables.Conditions, predicate, values, token);
        return rows.All(TcTables.Conditions).OrderBy(r => r.U("ElseGroup")).ThenBy(r => r.I("ConditionTypeOrReference")).ThenBy(r => r.I("SourceEntry")).ToList();
    }

    /// <summary>Player choices by ID, question or pending-choice text (bounded). The name is the question shown to the player.</summary>
    public async Task<IReadOnlyList<(uint Id, string Name)>> SearchPlayerChoicesAsync(string term, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        await using var command = session.Command($"SELECT ChoiceId,COALESCE(NULLIF(Question,''),PendingChoiceText,'') FROM playerchoice WHERE Question LIKE @s ESCAPE '=' OR PendingChoiceText LIKE @s ESCAPE '=' OR ChoiceId=@id ORDER BY ChoiceId=@id DESC, CHAR_LENGTH(COALESCE(NULLIF(Question,''),PendingChoiceText,'')), ChoiceId LIMIT {DbLimits.MaxSearch}");
        WorldReadSession.AddSearch(command, term, "@s", "@id");
        List<(uint, string)> choices = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            choices.Add((reader.GetUInt32(0), reader.GetString(1)));
        }

        return choices;
    }

    /// <summary>The quest titles and item names the choice's rows refer to; items come from the client, as in the quest editor.</summary>
    async Task ResolveNames(WorldReadSession session, RowSet view, ContentNames names, CancellationToken token)
    {
        var quests = view.All(TcTables.PlayerChoiceResponse)
            .Where(r => !r.IsNull("RewardQuestID"))
            .Select(r => r.U("RewardQuestID"))
            .ToHashSet();
        foreach (var item in view.All(TcTables.PlayerChoiceResponseRewardItem))
        {
            AddItem(names, item.U("ItemId"));
        }

        await Fill(session, "SELECT ID,COALESCE(LogTitle,'') FROM quest_template WHERE ID IN ({0})", quests, names.Quests, token);
    }

    void AddItem(ContentNames names, uint id)
    {
        if (id != 0 && client?.Item(id) is { } item)
        {
            names.Items[id] = item.Name;
            if (client.ItemDetails(id) is { } details)
            {
                names.ItemDetails[id] = details;
            }
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
            target[Convert.ToUInt32(reader.GetValue(0), CultureInfo.InvariantCulture)] = reader.GetString(1);
        }
    }
}
