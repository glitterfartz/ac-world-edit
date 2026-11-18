namespace ACWorldEdit.Foundation;

/// <summary>
/// TrinityCore player-choice rules: the four tables, complete rows for new choices, responses and
/// rewards, the positions new responses and reward items take, the child rows a removal takes with
/// it, and the checks the server's loader makes. Engine- and database-independent.
/// </summary>
public static class TcCinematicsRules
{
    /// <summary>The player-choice tables in dependency order: a choice, its responses, then their rewards.</summary>
    public static readonly TableSchema[] Tables =
        [TcTables.PlayerChoice, TcTables.PlayerChoiceResponse, TcTables.PlayerChoiceResponseReward, TcTables.PlayerChoiceResponseRewardItem];

    /// <summary>
    /// A complete choice row. Question, Duration, MaxResponses and ScriptName start NULL so the client
    /// picks the wording fallback, the timing and the response cap; every other column takes its
    /// database default.
    /// </summary>
    public static Row NewChoice(uint id) =>
        Defaults(TcTables.PlayerChoice)
            .With("ChoiceId", id)
            .With("Question", null)
            .With("Duration", null)
            .With("MaxResponses", null)
            .With("ScriptName", null);

    /// <summary>
    /// A complete response row at <paramref name="index"/>. Header and Answer both take the answer
    /// text so a new card is valid before it is edited; Description and Confirmation start empty and
    /// RewardQuestID is NULL (no quest reward) rather than 0.
    /// </summary>
    public static Row NewResponse(uint choice, uint responseId, uint index, string answer) =>
        Defaults(TcTables.PlayerChoiceResponse)
            .With("ChoiceId", choice)
            .With("ResponseId", responseId)
            .With("Index", index)
            .With("Header", answer)
            .With("Answer", answer)
            .With("RewardQuestID", null);

    /// <summary>The scalar reward row of one response, all zero. Its key has no Index, so there is at most one per response.</summary>
    public static Row NewReward(uint choice, uint responseId) =>
        Defaults(TcTables.PlayerChoiceResponseReward)
            .With("ChoiceId", choice)
            .With("ResponseId", responseId);

    /// <summary>An item granted by one response's reward row; BonusListIDs stays NULL (no bonus IDs).</summary>
    public static Row NewRewardItem(uint choice, uint responseId, uint index, uint itemId, int quantity) =>
        Defaults(TcTables.PlayerChoiceResponseRewardItem)
            .With("ChoiceId", choice)
            .With("ResponseId", responseId)
            .With("Index", index)
            .With("ItemId", itemId)
            .With("Quantity", quantity)
            .With("BonusListIDs", null);

    /// <summary>The next free ResponseId of a choice (highest + 1, so a deleted response's ID is not reused).</summary>
    public static uint NextResponseId(RowSet rows, uint choice) =>
        Responses(rows, choice).Select(r => r.U("ResponseId")).DefaultIfEmpty(0u).Max() + 1;

    /// <summary>The next free card position: one past the highest Index, so a gap left by a removal is not reused.</summary>
    public static uint NextIndex(RowSet rows, uint choice) =>
        Responses(rows, choice).Select(r => r.U("Index")).DefaultIfEmpty(0u).Max() + 1;

    /// <summary>The next free reward-item position of one response.</summary>
    public static uint NextRewardItemIndex(RowSet rows, uint choice, uint responseId) =>
        RewardItems(rows, choice, responseId).Select(r => r.U("Index")).DefaultIfEmpty(0u).Max() + 1;

    /// <summary>
    /// A response and everything that only exists with it: its scalar reward row and that row's items.
    /// One command, so undo brings them all back together.
    /// </summary>
    public static RowEdit[] RemoveResponse(Row response, RowSet rows)
    {
        uint choice = response.U("ChoiceId");
        uint id = response.U("ResponseId");
        return
        [
            InteractionEdits.Delete(TcTables.PlayerChoiceResponse, response),
            .. rows.All(TcTables.PlayerChoiceResponseReward)
                .Where(r => r.U("ChoiceId") == choice && r.U("ResponseId") == id)
                .Select(r => InteractionEdits.Delete(TcTables.PlayerChoiceResponseReward, r)),
            .. RewardItems(rows, choice, id)
                .Select(r => InteractionEdits.Delete(TcTables.PlayerChoiceResponseRewardItem, r))
        ];
    }

