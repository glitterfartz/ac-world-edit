using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.ChangeLog;

/// <summary>
/// The stable logical key and the script seal: edits to the same object keep one history row,
/// unchanged refreshes produce identical SQL, and a tampered stored script is rejected.
/// </summary>
public class ChangeLogIntegrityTests
{
    static readonly DateTime FirstNow = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
    static readonly DateTime LaterNow = new(2026, 3, 1, 10, 5, 0, DateTimeKind.Utc);
    static readonly DateTime Created = new(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc);

    static PendingChange RowChange(uint npcflag, string source = "source")
    {
        var edit = new RowEdit(
            AcTables.CreatureTemplate.Name,
            "42",
            Row.Of(("entry", 42u), ("npcflag", 0u), ("gossip_menu_id", 0u), ("subname", "")),
            Row.Of(("entry", 42u), ("npcflag", npcflag), ("gossip_menu_id", 0u), ("subname", "")));
        var scripts = SqlExport.Build(source, [], [edit], PendingChanges.FixedStamp, AcSchema.Instance);
        return new(
            ChangeIntegrity.Fingerprint(source, scripts.Apply),
            PendingKind.Row,
            source,
            "creature template 42",
            null,
            edit);
    }

    static ChangeRecord Build(PendingChange change, DateTime now) =>
        ChangeLogMerge.BuildRecord(change, "", Created, now, _ => AcSchema.Instance, _ => WorldDialect.AzerothCore);

    [Fact]
    public void SameLogicalObjectKeepsOneChangeKeyWhenItsValueChanges()
    {
        var first = RowChange(1);
        var second = RowChange(2);
        Assert.Equal(first.ChangeKey, second.ChangeKey);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void DifferentObjectsGetDifferentChangeKeys()
    {
        var row = RowChange(1);
        var otherSource = RowChange(1, "other");
        var graveyard = new PendingChange(new string('a', 64), PendingKind.Graveyard, "source", "graveyard 7", new GraveyardEdit(
            "source", 7, new GraveyardRecord(7, 0, 1, 2, 3, "GY", []), new GraveyardRecord(7, 0, 1, 2, 9, "GY", [])), null);
        Assert.NotEqual(row.ChangeKey, otherSource.ChangeKey);
        Assert.NotEqual(row.ChangeKey, graveyard.ChangeKey);
    }

    [Fact]
    public void RefreshTimeNoLongerChangesTheStoredScripts()
    {
        var change = RowChange(1);
        var first = Build(change, FirstNow);
        var later = Build(change, LaterNow);
        Assert.Equal(first.ApplySql, later.ApplySql);
        Assert.Equal(first.RevertSql, later.RevertSql);
        Assert.Equal(first.Fingerprint, later.Fingerprint);
        Assert.True(ChangeIntegrity.FingerprintMatches(first));
    }

    [Fact]
    public void MergeUpdatesTheExistingRowInsteadOfAddingASecondKey()
    {
        var first = Build(RowChange(1), FirstNow);
        var existing = new Dictionary<string, ChangeRecord> { [first.ChangeKey] = first with { Notes = "near the tree" } };
        var merged = Assert.Single(ChangeLogMerge.MergeStaged(
            [RowChange(2)], existing, _ => AcSchema.Instance, _ => WorldDialect.AzerothCore, LaterNow));
        Assert.Equal(first.ChangeKey, merged.ChangeKey);
        Assert.Equal("near the tree", merged.Notes);
        Assert.Equal(Created, merged.CreatedAt);
        Assert.NotEqual(first.Fingerprint, merged.Fingerprint);
        Assert.True(ChangeIntegrity.FingerprintMatches(merged));
    }

    [Fact]
    public void TamperedApplyScriptFailsTheFingerprintCheck()
    {
        var record = Build(RowChange(1), FirstNow);
        Assert.True(ChangeIntegrity.FingerprintMatches(record));
        Assert.False(ChangeIntegrity.FingerprintMatches(record with { ApplySql = record.ApplySql + "\nDROP TABLE creature;" }));
    }

    [Fact]
    public void TamperedRevertScriptFailsTheSeal()
    {
        var record = Build(RowChange(1), FirstNow);
        Assert.True(ChangeIntegrity.ScriptsHashMatches(record));
        Assert.False(ChangeIntegrity.ScriptsHashMatches(record with { RevertSql = record.RevertSql + "\nDROP TABLE creature;" }));
    }

    [Fact]
    public void MissingSealIsNotTreatedAsTrusted()
    {
        var record = Build(RowChange(1), FirstNow) with { ScriptsHash = "" };
        Assert.False(ChangeIntegrity.ScriptsHashMatches(record));
    }

    [Fact]
    public void RecordsFromAnotherSourceAreNeverRunnable()
    {
        var mine = Build(RowChange(1, "mine"), FirstNow);
        var theirs = Build(RowChange(2, "theirs"), FirstNow);
        var (runnable, foreign) = ChangeLogExport.ForSource([mine, theirs], "mine");
        Assert.Single(runnable);
        Assert.Equal(mine.Fingerprint, runnable[0].Fingerprint);
        Assert.Equal(1, foreign);
    }

    [Fact]
    public void ExportGroupsWorldsAndSplitsFileSuffixes()
    {
        var one = Build(RowChange(1, "one"), FirstNow);
        var two = Build(RowChange(2, "two"), FirstNow);
        var groups = ChangeLogExport.GroupBySource([one, two, one with { Fingerprint = new string('a', 64) }]);
        Assert.Equal(2, groups.Count);
        Assert.Equal("one", groups[0].SourceKey);
        Assert.Equal(2, groups[0].Records.Count);
        Assert.Equal("one", ChangeLogExport.FileSuffix("one"));
        Assert.Equal(12, ChangeLogExport.FileSuffix(new string('f', 64)).Length);
    }
}
