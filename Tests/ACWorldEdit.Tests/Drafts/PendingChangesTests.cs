using ACWorldEdit.Foundation;
using Xunit;

/// <summary>
/// PendingChanges listing order, fingerprint stability and exported-set persistence.
/// </summary>
public class PendingChangesTests
{
    static string TempPath() => Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "exported.json");

    static readonly WorldPose Base = new(0, 100, 200, 30, 1);

    static CreatureMove Move(uint guid, double x = 110, string source = "s") => new(source, guid, Base, Base with { X = x });

    static GraveyardEdit Edit(uint id)
    {
        var before = new GraveyardRecord(id, 0, 1, 2, 3, "GY", []);
        return new("s", id, before, before with { X = 9 });
    }

    static List<PendingChange> Collect(PendingChanges pending, IEnumerable<CreatureMove> moves) => pending.Collect(moves, [], []);

    [Fact]
    public void ListsCreaturesByGuidThenGraveyardsById()
    {
        var pending = new PendingChanges(TempPath());
        var list = pending.Collect([Move(9), Move(2)], [Edit(7), Edit(3)], []);
        Assert.Equal([PendingKind.Creature, PendingKind.Creature, PendingKind.Graveyard, PendingKind.Graveyard], list.Select(c => c.Kind));
        Assert.Equal([2u, 9u], list.Take(2).Select(c => c.Move!.Guid));
        Assert.Equal([3u, 7u], list.Skip(2).Select(c => c.Graveyard!.Id));
        Assert.All(list, c => Assert.False(string.IsNullOrEmpty(c.Label)));
    }

    [Fact]
    public void IdIsStableAcrossCallsAndChangesWithTheNetChange()
    {
        var pending = new PendingChanges(TempPath());
        string first = Collect(pending, [Move(1)]).Single().Id;
        Assert.Equal(first, Collect(pending, [Move(1)]).Single().Id);
        Assert.NotEqual(first, Collect(pending, [Move(1, 111)]).Single().Id);
        Assert.NotEqual(first, Collect(pending, [Move(1, 110, "other")]).Single().Id);
        Assert.NotEqual(first, Collect(pending, [Move(2)]).Single().Id);
    }

    [Fact]
    public void ExportedChangeLeavesListUntilItChangesAgain()
    {
        var pending = new PendingChanges(TempPath());
        var all = Collect(pending, [Move(1), Move(2)]);
        pending.MarkExported([all[0]]);
        Assert.Equal(2u, Assert.Single(Collect(pending, [Move(1), Move(2)])).Move!.Guid);
        // A further edit to the exported creature gets a new fingerprint and is pending again.
        Assert.Equal(2, Collect(pending, [Move(1, 150), Move(2)]).Count);
    }

    [Fact]
    public void ExportedSetPersistsAcrossSessions()
    {
        string path = TempPath();
        var first = new PendingChanges(path);
        first.MarkExported(Collect(first, [Move(1)]));
        Assert.Empty(Collect(new PendingChanges(path), [Move(1)]));
    }

    [Fact]
    public void CorruptExportedFileMeansEverythingIsPendingAgain()
    {
        string path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{not json");
        Assert.Single(Collect(new PendingChanges(path), [Move(1)]));
    }

    [Fact]
    public void EmptyInputsYieldNothing()
    {
        Assert.Empty(new PendingChanges(TempPath()).Collect([], [], []));
    }
}

