using ACWorldEdit.Foundation;
using Xunit;

/// <summary>
/// DraftHistory persistence and creature/graveyard net-change and discard semantics, on disposable temp paths.
/// </summary>
public class DraftHistoryTests
{
    static string TempPath(string name = "draft.json") => Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), name);

    static readonly WorldPose Base = new(0, 100, 200, 30, 1);

    [Fact]
    public void AppendUndoRedoTrackCursorAndFlags()
    {
        using var history = new DraftHistory<string>(TempPath(), "test", e => e);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        history.Undo();
        history.Redo();
        Assert.Equal(0, history.State.Cursor);

        history.Append("a");
        history.Append("b");
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        history.Undo();
        Assert.True(history.CanRedo);
        Assert.Equal(["a"], history.State.AppliedEntries());
        history.Redo();
        Assert.Equal(["a", "b"], history.State.AppliedEntries());
    }

    [Fact]
    public void ClearDropsHistoryAndPersists()
    {
        string path = TempPath();
        using (var history = new DraftHistory<string>(path, "test", e => e))
        {
            history.Append("a");
            history.Append("b");
            history.Undo();
            history.Clear();
            Assert.False(history.CanUndo);
            Assert.False(history.CanRedo);
        }

        using var reopened = new DraftHistory<string>(path, "test", e => e);
        Assert.Empty(reopened.State.History);
    }

    [Fact]
    public void AppendAfterUndoDiscardsRedoBranch()
    {
        using var history = new DraftHistory<string>(TempPath(), "test", e => e);
        history.Append("a");
        history.Append("b");
        history.Undo();
        history.Append("c");
        Assert.False(history.CanRedo);
        Assert.Equal(["a", "c"], history.State.History);
    }

    [Fact]
    public void ReopenRestoresHistoryAndCursor()
    {
        string path = TempPath();
        using (var history = new DraftHistory<string>(path, "test", e => e))
        {
            history.Append("a");
            history.Append("b");
            history.Undo();
        }

        using var reopened = new DraftHistory<string>(path, "test", e => e);
        Assert.Equal(["a", "b"], reopened.State.History);
        Assert.Equal(1, reopened.State.Cursor);
        Assert.True(reopened.CanRedo);
    }

    [Fact]
    public void SecondWriterIsRejectedUntilFirstDisposed()
    {
        string path = TempPath();
        var first = new DraftHistory<string>(path, "test", e => e);
        Assert.Throws<IOException>(() => new DraftHistory<string>(path, "test", e => e));
        first.Dispose();
        new DraftHistory<string>(path, "test", e => e).Dispose();
    }

    [Fact]
    public void TamperedPayloadIsRejectedAndFileKept()
    {
        string path = TempPath();
        using (var history = new DraftHistory<string>(path, "test", e => e))
        {
            history.Append("needle");
        }

        string original = File.ReadAllText(path);
        string tampered = original.Replace("needle", "nzedle");
        Assert.NotEqual(original, tampered);
        File.WriteAllText(path, tampered);
        Assert.Throws<InvalidDataException>(() => new DraftHistory<string>(path, "test", e => e));
        Assert.Equal(tampered, File.ReadAllText(path));
        // The lock is released after a failed recovery, so the file can be inspected or replaced.
        File.WriteAllText(path, original);
        using var recovered = new DraftHistory<string>(path, "test", e => e);
        Assert.Equal(["needle"], recovered.State.History);
    }

    [Fact]
    public void NonEnvelopeJsonIsRejected()
    {
        string path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "[\"only-one\"]");
        Assert.Throws<InvalidDataException>(() => new DraftHistory<string>(path, "test", e => e));
    }

    [Fact]
    public void EntryValidationRejectsRecoveredHistory()
    {
        string path = TempPath();
        using (var history = new DraftHistory<string>(path, "test", e => e))
        {
            history.Append("bad");
        }

        Assert.Throws<InvalidDataException>(() => new DraftHistory<string>(path, "test", e => e == "bad" ? throw new InvalidDataException("no") : e));
    }

    [Fact]
    public void NoTemporaryFileRemainsAfterSave()
    {
        string path = TempPath();
        using var history = new DraftHistory<string>(path, "test", e => e);
        history.Append("a");
        Assert.False(File.Exists(path + ".tmp"));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void NetMovesCollapseIntermediateMovesToFirstBaselineAndLastResult()
    {
        using var draft = new CreatureDraft(TempPath());
        draft.Move("s", 1, Base, Base with { X = 110 });
        draft.Move("s", 1, Base, Base with { X = 120 });
        var net = Assert.Single(draft.NetMoves());
        Assert.Equal(Base, net.Before);
        Assert.Equal(Base with { X = 120 }, net.After);
    }

    [Fact]
    public void NetMovesOmitMovesThatReturnToBaselineAndUndoneMoves()
    {
        using var draft = new CreatureDraft(TempPath());
        draft.Move("s", 1, Base, Base with { X = 110 });
        draft.Move("s", 1, Base, Base);
        Assert.Empty(draft.NetMoves());
        draft.Move("s", 1, Base, Base with { X = 110 });
        draft.Move("s", 2, Base, Base with { Y = 5 });
        draft.Undo();
        var net = Assert.Single(draft.NetMoves());
        Assert.Equal(1u, net.Guid);
        draft.Undo();
        Assert.Empty(draft.NetMoves());
    }

    [Fact]
    public void NoOpMoveAddsNoHistory()
    {
        using var draft = new CreatureDraft(TempPath());
        draft.Move("s", 1, Base, Base);
        Assert.False(draft.CanUndo);
    }

    [Fact]
    public void NetMovesSeparateSourcesAndGuids()
    {
        using var draft = new CreatureDraft(TempPath());
        draft.Move("a", 1, Base, Base with { X = 1 });
        draft.Move("b", 1, Base, Base with { X = 2 });
        draft.Move("a", 2, Base, Base with { X = 3 });
        Assert.Equal(3, draft.NetMoves().Count);
    }

    [Fact]
    public void RevertDiscardsOneCreatureAsUndoableCommandWithoutTouchingOthers()
    {
        using var draft = new CreatureDraft(TempPath());
        draft.Move("s", 1, Base, Base with { X = 110 });
        draft.Move("s", 2, Base, Base with { X = 120 });
        var one = draft.NetMoves().Single(m => m.Guid == 1);
        draft.Revert(one);
        Assert.Equal(Base, draft.Effective("s", 1, Base));
        Assert.Equal(Base with { X = 120 }, draft.Effective("s", 2, Base));
        Assert.Equal(2u, Assert.Single(draft.NetMoves()).Guid);

        // Discard is a normal command: undo brings the move back.
        draft.Undo();
        Assert.Equal(Base with { X = 110 }, draft.Effective("s", 1, Base));
        Assert.Equal(2, draft.NetMoves().Count);
    }

    [Fact]
    public void MoveRejectsCrossMapNonFiniteAndStaleBaseline()
    {
        using var draft = new CreatureDraft(TempPath());
        Assert.Throws<InvalidDataException>(() => draft.Move("s", 1, Base, Base with { Map = 1 }));
        Assert.Throws<ArgumentException>(() => draft.Move("s", 1, Base, Base with { X = double.NaN }));
        draft.Move("s", 1, Base, Base with { X = 110 });
        Assert.Throws<InvalidDataException>(() => draft.Move("s", 1, Base with { X = 101 }, Base));
    }

    [Fact]
    public void GraveyardNetEditsCollapseAndRevert()
    {
        var source = new GraveyardRecord(4, 0, 1, 2, 3, "GY", [new(12, 0, "")]);
        using var draft = new GraveyardDraft(TempPath());
        draft.Edit("db", source, source with { X = 5 });
        draft.Edit("db", source, source with { X = 6, Comment = "New" });
        var net = Assert.Single(draft.NetEdits());
        Assert.Equal(source.Normalized(), net.Before);
        Assert.Equal(6, net.After.X);
        draft.Revert(net);
        Assert.Empty(draft.NetEdits());
        Assert.Equal(source.Normalized(), draft.Effective("db", source));
    }
}
