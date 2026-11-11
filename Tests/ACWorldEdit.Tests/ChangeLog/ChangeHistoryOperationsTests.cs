using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.ChangeLog;

/// <summary>
/// Failure paths of the history seam: a world write that cannot be marked, a failed discard, a
/// partial staging failure and reset cleanup must keep the draft/row and report the error. Reverts
/// are also limited to rows this client applied.
/// </summary>
public class ChangeHistoryOperationsTests
{
    static readonly DatabaseSettings Acworld = new("db.example", 3306, "editor", "pw", "acworld");
    static readonly DateTime Created = new(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc);

    static string Describe(Exception error) => error.GetType().Name + ": " + error.Message;

    static ChangeRecord Record(string key, ChangeStatus status = ChangeStatus.Staged, string appliedBy = "") =>
        new(
            ChangeIntegrity.Fingerprint("source", "-- apply " + key),
            "source",
            WorldDialect.AzerothCore,
            "change " + key,
            "-- apply " + key,
            "-- revert " + key,
            status,
            Created,
            status == ChangeStatus.Applied ? Created : null,
            null,
            "")
        {
            ChangeKey = key,
            AppliedBy = appliedBy
        };

    static PendingChange Draft(string key) =>
        new(ChangeIntegrity.Fingerprint("source", "-- apply " + key), PendingKind.Row, "source", "change " + key, null,
            new RowEdit(AcTables.CreatureTemplate.Name, key, null, null));

    sealed class FakeStore : IChangeHistoryStore
    {
        public bool FailStage, FailApplyMark, FailRevertMark, FailDelete, FailDeleteAll;
        public int SecondDeleteAll;
        public readonly List<string> Applied = [];
        public readonly List<string> Reverted = [];
        public readonly List<string> Failed = [];
        public readonly List<string> Deleted = [];
        public readonly List<string> DeletedAll = [];

        public Task UpsertStagedAsync(DatabaseSettings acworld, IReadOnlyList<ChangeRecord> records, CancellationToken token)
        {
            if (FailStage)
            {
                throw new InvalidOperationException("stage failed");
            }

            return Task.CompletedTask;
        }

        public Task MarkAppliedAsync(DatabaseSettings acworld, string fingerprint, string appliedBy, CancellationToken token)
        {
            if (FailApplyMark)
            {
                throw new InvalidOperationException("mark applied failed");
            }

            Applied.Add(fingerprint);
            return Task.CompletedTask;
        }

        public Task MarkRevertedAsync(DatabaseSettings acworld, string fingerprint, CancellationToken token)
        {
            if (FailRevertMark)
            {
                throw new InvalidOperationException("mark reverted failed");
            }

            Reverted.Add(fingerprint);
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(DatabaseSettings acworld, string fingerprint, CancellationToken token)
        {
            Failed.Add(fingerprint);
            return Task.CompletedTask;
        }

        public Task DeleteStagedAsync(DatabaseSettings acworld, string fingerprint, CancellationToken token)
        {
            if (FailDelete)
            {
                throw new InvalidOperationException("delete failed");
            }

            Deleted.Add(fingerprint);
            return Task.CompletedTask;
        }

        public Task DeleteAllStagedAsync(DatabaseSettings acworld, string? sourceKey, CancellationToken token)
        {
            DeletedAll.Add(acworld.Database);
            if (FailDeleteAll && DeletedAll.Count == SecondDeleteAll)
            {
                throw new InvalidOperationException("reset delete failed");
            }

            return Task.CompletedTask;
        }
    }

    static Dictionary<string, ChangeRecord> Local(ChangeRecord record) => new() { [record.ChangeKey] = record };

    [Fact]
    public async Task ApplyMarksTheRowOnlyAfterTheWorldWriteSucceeds()
    {
        var record = Record("a");
        var store = new FakeStore();
        var outcome = await ChangeHistoryOperations.ApplyAsync(
            [record], Local(record), _ => Task.FromResult<string?>(null), store, Acworld, "client", Describe, CancellationToken.None);

        Assert.Null(outcome.Error);
        Assert.Equal(["a"], outcome.CompletedChangeKeys);
        Assert.Single(store.Applied);
        Assert.Empty(store.Failed);
    }

