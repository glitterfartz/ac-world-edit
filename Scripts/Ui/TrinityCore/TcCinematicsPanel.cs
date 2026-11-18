using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// TrinityCore cinematics editor: player choices and conversations, each committed one validated
/// command at a time through the shared local draft. The tabs share the window's search list; scenes
/// fill their tab in the last stage.
/// </summary>
public partial class TcCinematicsPanel : CinematicsPanel
{
    public Func<IClientLookup?> Lookup { get; set; } = () => null;

    /// <summary>The player-choice read; tests replace it with a fixed snapshot.</summary>
    public Func<uint, CancellationToken, Task<PlayerChoiceSnapshot>> Reader { get; set; }

    /// <summary>The conversation read; tests replace it with a fixed snapshot.</summary>
    public Func<uint, CancellationToken, Task<ConversationSnapshot>> ConversationReader { get; set; }

    /// <summary>The search of the current tab; tests can replace either.</summary>
    public Func<string, CancellationToken, Task<IReadOnlyList<(uint Id, string Name)>>> Searcher { get; set; }
    public Func<string, CancellationToken, Task<IReadOnlyList<(uint Id, string Name)>>> ConversationSearcher { get; set; }

    PlayerChoiceSnapshot? snapshot;
    ConversationSnapshot? conversation;
    RowSet effective = new();
    IReadOnlyList<InteractionIssue> issues = [];
    string sourceKey = "";
    int generation;
    TcPlayerChoiceView? view;
    TcConversationView? conversationView;

    public TcCinematicsPanel()
    {
        Reader = (id, token) => DBSource.Open(Connection(), Lookup()).ReadPlayerChoiceAsync(id, DraftKeys(), Apply(), catalog, token);
        ConversationReader = (id, token) => DBSource.Open(Connection(), Lookup()).ReadConversationAsync(id, DraftKeys(), Apply(), catalog, token);
        Searcher = (term, token) => DBSource.Open(Connection(), Lookup()).SearchPlayerChoicesAsync(term, token);
        ConversationSearcher = (term, token) => DBSource.Open(Connection(), Lookup()).SearchConversationsAsync(term, token);
    }

    public override WorldDialect Dialect => WorldDialect.TrinityCore;

    /// <summary>The cards of the open choice, or null before the tab is built.</summary>
    public TcPlayerChoiceView? PlayerChoice => view;
    /// <summary>The editor of the open conversation, or null before the tab is built.</summary>
    public TcConversationView? Conversation => conversationView;
    /// <summary>The database source the open rows belong to, for the smoke's draft checks.</summary>
    public string SourceKey => sourceKey;

    /// <summary>What the last choice read returned, for the smoke's failure message.</summary>
    public string SmokeDetail => snapshot == null
        ? "no snapshot"
        : $"read choices={snapshot.Rows.All(TcTables.PlayerChoice).Count} responses={snapshot.Rows.All(TcTables.PlayerChoiceResponse).Count} effective={TcCinematicsRules.Responses(effective, CurrentId ?? 0).Count()}";

    IReadOnlyList<(TableSchema Table, string Key)> DraftKeys() =>
        Drafts.Draft?.Keys(DBSource.Open(Connection(), Lookup()).SourceKey) ?? [];

    Func<RowSet, RowSet> Apply() => Drafts.Draft?.Frozen(DBSource.Open(Connection(), Lookup()).SourceKey) ?? (rows => rows);

    protected override void Build()
    {
        BuildChrome();
        view = new TcPlayerChoiceView
        {
            Commit = Commit,
            NewRequested = () => _ = NewItem(null),
            DuplicateRequested = () => _ = NewItem(CurrentId)
        };
        page.AddChild(view);
        conversationView = new TcConversationView
        {
            Commit = Commit,
            NewRequested = () => _ = NewItem(null),
            DuplicateRequested = () => _ = NewItem(CurrentId),
            FirstLinePick = PickFirstLine
        };
        page.AddChild(conversationView);
        ReloadOnForeignChange(() => CurrentId.HasValue, () => _ = Load(CurrentId!.Value));
        RenderTab();
    }

    public override void ShowTab(Tab tab)
    {
        if (CurrentTab == tab)
        {
            return;
        }

        CurrentId = null;
        snapshot = null;
        conversation = null;
        effective = new RowSet();
        issues = [];
        base.ShowTab(tab);
    }

