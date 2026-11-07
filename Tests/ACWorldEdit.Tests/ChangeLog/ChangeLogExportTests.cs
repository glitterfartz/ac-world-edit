using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.ChangeLog;

/// <summary>
/// The change-history export: one combined file from checked rows, apply oldest-first and revert
/// newest-first, with per-change headers so shared files stay reviewable.
/// </summary>
public class ChangeLogExportTests
{
    static ChangeRecord Record(string fingerprint, string label, DateTime created) => new(
        fingerprint.PadRight(64, '0'),
        "source",
        WorldDialect.AzerothCore,
        label,
        $"-- apply {label}\nUPDATE `creature` SET `x`=1 WHERE `guid`=1;",
        $"-- revert {label}\nUPDATE `creature` SET `x`=0 WHERE `guid`=1;",
        ChangeStatus.Applied,
        created,
        created,
        null,
        "");

    [Fact]
    public void EmptySelectionExplainsItself()
    {
        string combined = ChangeLogExport.BuildCombined([], revert: false, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Contains("Nothing selected", combined);
    }

    [Fact]
    public void ApplyBundleKeepsCallerOrderAndRevertNotesDirection()
    {
        var first = Record("a", "first", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var second = Record("b", "second", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        string apply = ChangeLogExport.BuildCombined([first, second], revert: false);
        Assert.True(apply.IndexOf("-- apply first", StringComparison.Ordinal) < apply.IndexOf("-- apply second", StringComparison.Ordinal));
        Assert.Contains("oldest first", apply);

        string revert = ChangeLogExport.BuildCombined([second, first], revert: true);
        Assert.True(revert.IndexOf("-- revert second", StringComparison.Ordinal) < revert.IndexOf("-- revert first", StringComparison.Ordinal));
        Assert.Contains("newest first", revert);
        Assert.Contains("-- revert second", revert);
    }

    [Fact]
    public void CollectAllIgnoresTheLegacyExportedFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "exported.json");
        var pending = new PendingChanges(path);
        var before = Row.Of(("entry", 42u), ("npcflag", 0u), ("gossip_menu_id", 0u), ("subname", ""));
        var after = Row.Of(("entry", 42u), ("npcflag", 1u), ("gossip_menu_id", 0u), ("subname", ""));
        var rows = new List<(string Source, RowEdit Edit)> { ("s", new RowEdit(AcTables.CreatureTemplate.Name, "42", before, after)) };
        var all = pending.CollectAll([], rows);
        pending.MarkExported(all);
        // The legacy exported set hides the change from Collect, but the history stages everything.
        Assert.Empty(pending.Collect([], rows));
        Assert.Single(pending.CollectAll([], rows));
    }

    [Fact]
    public void SplitScriptSeparatesChecksBodyAndPostChecks()
    {
        string script = """
            -- header
            SET NAMES utf8mb4;

            -- Preflight (read-only)
            SELECT 'a' AS `change`, IF(COUNT(*) = 1, 'ok', 'STALE') AS `state` FROM `creature` WHERE `guid` = 1;
            SELECT 'b' AS `change`, IF(COUNT(*) = 1, 'ok', 'STALE') AS `state` FROM `creature` WHERE `guid` = 2;

            START TRANSACTION;

            -- a
            UPDATE `creature` SET `x` = 1 WHERE `guid` = 1;

            COMMIT;

            -- Post-check (read-only): every row must read ok
            SELECT 'a' AS `change`, IF(COUNT(*) = 1, 'ok', 'STALE') AS `state` FROM `creature` WHERE `guid` = 1;
            """;
        var (checks, body, post) = WorldApplier.SplitScript(script);
        Assert.Equal(2, checks.Count);
        Assert.Single(body);
        Assert.Contains("UPDATE", body[0]);
        Assert.Single(post);
    }

    static (string Source, RowEdit Edit) StagedRow(string source = "s")
    {
        var before = Row.Of(("entry", 42u), ("npcflag", 0u), ("gossip_menu_id", 0u), ("subname", ""));
        var after = Row.Of(("entry", 42u), ("npcflag", 1u), ("gossip_menu_id", 0u), ("subname", ""));
        return (source, new RowEdit(AcTables.CreatureTemplate.Name, "42", before, after));
    }

    static PendingChange StagedChange(string fingerprint, string label, string source = "s")
    {
        var (stageSource, edit) = StagedRow(source);
        return new PendingChange(fingerprint, PendingKind.Row, stageSource, label, null, edit);
    }

    static ChangeRecord Existing(string fingerprint, string notes, DateTime created) => new(
        fingerprint, "s", WorldDialect.AzerothCore, "old label", "-- apply", "-- revert",
        ChangeStatus.Applied, created, created, null, notes);

    /// <summary>
    /// Regression test for the empty history window: every staged draft must produce exactly one
    /// staged record, so a staged change can never silently vanish from the window.
    /// </summary>
    [Fact]
    public void EveryStagedDraftProducesExactlyOneStagedRecord()
    {
        var staged = new PendingChanges(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "exported.json"))
            .CollectAll([], [StagedRow("s"), StagedRow("other")]);
        var merged = ChangeLogMerge.MergeStaged(
            staged, new Dictionary<string, ChangeRecord>(), _ => AcSchema.Instance, _ => WorldDialect.AzerothCore,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(2, merged.Count);
        Assert.All(merged, r => Assert.Equal(ChangeStatus.Staged, r.Status));
        Assert.Equal(staged.Select(c => c.Id).Order(StringComparer.Ordinal), merged.Select(r => r.Fingerprint).Order(StringComparer.Ordinal));
        Assert.All(merged, r => Assert.False(string.IsNullOrEmpty(r.ApplySql)));
        Assert.All(merged, r => Assert.False(string.IsNullOrEmpty(r.RevertSql)));
    }

    [Fact]
    public void MergePreservesNotesAndFirstSeenTimestamps()
    {
        var now = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var created = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        string fingerprint = new string('c', 64);
        var existing = new Dictionary<string, ChangeRecord>
        {
            [fingerprint] = Existing(fingerprint, "near the tree", created)
        };

        var merged = Assert.Single(ChangeLogMerge.MergeStaged(
            [StagedChange(fingerprint, "new label")], existing, _ => AcSchema.Instance, _ => WorldDialect.AzerothCore, now));

        Assert.Equal("near the tree", merged.Notes);
        Assert.Equal(created, merged.CreatedAt);
        Assert.Equal("new label", merged.Label);
    }

    [Fact]
    public void EvaluateCheckResultReadsTheStateColumnNotTheLabel()
    {
        // The exact reported failure: label "creature 11800166: insert" contains neither ok nor
        // STALE, but the row reads ok. The old scalar check read the label and aborted every apply.
        var (label, ok) = WorldApplier.EvaluateCheckResult(
            ["change", "state"], ["creature 11800166: insert", "ok"]);
        Assert.Equal("creature 11800166: insert", label);
        Assert.True(ok);
    }

    [Fact]
    public void EvaluateCheckResultFailsStaleEvenWhenTheLabelContainsOk()
    {
        var (label, ok) = WorldApplier.EvaluateCheckResult(
            ["change", "state"], ["smoking gun", "STALE"]);
        Assert.Equal("smoking gun", label);
        Assert.False(ok);
    }

    [Fact]
    public void EvaluateCheckResultRequiresAnExplicitOk()
    {
        Assert.False(WorldApplier.EvaluateCheckResult(["change", "state"], ["x", ""]).Ok);
        Assert.False(WorldApplier.EvaluateCheckResult(["change"], ["x"]).Ok);
        Assert.False(WorldApplier.EvaluateCheckResult([], []).Ok);
        Assert.False(WorldApplier.EvaluateCheckResult(["change", "state"], ["x", null]).Ok);
        Assert.True(WorldApplier.EvaluateCheckResult(["STATE", "CHANGE"], ["OK", "x"]).Ok);
    }

    [Fact]
    public void MergeStampsNewRowsWithNowAndBlankNotes()
    {
        var now = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var merged = Assert.Single(ChangeLogMerge.MergeStaged(
            [StagedChange(new string('d', 64), "label")], new Dictionary<string, ChangeRecord>(),
            _ => AcSchema.Instance, _ => WorldDialect.AzerothCore, now));

        Assert.Equal("", merged.Notes);
        Assert.Equal(now, merged.CreatedAt);
    }
}
