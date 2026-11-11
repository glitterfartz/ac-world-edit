using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// The history writes the apply/revert/discard flows depend on. Keeping this behind a small seam
/// lets the failure paths be tested without a live database or the Godot viewer.
/// </summary>
public interface IChangeHistoryStore
{
    Task UpsertStagedAsync(DatabaseSettings acworld, IReadOnlyList<ChangeRecord> records, CancellationToken token);

    Task MarkAppliedAsync(DatabaseSettings acworld, string fingerprint, string appliedBy, CancellationToken token);

    Task MarkRevertedAsync(DatabaseSettings acworld, string fingerprint, CancellationToken token);

    Task MarkFailedAsync(DatabaseSettings acworld, string fingerprint, CancellationToken token);

    Task DeleteStagedAsync(DatabaseSettings acworld, string fingerprint, CancellationToken token);

    Task DeleteAllStagedAsync(DatabaseSettings acworld, string? sourceKey, CancellationToken token);
}

/// <summary>The production store: the static <see cref="AcworldStore"/> routines.</summary>
public sealed class AcworldHistoryStore : IChangeHistoryStore
{
    public static AcworldHistoryStore Instance { get; } = new();

    AcworldHistoryStore()
    {
    }

    public Task UpsertStagedAsync(DatabaseSettings acworld, IReadOnlyList<ChangeRecord> records, CancellationToken token) =>
        AcworldStore.UpsertStagedAsync(acworld, records, token);

    public Task MarkAppliedAsync(DatabaseSettings acworld, string fingerprint, string appliedBy, CancellationToken token) =>
        AcworldStore.MarkAppliedAsync(acworld, fingerprint, appliedBy, token);

    public Task MarkRevertedAsync(DatabaseSettings acworld, string fingerprint, CancellationToken token) =>
        AcworldStore.MarkRevertedAsync(acworld, fingerprint, token);

    public Task MarkFailedAsync(DatabaseSettings acworld, string fingerprint, CancellationToken token) =>
        AcworldStore.MarkFailedAsync(acworld, fingerprint, token);

    public Task DeleteStagedAsync(DatabaseSettings acworld, string fingerprint, CancellationToken token) =>
        AcworldStore.DeleteStagedAsync(acworld, fingerprint, token);

    public Task DeleteAllStagedAsync(DatabaseSettings acworld, string? sourceKey, CancellationToken token) =>
        AcworldStore.DeleteAllStagedAsync(acworld, sourceKey, token);
}

/// <summary>What an apply or revert run completed, and the first failure worth reporting.</summary>
public sealed record HistoryRunOutcome(IReadOnlyList<string> CompletedChangeKeys, string? Error);

/// <summary>What a discard or reset removed, and the first failure worth reporting.</summary>
public sealed record HistoryDeleteOutcome(IReadOnlyList<string> DeletedChangeKeys, string? Error);

/// <summary>
/// Orchestrates the history side of an apply, revert, discard, stage or reset. Every write is part of
/// the operation: a failed mark or delete keeps the draft/row and returns the error, so the viewer
/// never reports success while the history disagrees.
/// </summary>
public static class ChangeHistoryOperations
{
    public static async Task<string?> StageAsync(
        IChangeHistoryStore store,
        DatabaseSettings acworld,
        IReadOnlyList<ChangeRecord> records,
        Func<Exception, string> describe,
        CancellationToken token)
    {
        if (records.Count == 0)
        {
            return null;
        }

        try
        {
            await store.UpsertStagedAsync(acworld, records, token).ConfigureAwait(false);
            return null;
        }
        catch (Exception error)
        {
            return "Some changes could not be staged. " + describe(error);
        }
    }

