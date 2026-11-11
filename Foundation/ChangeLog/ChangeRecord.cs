using System.Security.Cryptography;
using System.Text;

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
/// <para>
/// <see cref="ChangeKey"/> is the stable logical identity of the edit (the object that changed),
/// unlike <see cref="Fingerprint"/>, which hashes the generated apply script and therefore changes
/// whenever the draft's net value changes. <see cref="ScriptsHash"/> seals the stored script pair so
/// a tampered row is rejected before its revert SQL runs.
/// </para>
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
    string Notes)
{
    /// <summary>Stable logical key; rows written before the column existed fall back to the fingerprint.</summary>
    public string ChangeKey { get; init; } = Fingerprint;

    /// <summary>SHA-256 seal over source + apply + revert SQL, so a stored row can be verified before revert.</summary>
    public string ScriptsHash { get; init; } = ChangeIntegrity.ScriptsHash(SourceKey, ApplySql, RevertSql);

    /// <summary>
    /// The client profile that marked this row applied. Empty for rows written before provenance
    /// existed; those are treated as belonging to another client, not as trusted.
    /// </summary>
    public string AppliedBy { get; init; } = "";
}

/// <summary>
/// Hashing rules shared by the local drafts, the change store and the viewer's trust checks.
/// All hashes are uppercase SHA-256 hex, matching <see cref="Convert.ToHexString(byte[])"/>.
/// </summary>
public static class ChangeIntegrity
{
    /// <summary>The draft fingerprint: the source plus the generated apply script.</summary>
    public static string Fingerprint(string source, string applySql) =>
        Hash(source + "\n" + applySql);

    /// <summary>The stored-script seal: source plus both scripts, verified before a revert runs.</summary>
    public static string ScriptsHash(string source, string applySql, string revertSql) =>
        Hash(source + "\n" + applySql + "\n" + revertSql);

    /// <summary>Whether the record still has the exact apply script its fingerprint names.</summary>
    public static bool FingerprintMatches(ChangeRecord record) =>
        string.Equals(record.Fingerprint, Fingerprint(record.SourceKey, record.ApplySql), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the stored script pair matches its recorded seal.</summary>
    public static bool ScriptsHashMatches(ChangeRecord record) =>
        !string.IsNullOrEmpty(record.ScriptsHash) &&
        string.Equals(record.ScriptsHash, ScriptsHash(record.SourceKey, record.ApplySql, record.RevertSql), StringComparison.OrdinalIgnoreCase);

    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

/// <summary>
/// Builds single-file exports from tracked changes. Selected records are concatenated in caller
/// order (the window passes chronological order for apply, reverse for revert). Each stored script
/// already carries its own guards and transaction, so concatenation stays runnable and preserves
/// the per-change stale protection.
/// </summary>
public static class ChangeLogExport
{
    /// <summary>
    /// Group records by world source. A runnable export must never silently mix two databases, so
    /// the viewer writes one file per group.
    /// </summary>
    public static IReadOnlyList<(string SourceKey, List<ChangeRecord> Records)> GroupBySource(IEnumerable<ChangeRecord> records) =>
        records.GroupBy(r => r.SourceKey, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, g.ToList()))
            .ToList();

    /// <summary>A short, filename-safe suffix for a source's export file.</summary>
    public static string FileSuffix(string sourceKey) =>
        sourceKey.Length <= 12 ? sourceKey : sourceKey[..12];

    /// <summary>
    /// The records that may run against <paramref name="sourceKey"/>. Records from another world are
    /// counted and returned separately so the caller can report exactly what it skipped.
    /// </summary>
    public static (IReadOnlyList<ChangeRecord> Runnable, int Foreign) ForSource(IEnumerable<ChangeRecord> records, string sourceKey)
    {
        var runnable = new List<ChangeRecord>();
        int foreign = 0;
        foreach (var record in records)
        {
            if (record.SourceKey == sourceKey)
            {
                runnable.Add(record);
            }
            else
            {
                foreign++;
            }
        }

        return (runnable, foreign);
    }

    public static string BuildCombined(IReadOnlyList<ChangeRecord> records, bool revert, DateTime? generated = null)
    {
        string stamp = (generated ?? DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
        var text = new System.Text.StringBuilder();
        text.Append("-- ACWorldEdit change-log export · generated ").Append(stamp).Append('\n');
        if (records.Count > 0 && records.All(r => r.SourceKey == records[0].SourceKey))
        {
            text.Append("-- Source database: ").Append(records[0].SourceKey).Append('\n');
        }

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