/// <summary>
/// Row (interaction/content) net changes, discard and their appearance in PendingChanges.
/// </summary>
public class RowDraftNetChangeTests
{
    static string TempPath() => Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "content.json");

    static Row Template(uint npcflag = 0) => Row.Of(("entry", 42u), ("npcflag", npcflag), ("gossip_menu_id", 0u), ("subname", ""));

    static RowEdit Edit(Row? before, Row? after) => new(AcTables.CreatureTemplate.Name, "42", before, after);

    static RowSet Snapshot()
    {
        var rows = new RowSet();
        rows.Set(AcTables.CreatureTemplate, Template());
        return rows;
    }

    static readonly Row Starter = Row.Of(("id", 42u), ("quest", 7u));

    static RowEdit AddStarter(Row? before, Row? after) => new(AcTables.QuestStarter.Name, AcTables.QuestStarter.KeyOf(Starter), before, after);

    [Fact]
    public void RepeatedEditsCollapseToFirstBeforeAndLastAfter()
    {
        using var draft = new InteractionDraft(TempPath());
        var snapshot = Snapshot();
        draft.Execute("s", snapshot, "one", [Edit(Template(), Template(1))]);
        draft.Execute("s", snapshot, "two", [Edit(Template(1), Template(3))]);
        var (source, net) = Assert.Single(draft.NetEdits());
        Assert.Equal("s", source);
        Assert.Equal(Template(), net.Before);
        Assert.Equal(Template(3), net.After);
    }

    [Fact]
    public void EditThatReturnsToTheBaselineIsOmitted()
    {
        using var draft = new InteractionDraft(TempPath());
        var snapshot = Snapshot();
        draft.Execute("s", snapshot, "up", [Edit(Template(), Template(1))]);
        draft.Execute("s", snapshot, "down", [Edit(Template(1), Template())]);
        Assert.Empty(draft.NetEdits());
    }

    [Fact]
    public void InsertThenDeleteIsOmittedAndUndoneEditsDoNotCount()
    {
        using var draft = new InteractionDraft(TempPath());
        var snapshot = Snapshot();
        draft.Execute("s", snapshot, "add", [AddStarter(null, Starter)]);
        draft.Execute("s", snapshot, "remove", [AddStarter(Starter, null)]);
        Assert.Empty(draft.NetEdits());
        draft.Undo();
        Assert.Single(draft.NetEdits());
        draft.Undo();
        Assert.Empty(draft.NetEdits());
    }

    [Fact]
    public void NetEditsAreSeparatedBySourceAndKeepFirstTouchedOrder()
    {
        using var draft = new InteractionDraft(TempPath());
        var snapshot = Snapshot();
        draft.Execute("b", snapshot, "x", [Edit(Template(), Template(1))]);
        draft.Execute("a", snapshot, "y", [AddStarter(null, Starter)]);
        draft.Execute("b", snapshot, "z", [Edit(Template(1), Template(2))]);
        Assert.Equal(["b", "a"], draft.NetEdits().Select(e => e.Source));
    }

    [Fact]
    public void RevertRestoresTheBaselineAsAnUndoableCommand()
    {
        using var draft = new InteractionDraft(TempPath());
        var snapshot = Snapshot();
        draft.Execute("s", snapshot, "up", [Edit(Template(), Template(5))]);
        draft.Execute("s", snapshot, "starter", [AddStarter(null, Starter)]);
        var net = draft.NetEdits().Single(e => e.Edit.Table == AcTables.CreatureTemplate.Name);
        draft.Revert(net.Source, net.Edit);
        var view = draft.Effective("s", snapshot);
        Assert.Equal(Template(), view.Get(AcTables.CreatureTemplate, "42"));
        Assert.NotNull(view.Get(AcTables.QuestStarter, AcTables.QuestStarter.KeyOf(Starter)));
        Assert.Equal("Discard creature_template 42", draft.LastLabel);
        Assert.Single(draft.NetEdits());

        draft.Undo();
        Assert.Equal(Template(5), draft.Effective("s", snapshot).Get(AcTables.CreatureTemplate, "42"));
        Assert.Equal(2, draft.NetEdits().Count);
    }

    [Fact]
    public void RevertOfAnInsertRemovesTheRow()
    {
        using var draft = new InteractionDraft(TempPath());
        var snapshot = Snapshot();
        draft.Execute("s", snapshot, "add", [AddStarter(null, Starter)]);
        var net = Assert.Single(draft.NetEdits());
        draft.Revert(net.Source, net.Edit);
        Assert.Null(draft.Effective("s", snapshot).Get(AcTables.QuestStarter, AcTables.QuestStarter.KeyOf(Starter)));
        Assert.Empty(draft.NetEdits());
    }

    [Fact]
    public void RevertWorksOnAConflictingRowWhereExecuteWouldRefuse()
    {
        using var draft = new InteractionDraft(TempPath());
        var snapshot = Snapshot();
        draft.Execute("s", snapshot, "up", [Edit(Template(), Template(5))]);
        var changed = Snapshot();
        changed.Set(AcTables.CreatureTemplate, Template(9));
        Assert.Single(draft.Conflicts("s", changed));
        Assert.Throws<InvalidDataException>(() => draft.Execute("s", changed, "more", [Edit(Template(9), Template(10))]));
        var net = Assert.Single(draft.NetEdits());
        draft.Revert(net.Source, net.Edit);
        Assert.Empty(draft.NetEdits());
    }

    [Fact]
    public void RowEditsAppearAsPendingRowChangesAndExportedOnesLeave()
    {
        using var draft = new InteractionDraft(TempPath());
        var snapshot = Snapshot();
        draft.Execute("s", snapshot, "up", [Edit(Template(), Template(5))]);
        string exportedPath = Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "exported.json");
        var pending = new PendingChanges(exportedPath);
        var change = Assert.Single(pending.Collect([], [], draft.NetEdits()));
        Assert.Equal(PendingKind.Row, change.Kind);
        Assert.Equal("s", change.Source);
        Assert.NotNull(change.Row);
        Assert.Null(change.Move);
        Assert.Null(change.Graveyard);

        pending.MarkExported([change]);
        Assert.Empty(pending.Collect([], [], draft.NetEdits()));
        // Discarding after export leaves nothing pending either.
        draft.Revert("s", draft.NetEdits().Single().Edit);
        Assert.Empty(pending.Collect([], [], draft.NetEdits()));
    }

    [Fact]
    public void RowChangeIdTracksTheRowContent()
    {
        using var draft = new InteractionDraft(TempPath());
        var snapshot = Snapshot();
        var pending = new PendingChanges(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "exported.json"));
        draft.Execute("s", snapshot, "up", [Edit(Template(), Template(5))]);
        string first = pending.Collect([], [], draft.NetEdits()).Single().Id;
        draft.Execute("s", snapshot, "more", [Edit(Template(5), Template(6))]);
        Assert.NotEqual(first, pending.Collect([], [], draft.NetEdits()).Single().Id);
    }
}