    public static async Task<HistoryRunOutcome> ApplyAsync(
        IReadOnlyList<ChangeRecord> records,
        IReadOnlyDictionary<string, ChangeRecord> localScripts,
        Func<string, Task<string?>> run,
        IChangeHistoryStore store,
        DatabaseSettings acworld,
        string appliedBy,
        Func<Exception, string> describe,
        CancellationToken token)
    {
        var completed = new List<string>();
        string? firstError = null;
        foreach (var record in records)
        {
            if (!localScripts.TryGetValue(record.ChangeKey, out var local))
            {
                firstError ??= $"The draft for \"{record.Label}\" is no longer open.";
                continue;
            }

            if (!ChangeIntegrity.FingerprintMatches(record))
            {
                firstError ??= $"The stored apply script for \"{record.Label}\" does not match its fingerprint, so it was not run.";
                continue;
            }

            if (local.Fingerprint != record.Fingerprint)
            {
                firstError ??= $"The draft for \"{record.Label}\" changed since the history was refreshed. Refresh and review it again.";
                continue;
            }

            string? error = await run(local.ApplySql).ConfigureAwait(false);
            if (error != null)
            {
                firstError ??= error;
                try
                {
                    await store.MarkFailedAsync(acworld, record.Fingerprint, token).ConfigureAwait(false);
                }
                catch
                {
                    // The run failed already; a failed status write must not hide that.
                }

                continue;
            }

            try
            {
                await store.MarkAppliedAsync(acworld, record.Fingerprint, appliedBy, token).ConfigureAwait(false);
            }
            catch (Exception markError)
            {
                firstError ??= $"The world change ran, but the history could not mark it applied ({describe(markError)}). The draft was kept.";
                continue;
            }

            completed.Add(record.ChangeKey);
        }

        return new HistoryRunOutcome(completed, firstError);
    }

    public static async Task<HistoryRunOutcome> RevertAsync(
        IReadOnlyList<ChangeRecord> records,
        string clientKey,
        Func<string, Task<string?>> run,
        IChangeHistoryStore store,
        DatabaseSettings acworld,
        Func<Exception, string> describe,
        CancellationToken token)
    {
        var completed = new List<string>();
        string? firstError = null;
        foreach (var record in records)
        {
            if (string.IsNullOrEmpty(record.AppliedBy) || record.AppliedBy != clientKey)
            {
                firstError ??= $"\"{record.Label}\" was applied by another client, so this client will not revert it.";
                continue;
            }

            if (!ChangeIntegrity.ScriptsHashMatches(record))
            {
                firstError ??= $"The stored revert script for \"{record.Label}\" failed its integrity check, so it was not run.";
                continue;
            }

            string? error = await run(record.RevertSql).ConfigureAwait(false);
            if (error != null)
            {
                firstError ??= error;
                continue;
            }

            try
            {
                await store.MarkRevertedAsync(acworld, record.Fingerprint, token).ConfigureAwait(false);
            }
            catch (Exception markError)
            {
                firstError ??= $"The world change was reverted, but the history could not mark it ({describe(markError)}).";
                continue;
            }

            completed.Add(record.ChangeKey);
        }

        return new HistoryRunOutcome(completed, firstError);
    }

    public static async Task<HistoryDeleteOutcome> DiscardAsync(
        IReadOnlyList<PendingChange> changes,
        IChangeHistoryStore store,
        DatabaseSettings acworld,
        Func<Exception, string> describe,
        CancellationToken token)
    {
        var deleted = new List<string>();
        string? firstError = null;
        foreach (var change in changes)
        {
            try
            {
                await store.DeleteStagedAsync(acworld, change.Id, token).ConfigureAwait(false);
                deleted.Add(change.ChangeKey);
            }
            catch (Exception error)
            {
                firstError ??= "Could not discard a staged change (" + describe(error) + "). The draft was kept.";
            }
        }

        return new HistoryDeleteOutcome(deleted, firstError);
    }

    public static async Task<string?> ResetAsync(
        IEnumerable<DatabaseSettings> targets,
        IChangeHistoryStore store,
        Func<DatabaseSettings, Exception, string> describe,
        CancellationToken token)
    {
        foreach (var acworld in targets)
        {
            try
            {
                await store.DeleteAllStagedAsync(acworld, null, token).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                return $"Reset stopped: could not remove staged change history from {acworld.Database} ({describe(acworld, error)}). Nothing was deleted.";
            }
        }

        return null;
    }
}
