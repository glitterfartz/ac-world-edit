using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;
using System.Globalization;

namespace ACWorldEdit.Data;

/// <summary>
/// TrinityCore cinematics reads: a player choice with its responses and rewards, the condition rows
/// that gate a choice or a conversation line, and the searches the editors list. Conversations and
/// scenes join in later stages. Every call opens its own read-only snapshot transaction.
/// </summary>
public sealed class TcCinematicsSource(WorldDatabase database, IClientLookup? client)
{
    static readonly TableSchema[] ChoiceTables =
        [TcTables.PlayerChoice, TcTables.PlayerChoiceResponse, TcTables.PlayerChoiceResponseReward, TcTables.PlayerChoiceResponseRewardItem];

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