    /// <summary>
    /// Loader checks for one choice's effective rows. <paramref name="names"/> resolves the reward
    /// quest; a null lookup skips that check. Errors are what the client cannot render or the server
    /// rejects; warnings still load.
    /// </summary>
    public static IEnumerable<InteractionIssue> Check(uint choice, RowSet rows, IContentNames? names)
    {
        if (rows.Get(TcTables.PlayerChoice, choice.ToString()) is not { } card)
        {
            yield break;
        }

        bool hasQuestion = !card.IsNull("Question") && card.T("Question").Trim().Length > 0;
        if (!hasQuestion && card.T("PendingChoiceText").Trim().Length == 0)
        {
            yield return new(true, "The choice shows no question text.");
        }

        var responses = Responses(rows, choice).ToList();
        if (responses.Count == 0)
        {
            yield return new(false, "The choice has no responses, so the client shows an empty card.");
        }

        if (!card.IsNull("MaxResponses") && card.U("MaxResponses") != 0 && responses.Count > card.U("MaxResponses"))
        {
            yield return new(false, $"Only the first {card.U("MaxResponses")} of {responses.Count} responses are shown (MaxResponses).");
        }

        foreach (var group in responses.GroupBy(r => r.U("Index")).Where(g => g.Count() > 1))
        {
            yield return new(true, $"Responses {string.Join(" and ", group.Select(r => r.U("ResponseId")))} share position {group.Key}.");
        }

        foreach (var response in responses)
        {
            uint id = response.U("ResponseId");
            if (response.T("Answer").Trim().Length == 0)
            {
                yield return new(true, $"Response {id}: the answer (button text) is empty.");
            }

            if (response.T("Header").Trim().Length == 0)
            {
                yield return new(true, $"Response {id}: the header is empty.");
            }

            // The names are a snapshot of the IDs it resolved; an ID typed after the read is absent
            // without being absent from the database, so an unresolved reference is a warning.
            if (!response.IsNull("RewardQuestID") && response.U("RewardQuestID") != 0 && names?.Quest(response.U("RewardQuestID")) == null)
            {
                yield return new(false, $"Response {id}: reward quest {response.U("RewardQuestID")} was not found in the open database; check the ID.");
            }

            foreach (var item in RewardItems(rows, choice, id))
            {
                uint itemId = item.U("ItemId");
                if (itemId == 0)
                {
                    if (item.I("Quantity") != 0)
                    {
                        yield return new(false, $"Response {id}: reward item {item.U("Index")} has an amount but no item.");
                    }

                    continue;
                }

                if (item.I("Quantity") <= 0)
                {
                    yield return new(true, $"Response {id}: reward item {itemId} needs an amount above 0.");
                }

                if (names != null && names.Item(itemId) == null)
                {
                    yield return new(false, $"Response {id}: reward item {itemId} was not found in the open database; check the ID.");
                }
            }
        }

        var known = responses.Select(r => r.U("ResponseId")).ToHashSet();
        foreach (var reward in rows.All(TcTables.PlayerChoiceResponseReward).Where(r => r.U("ChoiceId") == choice && !known.Contains(r.U("ResponseId"))))
        {
            yield return new(false, $"Reward row for response {reward.U("ResponseId")} has no response; the server ignores it.");
        }

        foreach (var item in rows.All(TcTables.PlayerChoiceResponseRewardItem).Where(r => r.U("ChoiceId") == choice && !known.Contains(r.U("ResponseId"))))
        {
            yield return new(false, $"Reward item row for response {item.U("ResponseId")} has no response; the server ignores it.");
        }
    }

    /// <summary>This choice's responses in the order the client shows them.</summary>
    public static IEnumerable<Row> Responses(RowSet rows, uint choice) =>
        rows.All(TcTables.PlayerChoiceResponse)
            .Where(r => r.U("ChoiceId") == choice)
            .OrderBy(r => r.U("Index")).ThenBy(r => r.U("ResponseId"));

    /// <summary>The source-36 conditions on one response, ordered as the server evaluates them.</summary>
    public static IEnumerable<Row> Conditions(RowSet rows, uint choice, uint responseId) =>
        rows.All(TcTables.Conditions)
            .Where(row => row.I("SourceTypeOrReferenceId") == 36 && row.U("SourceGroup") == choice && row.I("SourceEntry") == (int)responseId)
            .OrderBy(row => row.U("ElseGroup")).ThenBy(row => row.I("ConditionTypeOrReference"));

    /// <summary>This response's reward items in their listed order.</summary>
    public static IEnumerable<Row> RewardItems(RowSet rows, uint choice, uint responseId) =>
        rows.All(TcTables.PlayerChoiceResponseRewardItem)
            .Where(r => r.U("ChoiceId") == choice && r.U("ResponseId") == responseId)
            .OrderBy(r => r.U("Index"));

    /// <summary>A row with every column filled: empty text and zero numbers.</summary>
    static Row Defaults(TableSchema table) =>
        new(table.Columns.Select(c => new KeyValuePair<string, string>(c.Name, c.Kind == ColumnKind.Text ? "" : "0")));
}
