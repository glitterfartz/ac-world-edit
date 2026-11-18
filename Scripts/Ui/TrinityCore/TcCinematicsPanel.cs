using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// TrinityCore cinematics editor: the player-choice cards, their single reward row and reward items,
/// committed one validated command at a time through the shared local draft. Conversations and
/// scenes fill their tabs in later stages; until then those tabs say so.
/// </summary>
public partial class TcCinematicsPanel : CinematicsPanel
{
    public Func<IClientLookup?> Lookup { get; set; } = () => null;

    /// <summary>The player-choice read; tests replace it with a fixed snapshot.</summary>
    public Func<uint, CancellationToken, Task<PlayerChoiceSnapshot>> Reader { get; set; }

    /// <summary>The left-hand search for the current tab; tests can replace it.</summary>
    public Func<string, CancellationToken, Task<IReadOnlyList<(uint Id, string Name)>>> Searcher { get; set; }

    PlayerChoiceSnapshot? snapshot;
    RowSet effective = new();
    IReadOnlyList<InteractionIssue> issues = [];
    string sourceKey = "";
    int generation;
    TcPlayerChoiceView? view;

    public TcCinematicsPanel()
    {
        Reader = (id, token) => DBSource.Open(Connection(), Lookup()).ReadPlayerChoiceAsync(id, DraftKeys(id), Apply, catalog, token);
        Searcher = (term, token) => DBSource.Open(Connection(), Lookup()).SearchPlayerChoicesAsync(term, token);
    }

    public override WorldDialect Dialect => WorldDialect.TrinityCore;

    /// <summary>The cards of the open choice, or null before the tab is built.</summary>
    public TcPlayerChoiceView? PlayerChoice => view;

    IReadOnlyList<(TableSchema Table, string Key)> DraftKeys(uint id) =>
        Drafts.Draft?.Keys(DBSource.Open(Connection(), Lookup()).SourceKey) ?? [];

    Func<RowSet, RowSet> Apply => Drafts.Draft?.Frozen(DBSource.Open(Connection(), Lookup()).SourceKey) ?? (rows => rows);

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
        ReloadOnForeignChange(() => CurrentId.HasValue, () => _ = Load(CurrentId!.Value));
        RenderTab();
    }

    protected override void RenderTab()
    {
        if (view == null)
        {
            return;
        }

        view.Visible = CurrentTab == Tab.PlayerChoices;
        if (CurrentTab == Tab.PlayerChoices)
        {
            RenderPlayerChoice();
            return;
        }

        title.Text = CurrentTab == Tab.Conversations ? "Conversations" : "Scenes";
        subtitle.Text = CurrentTab == Tab.Conversations
            ? "The conversation editor arrives with the next stage."
            : "The scene editor arrives with the last stage.";
        UpdateStatus();
        Updated();
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

    public override void Open(uint id)
    {
        CurrentTab = Tab.PlayerChoices;
        RenderTab();
        if (id == 0)
        {
            CurrentId = null;
            snapshot = null;
            Rebuild();
            return;
        }

        _ = Load(id);
    }

    /// <summary>Read a choice (plus every draft-touched row) and redraw; stale results drop.</summary>
    public override async Task Load(uint id)
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

    protected override void Rebuild()
    {
        if (snapshot == null)
        {
            RenderPlayerChoice();
            return;
        }

        effective = Drafts.Draft?.Effective(sourceKey, snapshot.Rows) ?? snapshot.Rows;
        issues = CurrentId is uint id ? TcCinematicsRules.Check(id, effective, snapshot.Names).ToArray() : [];
        RenderPlayerChoice();
    }

    protected override void ReloadAfterHistory()
    {
        if (CurrentId is uint id)
        {
            _ = Load(id);
        }
    }

    /// <summary>
    /// Create a choice above both the database maximum and every choice already reserved in the
    /// draft, or copy the open one with its responses and rewards.
    /// </summary>
    public override async Task NewItem(uint? copyFrom)
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
                await Load(id);
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
        // The question is what the list shows, so mark the copy; fall back to the pending text when
        // the source leaves the question NULL.
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

    /// <summary>Record one validated command against the open choice; the check runs on the result.</summary>
    bool Commit(string label, RowEdit[] edits)
    {
        if (snapshot == null || CurrentId is not uint id)
        {
            return false;
        }

        var names = snapshot.Names;
        return CommitEdits(sourceKey, snapshot.Rows, effective, label, edits, rows => TcCinematicsRules.Check(id, rows, names), Rebuild, UpdateStatus);
    }

    public override Task<IReadOnlyList<(uint Id, string Name)>> Search(string term) =>
        CurrentTab == Tab.PlayerChoices
            ? Searcher(term, CancellationToken.None)
            : Task.FromResult<IReadOnlyList<(uint Id, string Name)>>([]);
}