    [Fact]
    public async Task ApplyMarkingFailureKeepsTheDraftAndReportsIt()
    {
        var record = Record("a");
        var store = new FakeStore { FailApplyMark = true };
        var outcome = await ChangeHistoryOperations.ApplyAsync(
            [record], Local(record), _ => Task.FromResult<string?>(null), store, Acworld, "client", Describe, CancellationToken.None);

        Assert.Empty(outcome.CompletedChangeKeys);
        Assert.Contains("could not mark it applied", outcome.Error);
        Assert.Contains("draft was kept", outcome.Error);
        Assert.Empty(store.Applied);
    }

    [Fact]
    public async Task FailedWorldApplyMarksFailedAndDoesNotComplete()
    {
        var record = Record("a");
        var store = new FakeStore();
        var outcome = await ChangeHistoryOperations.ApplyAsync(
            [record], Local(record), _ => Task.FromResult<string?>("stale preflight"), store, Acworld, "client", Describe, CancellationToken.None);

        Assert.Empty(outcome.CompletedChangeKeys);
        Assert.Equal("stale preflight", outcome.Error);
        Assert.Single(store.Failed);
        Assert.Empty(store.Applied);
    }

    [Fact]
    public async Task RevertMarkingFailureIsReportedAndNotCounted()
    {
        var record = Record("a", ChangeStatus.Applied, "client");
        var store = new FakeStore { FailRevertMark = true };
        var outcome = await ChangeHistoryOperations.RevertAsync(
            [record], "client", _ => Task.FromResult<string?>(null), store, Acworld, Describe, CancellationToken.None);

        Assert.Empty(outcome.CompletedChangeKeys);
        Assert.Contains("could not mark it", outcome.Error);
        Assert.Empty(store.Reverted);
    }

    [Fact]
    public async Task RevertRefusesRowsAppliedByAnotherClient()
    {
        var foreign = Record("a", ChangeStatus.Applied, "other-client");
        var legacy = Record("b", ChangeStatus.Applied);
        var store = new FakeStore();
        int runs = 0;
        var outcome = await ChangeHistoryOperations.RevertAsync(
            [foreign, legacy], "client", _ =>
            {
                runs++;
                return Task.FromResult<string?>(null);
            }, store, Acworld, Describe, CancellationToken.None);

        Assert.Empty(outcome.CompletedChangeKeys);
        Assert.Equal(0, runs);
        Assert.Contains("another client", outcome.Error);
    }

    [Fact]
    public async Task DiscardDeleteFailureKeepsTheDraftAndReportsIt()
    {
        var store = new FakeStore { FailDelete = true };
        var outcome = await ChangeHistoryOperations.DiscardAsync(
            [Draft("a")], store, Acworld, Describe, CancellationToken.None);

        Assert.Empty(outcome.DeletedChangeKeys);
        Assert.Contains("draft was kept", outcome.Error);
    }

    [Fact]
    public async Task PartialStagingFailureIsReported()
    {
        var store = new FakeStore { FailStage = true };
        string? error = await ChangeHistoryOperations.StageAsync(
            store, Acworld, [Record("a"), Record("b")], Describe, CancellationToken.None);

        Assert.NotNull(error);
        Assert.StartsWith("Some changes could not be staged.", error);
    }

    [Fact]
    public async Task ResetDeletesStagedRowsForEveryProfile()
    {
        var store = new FakeStore();
        string? error = await ChangeHistoryOperations.ResetAsync(
            [Acworld, Acworld with { Database = "acworld_retail" }], store, (_, e) => Describe(e), CancellationToken.None);

        Assert.Null(error);
        Assert.Equal(["acworld", "acworld_retail"], store.DeletedAll);
    }

    [Fact]
    public async Task ResetStopsAndReportsWhenAProfileCannotBeCleaned()
    {
        var store = new FakeStore { FailDeleteAll = true, SecondDeleteAll = 2 };
        string? error = await ChangeHistoryOperations.ResetAsync(
            [Acworld, Acworld with { Database = "acworld_retail" }], store, (_, e) => Describe(e), CancellationToken.None);

        Assert.NotNull(error);
        Assert.StartsWith("Reset stopped:", error);
        Assert.Contains("acworld_retail", error);
        Assert.Equal(["acworld", "acworld_retail"], store.DeletedAll);
    }
}