    protected override void RenderTab()
    {
        if (view == null || conversationView == null)
        {
            return;
        }

        view.Visible = CurrentTab == Tab.PlayerChoices;
        conversationView.Visible = CurrentTab == Tab.Conversations;
        if (CurrentTab == Tab.PlayerChoices)
        {
            RenderPlayerChoice();
        }
        else if (CurrentTab == Tab.Conversations)
        {
            RenderConversation();
        }
        else
        {
            title.Text = "Scenes";
            subtitle.Text = "The scene editor arrives with the last stage.";
            UpdateStatus();
            Updated();
        }
    }

    void RenderPlayerChoice()
    {
        if (view == null)
        {
            return;
        }

        if (snapshot == null || CurrentId is not uint id)
        {
            title.Text = "Player choice";
            subtitle.Text = "Search for a choice on the left, or create one with + New choice.";
            view.Show(null, null, []);
            UpdateStatus();
            Updated();
            return;
        }

        var card = effective.Get(TcTables.PlayerChoice, id.ToString());
        title.Text = card == null ? $"Choice {id} does not exist"
            : card.IsNull("Question") || card.T("Question").Trim().Length == 0 ? card.T("PendingChoiceText") : card.T("Question");
        subtitle.Text = $"Player choice {id} · {TcCinematicsRules.Responses(effective, id).Count()} responses";
        view.Show(effective, snapshot.Names, issues);
        UpdateStatus();
        Updated();
    }

    void RenderConversation()
    {
        if (conversationView == null)
        {
            return;
        }

        if (conversation == null || CurrentId is not uint id)
        {
            title.Text = "Conversation";
            subtitle.Text = "Search for a conversation on the left, or create one with + New conversation.";
            conversationView.Show(null);
            UpdateStatus();
            Updated();
            return;
        }

        var template = effective.Get(TcTables.ConversationTemplate, id.ToString());
        title.Text = template == null ? $"Conversation {id} does not exist" : $"Conversation {id}";
        subtitle.Text = template == null
            ? "Create one with + New conversation, or search for another."
            : $"{conversation.Chain.Count} lines · {effective.All(TcTables.ConversationActors).Count(actor => actor.U("ConversationId") == id)} actors"
                + (conversation.ClientAvailable ? "" : " · client data unavailable");
        conversationView.Show(conversation with { Rows = effective });
        UpdateStatus();
        Updated();
    }

    public override void Open(uint id)
    {
        if (id == 0)
        {
            CurrentId = null;
            snapshot = null;
            conversation = null;
            Rebuild();
            return;
        }

        _ = Load(id);
    }

    /// <summary>Read the open tab's row (plus every draft-touched row) and redraw; stale results drop.</summary>
    public override Task Load(uint id) => CurrentTab switch
    {
        Tab.Conversations => LoadConversation(id),
        Tab.Scenes => Task.CompletedTask,
        _ => LoadPlayerChoice(id)
    };

    async Task LoadPlayerChoice(uint id)
    {
        int current = ++generation;
        try
        {
            await EnsureCatalog();
            var result = await Reader(id, CancellationToken.None);
            if (current != generation)
            {
                return;
            }

            sourceKey = result.SourceKey;
            if (CurrentId != id)
            {
                status = "";
            }

            CurrentId = id;
            snapshot = result;
            Rebuild();
        }
        catch (Exception error)
        {
            if (current == generation)
            {
                status = DbErrors.Safe(error);
                UpdateStatus();
            }
        }
    }

    async Task LoadConversation(uint id)
    {
        int current = ++generation;
        try
        {
            await EnsureCatalog();
            var result = await ConversationReader(id, CancellationToken.None);
            if (current != generation)
            {
                return;
            }

            sourceKey = result.SourceKey;
            if (CurrentId != id)
            {
                status = "";
            }

            CurrentId = id;
            conversation = result;
            Rebuild();
        }
        catch (Exception error)
        {
            if (current == generation)
            {
                status = DbErrors.Safe(error);
                UpdateStatus();
            }
        }
    }

