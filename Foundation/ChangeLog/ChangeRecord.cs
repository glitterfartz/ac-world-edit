namespace ACWorldEdit.Foundation;

/// <summary>
/// Lifecycle of one tracked world change in the acworld database.
/// Staged = still in local drafts, not yet run. Applied = apply SQL ran against the world.
/// Reverted = a previous apply was undone by running the revert SQL. Failed = last run was refused.
/// </summary>
public enum ChangeStatus
{
    Staged,
    Applied,
    Reverted,
    Failed
}

/// <summary>
/// One row of the acworld change log: the guarded apply/revert scripts for a single net draft
/// change plus when it happened and the user's optional note. Stored in MySQL (acworld.change_log)
/// and mirrored here Godot-free so the viewer and the tests share the shape.
/// </summary>
public sealed record ChangeRecord(
    string Fingerprint,
    string SourceKey,
    WorldDialect Dialect,
    string Label,
    string ApplySql,
    string RevertSql,
    ChangeStatus Status,
    DateTime CreatedAt,
    DateTime? AppliedAt,
    DateTime? RevertedAt,
    string Notes);

/// <summary>
/// Builds single-file exports from tracked changes. Selected records are concatenated in caller
/// order (the window passes chronological order for apply, reverse for revert). Each stored script
/// already carries its own guards and transaction, so concatenation stays runnable and preserves
/// the per-change stale protection.
/// </summary>
public static class ChangeLogExport
{
    public static string BuildCombined(IReadOnlyList<ChangeRecord> records, bool revert, DateTime? generated = null)
    {
        string stamp = (generated ?? DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
        var text = new System.Text.StringBuilder();
        text.Append("-- ACWorldEdit change-log export · generated ").Append(stamp).Append('\n');
        text.Append(revert ? "-- Revert script for " : "-- Apply script for ").Append(records.Count).Append(" change(s).\n");
        text.Append("-- Order: ").Append(revert ? "newest first (undoes the most recent change first).\n" : "oldest first (replays work in order).\n");
        text.Append("-- Review before running. Check that every preflight row reads ok.\n\n");
        if (records.Count == 0)
        {
            text.Append("-- Nothing selected.\n");
            return text.ToString();
        }

        foreach (var record in records)
        {
            text.Append("-- ===== ").Append(SingleLine($"{record.Label} [{record.Fingerprint[..Math.Min(12, record.Fingerprint.Length)]}]")).Append(" =====\n");
            text.Append(revert ? record.RevertSql : record.ApplySql).Append('\n');
            if (!(revert ? record.RevertSql : record.ApplySql).EndsWith('\n'))
            {
                text.Append('\n');
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    static string SingleLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');
}
