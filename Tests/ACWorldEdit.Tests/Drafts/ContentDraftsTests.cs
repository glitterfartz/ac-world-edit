using ACWorldEdit.Foundation;
using Xunit;

/// <summary>
/// ContentDrafts commit/undo/discard behavior: new-error rejection, the Changed event, a corrupt or
/// locked draft file disabling editing without touching it. Temp paths only.
/// </summary>
public class ContentDraftsTests
{
    static string TempPath() => Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "interactions.json");

    static Row Template(uint npcflag = 0) => Row.Of(("entry", 42u), ("npcflag", npcflag), ("gossip_menu_id", 0u), ("subname", ""));

    static RowEdit Edit(uint from, uint to) => new("creature_template", "42", Template(from), Template(to));

    static RowSet Snapshot()
    {
        var rows = new RowSet();
        rows.Set(InteractionTables.CreatureTemplate, Template());
        return rows;
    }

    static IEnumerable<InteractionIssue> NoIssues(RowSet rows) => [];

    /// <summary>
    /// Error whenever npcflag is 0x8000 or more, so "introduced" is decided by the edit.
    /// </summary>
    static IEnumerable<InteractionIssue> HighFlagIsError(RowSet rows)
    {
        if (rows.Get(InteractionTables.CreatureTemplate, "42")!.U("npcflag") >= 0x8000)
        {
            yield return new(true, "flag too high");
        }
    }

    [Fact]
    public void CommitSavesAndRaisesChangedWithTheSender()
    {
        using var drafts = new ContentDrafts(TempPath());
        object? seen = null;
        int calls = 0;
        drafts.Changed += s =>
        {
            seen = s;
            calls++;
        };
        var snapshot = Snapshot();
        var sender = new object();
        var (ok, message) = drafts.Commit(sender, "s", snapshot, snapshot, "Set flag", [Edit(0, 3)], NoIssues);
        Assert.True(ok);
        Assert.Contains("Set flag", message);
        Assert.Same(sender, seen);
        Assert.Equal(1, calls);
        Assert.Equal(3u, drafts.Draft!.Effective("s", snapshot).Get(InteractionTables.CreatureTemplate, "42")!.U("npcflag"));
    }

    [Fact]
    public void CommitRejectsAnEditThatIntroducesANewError()
    {
        using var drafts = new ContentDrafts(TempPath());
        int calls = 0;
        drafts.Changed += _ => calls++;
        var snapshot = Snapshot();
        var (ok, message) = drafts.Commit(null, "s", snapshot, snapshot, "Too high", [Edit(0, 0x8000)], HighFlagIsError);
        Assert.False(ok);
        Assert.Equal("Not saved: flag too high", message);
        Assert.Equal(0, calls);
        Assert.False(drafts.Draft!.CanUndo);
    }

    [Fact]
    public void CommitAllowsEditsWhileAnExistingErrorPersists()
    {
        using var drafts = new ContentDrafts(TempPath());
        var snapshot = new RowSet();
        snapshot.Set(InteractionTables.CreatureTemplate, Template(0x8000));
        var (ok, _) = drafts.Commit(null, "s", snapshot, snapshot, "Still high", [Edit(0x8000, 0x8001)], HighFlagIsError);
        Assert.True(ok);
    }

    [Fact]
    public void CommitReportsStaleBeforeRowsWithoutThrowing()
    {
        using var drafts = new ContentDrafts(TempPath());
        var snapshot = Snapshot();
        var (ok, message) = drafts.Commit(null, "s", snapshot, snapshot, "Stale", [Edit(7, 8)], NoIssues);
        Assert.False(ok);
        Assert.StartsWith("Not saved: ", message);
    }

    [Fact]
    public void CommitDoesNotMutateTheEffectiveRowsItWasGiven()
    {
        using var drafts = new ContentDrafts(TempPath());
        var snapshot = Snapshot();
        var effective = snapshot.Clone();
        drafts.Commit(null, "s", snapshot, effective, "x", [Edit(0, 3)], NoIssues);
        Assert.Equal(0u, effective.Get(InteractionTables.CreatureTemplate, "42")!.U("npcflag"));
    }

    [Fact]
    public void HistoryUndoRedoReportLabelsAndNotifyEveryTime()
    {
        using var drafts = new ContentDrafts(TempPath());
        int calls = 0;
        drafts.Changed += _ => calls++;
        var snapshot = Snapshot();
        drafts.Commit(null, "s", snapshot, snapshot, "Set flag", [Edit(0, 3)], NoIssues);
        Assert.Equal("Undid \"Set flag\".", drafts.History(null, redo: false));
        Assert.Equal("Redid \"Set flag\".", drafts.History(null, redo: true));
        // Nothing left to redo: no label, but the call still completes and notifies.
        Assert.Equal("", drafts.History(null, redo: true));
        Assert.Equal(4, calls);
    }

    [Fact]
    public void RevertDiscardsOneRowAndNotifiesWithNoSender()
    {
        using var drafts = new ContentDrafts(TempPath());
        object? seen = new object();
        drafts.Changed += s => seen = s;
        var snapshot = Snapshot();
        drafts.Commit(null, "s", snapshot, snapshot, "Set flag", [Edit(0, 3)], NoIssues);
        var net = Assert.Single(drafts.Draft!.NetEdits());
        Assert.True(drafts.Revert(net.Source, net.Edit));
        Assert.Null(seen);
        Assert.Empty(drafts.Draft.NetEdits());
    }

    [Fact]
    public void ALockedDraftDisablesEditingAndKeepsTheFile()
    {
        string path = TempPath();
        using var first = new ContentDrafts(path);
        using var second = new ContentDrafts(path);
        Assert.Null(first.Error);
        Assert.Null(second.Draft);
        Assert.NotNull(second.Error);
        var snapshot = Snapshot();
        var (ok, message) = second.Commit(null, "s", snapshot, snapshot, "x", [Edit(0, 3)], NoIssues);
        Assert.False(ok);
        Assert.Equal(second.Error, message);
        Assert.Equal(second.Error, second.History(null, redo: false));
        Assert.False(second.Revert("s", Edit(0, 3)));
    }

    [Fact]
    public void ACorruptDraftDisablesEditingAndIsNotOverwritten()
    {
        string path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "garbage");
        using (var drafts = new ContentDrafts(path))
        {
            Assert.Null(drafts.Draft);
            Assert.Contains("Cannot open", drafts.Error);
        }

        Assert.Equal("garbage", File.ReadAllText(path));
    }

    [Fact]
    public void ReopenReleasesTheLockAndRecoversSavedHistory()
    {
        string path = TempPath();
        using var drafts = new ContentDrafts(path);
        var snapshot = Snapshot();
        drafts.Commit(null, "s", snapshot, snapshot, "Set flag", [Edit(0, 3)], NoIssues);
        drafts.Reopen();
        Assert.Null(drafts.Error);
        Assert.Single(drafts.Draft!.NetEdits());
    }
}