    protected override void Rebuild()
    {
        if (CurrentTab == Tab.Conversations)
        {
            if (conversation == null)
            {
                RenderConversation();
                return;
            }

            effective = Drafts.Draft?.Effective(sourceKey, conversation.Rows) ?? conversation.Rows;
            issues = CurrentId is uint id ? TcConversationRules.Check(id, effective, conversation.Names, ChainLines()).ToArray() : [];
            RenderConversation();
            return;
        }

        if (snapshot == null)
        {
            RenderPlayerChoice();
            return;
        }

        effective = Drafts.Draft?.Effective(sourceKey, snapshot.Rows) ?? snapshot.Rows;
        issues = CurrentId is uint choice ? TcCinematicsRules.Check(choice, effective, snapshot.Names).ToArray() : [];
        RenderPlayerChoice();
    }

    uint[]? ChainLines() => conversation is { ClientAvailable: true } ? conversation.Chain.Select(line => line.Id).ToArray() : null;

    protected override void ReloadAfterHistory()
    {
        if (CurrentId is uint id)
        {
            _ = Load(id);
        }
    }

    /// <summary>
    /// Create a row above both the database maximum and every one already reserved in the draft, or
    /// copy the open one with its children. A copied conversation keeps its actors, not its line
    /// overrides: an override belongs to the client line, which the copy shares.
    /// </summary>
    public override Task NewItem(uint? copyFrom) => CurrentTab == Tab.Conversations ? NewConversation(copyFrom) : NewPlayerChoice(copyFrom);

    async Task NewPlayerChoice(uint? copyFrom)
    {
        int current = ++generation;
        try
        {
            var draft = Drafts.Draft;
            if (draft == null)
            {
                status = Drafts.Error ?? "Editing is disabled.";
                UpdateStatus();
                return;
            }

            var probe = await Reader(copyFrom ?? 0, CancellationToken.None);
            if (current != generation)
            {
                return;
            }

            sourceKey = probe.SourceKey;
            uint id = Math.Max(probe.MaxChoiceId, draft.MaxReserved(sourceKey, TcTables.PlayerChoice, "ChoiceId")) + 1;
            var probeView = draft.Effective(sourceKey, probe.Rows);
            List<RowEdit> edits = [new RowEdit(TcTables.PlayerChoice.Name, id.ToString(), null, NewChoiceRow(id, copyFrom, probeView))];
            if (copyFrom is uint from)
            {
                edits.AddRange(CopyResponses(from, id, probeView));
            }

            var (ok, message) = Drafts.Commit(this, sourceKey, probe.Rows, probeView,
                copyFrom == null ? $"Create player choice {id}" : $"Copy player choice {copyFrom} as {id}", edits, _ => []);
            status = ok ? (copyFrom == null ? $"Created player choice {id}. Write the question and its answers." : $"Copied as player choice {id}.") : message;
            if (ok)
            {
                await LoadPlayerChoice(id);
            }
            else
            {
                UpdateStatus();
            }
        }
        catch (Exception error)
        {
            status = DbErrors.Safe(error);
            UpdateStatus();
        }
    }

    async Task NewConversation(uint? copyFrom)
    {
        int current = ++generation;
        try
        {
            var draft = Drafts.Draft;
            if (draft == null)
            {
                status = Drafts.Error ?? "Editing is disabled.";
                UpdateStatus();
                return;
            }

            var probe = await ConversationReader(copyFrom ?? 0, CancellationToken.None);
            if (current != generation)
            {
                return;
            }

            sourceKey = probe.SourceKey;
            uint id = Math.Max(probe.MaxConversationId, draft.MaxReserved(sourceKey, TcTables.ConversationTemplate, "Id")) + 1;
            var probeView = draft.Effective(sourceKey, probe.Rows);
            List<RowEdit> edits = [];
            if (copyFrom is uint from && probeView.Get(TcTables.ConversationTemplate, from.ToString()) is { } original)
            {
                edits.Add(new RowEdit(TcTables.ConversationTemplate.Name, id.ToString(), null, new Row(original.Values).With("Id", id)));
                edits.AddRange(probeView.All(TcTables.ConversationActors)
                    .Where(actor => actor.U("ConversationId") == from)
                    .OrderBy(actor => actor.U("Idx"))
                    .Select(actor => new RowEdit(TcTables.ConversationActors.Name, $"{id}|{actor.U("Idx")}", null, actor.With("ConversationId", id))));
            }
            else
            {
                edits.Add(new RowEdit(TcTables.ConversationTemplate.Name, id.ToString(), null, TcConversationRules.NewConversation(id, 0)));
            }

            var (ok, message) = Drafts.Commit(this, sourceKey, probe.Rows, probeView,
                copyFrom == null ? $"Create conversation {id}" : $"Copy conversation {copyFrom} as {id}", edits, _ => []);
            status = ok ? (copyFrom == null ? $"Created conversation {id}. Choose its first line." : $"Copied as conversation {id}.") : message;
            if (ok)
            {
                await LoadConversation(id);
            }
            else
            {
                UpdateStatus();
            }
        }
        catch (Exception error)
        {
            status = DbErrors.Safe(error);
            UpdateStatus();
        }
    }

    /// <summary>A new choice, or a copy of the source row marked as a copy.</summary>
    static Row NewChoiceRow(uint id, uint? copyFrom, RowSet rows)
    {
        if (copyFrom is not uint from || rows.Get(TcTables.PlayerChoice, from.ToString()) is not { } original)
        {
            return TcCinematicsRules.NewChoice(id);
        }

        var copy = new Row(original.Values).With("ChoiceId", id);
        return original.IsNull("Question") || original.T("Question").Trim().Length == 0
            ? copy.With("PendingChoiceText", original.T("PendingChoiceText") + " (copy)")
            : copy.With("Question", original.T("Question") + " (copy)");
    }

    /// <summary>The source's responses under the new choice, each with its reward row and items.</summary>
    static IEnumerable<RowEdit> CopyResponses(uint from, uint id, RowSet rows)
    {
        foreach (var response in TcCinematicsRules.Responses(rows, from))
        {
            uint responseId = response.U("ResponseId");
            yield return new RowEdit(TcTables.PlayerChoiceResponse.Name, $"{id}|{responseId}", null, response.With("ChoiceId", id));
            foreach (var reward in rows.All(TcTables.PlayerChoiceResponseReward).Where(r => r.U("ChoiceId") == from && r.U("ResponseId") == responseId))
            {
                yield return new RowEdit(TcTables.PlayerChoiceResponseReward.Name, $"{id}|{responseId}", null, reward.With("ChoiceId", id));
            }

            foreach (var item in TcCinematicsRules.RewardItems(rows, from, responseId))
            {
                yield return new RowEdit(TcTables.PlayerChoiceResponseRewardItem.Name, $"{id}|{responseId}", null, item.With("ChoiceId", id));
            }
        }
    }

    /// <summary>
    /// The first-line picker: the client's conversation lines by their spoken text. Picking one
    /// changes FirstLineId; the chain redraws on the next read.
    /// </summary>
    void PickFirstLine()
    {
        RecordPicker.Show(this, "Choose the first conversation line",
            term => Task.FromResult(Lookup()?.SearchConversationLines(term, DbLimits.MaxSearch) ?? []),
            (lineId, _) => conversationView?.SetFirstLine(lineId), store: Store);
    }

    /// <summary>Record one validated command against the open row; the check runs on the result.</summary>
    bool Commit(string label, RowEdit[] edits) =>
        CurrentTab == Tab.Conversations ? CommitConversation(label, edits) : CommitPlayerChoice(label, edits);

    bool CommitPlayerChoice(string label, RowEdit[] edits)
    {
        if (snapshot == null || CurrentId is not uint id)
        {
            return false;
        }

        var names = snapshot.Names;
        return CommitEdits(sourceKey, snapshot.Rows, effective, label, edits, rows => TcCinematicsRules.Check(id, rows, names), Rebuild, UpdateStatus);
    }

    bool CommitConversation(string label, RowEdit[] edits)
    {
        if (conversation == null || CurrentId is not uint id)
        {
            return false;
        }

        var names = conversation.Names;
        return CommitEdits(sourceKey, conversation.Rows, effective, label, edits,
            rows => TcConversationRules.Check(id, rows, names, ChainLines()), Rebuild, UpdateStatus);
    }

    public override Task<IReadOnlyList<(uint Id, string Name)>> Search(string term) => CurrentTab switch
    {
        Tab.Conversations => ConversationSearcher(term, CancellationToken.None),
        Tab.Scenes => Task.FromResult<IReadOnlyList<(uint Id, string Name)>>([]),
        _ => Searcher(term, CancellationToken.None)
    };
}
