using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using MySqlConnector;
using Xunit;
using static Test;

namespace ACWorldEdit.Tests.Db;

/// <summary>
/// Live interaction tests for the editor's own history database: connect, create the schema,
/// and round-trip staged/applied/reverted rows. Each test uses a uniquely named database and
/// drops it afterwards, so nothing is shared and nothing is left behind. Gated on
/// ACWORLD_DB_INFO like every other Db suite; the whole point is proving a real connection
/// works before history-backed work is marked done.
/// </summary>
[Trait("Category", "Db")]
public class AcworldStoreDbTests
{
    static string UniqueDb() => "acwe_hist_" + Guid.NewGuid().ToString("N")[..12];

    static DatabaseSettings HistorySettings() => Config with { Database = UniqueDb() };

    static ChangeRecord Staged(string fingerprint, string source = "test-source") => new(
        fingerprint.PadRight(64, '0'),
        source,
        WorldDialect.AzerothCore,
        "test label " + fingerprint,
        "-- apply " + fingerprint,
        "-- revert " + fingerprint,
        ChangeStatus.Staged,
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        null,
        null,
        "");

    static CancellationToken TestToken() => new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token;

    static async Task DropDatabaseAsync(DatabaseSettings acworld)
    {
        try
        {
            var server = Config with { Database = "" };
            var builder = new MySqlConnectionStringBuilder(server.BuildConnectionString()) { Database = "" };
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var drop = new MySqlCommand($"DROP DATABASE IF EXISTS `{acworld.Database.Replace("`", "``")}`", connection);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    /// <summary>
    /// The exact situation from the 1044 report, verified live: against a server where this user
    /// may create databases the schema is built; where it may not, the failure is the actionable
    /// "ask the admin" message instead of a raw driver error. Passes on both kinds of server.
    /// </summary>
    [DbFact]
    public async Task EnsureSchemaSucceedsOrExplainsRefusedCreate()
    {
        var acworld = HistorySettings();
        try
        {
            await AcworldStore.EnsureSchemaAsync(acworld, TestToken());
            await using var connection = new MySqlConnection(acworld.BuildConnectionString());
            await connection.OpenAsync(TestToken());
            await using var tables = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'change_log'", connection);
            Assert.Equal(1L, Convert.ToInt64(await tables.ExecuteScalarAsync(TestToken())));
        }
        catch (DatabaseFeatureException error)
        {
            Assert.Contains("1044", error.Message);
            Assert.Contains(acworld.Database, error.Message);
            Assert.Contains("GRANT", error.Message);
        }
        finally
        {
            await DropDatabaseAsync(acworld);
        }
    }

    [DbWriteFact]
    public async Task EnsureSchemaCreatesDatabaseAndTable()
    {
        var acworld = HistorySettings();
        try
        {
            await AcworldStore.EnsureSchemaAsync(acworld, TestToken());

            await using var connection = new MySqlConnection(acworld.BuildConnectionString());
            await connection.OpenAsync(TestToken());
            await using var tables = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'change_log'", connection);
            Assert.Equal(1L, Convert.ToInt64(await tables.ExecuteScalarAsync(TestToken())));
        }
        finally
        {
            await DropDatabaseAsync(acworld);
        }
    }

    [DbWriteFact]
    public async Task StagedRoundTripPreservesEveryField()
    {
        var acworld = HistorySettings();
        try
        {
            await AcworldStore.EnsureSchemaAsync(acworld, TestToken());
            var created = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
            var record = Staged("aa") with { Label = "move hogger", Notes = "near the tree", CreatedAt = created };
            await AcworldStore.UpsertStagedAsync(acworld, record, TestToken());

            var listed = await AcworldStore.ListAsync(acworld, "test-source", includeAllSources: false, TestToken());
            var back = Assert.Single(listed);
            Assert.Equal(record.Fingerprint, back.Fingerprint);
            Assert.Equal("test-source", back.SourceKey);
            Assert.Equal(WorldDialect.AzerothCore, back.Dialect);
            Assert.Equal("move hogger", back.Label);
            Assert.Equal("-- apply aa", back.ApplySql);
            Assert.Equal("-- revert aa", back.RevertSql);
            Assert.Equal(ChangeStatus.Staged, back.Status);
            Assert.Equal("near the tree", back.Notes);
            Assert.Equal(created, back.CreatedAt);
            Assert.Null(back.AppliedAt);
            Assert.Null(back.RevertedAt);
        }
        finally
        {
            await DropDatabaseAsync(acworld);
        }
    }

    [DbWriteFact]
    public async Task FullLifecycleMarksAndDeletes()
    {
        var acworld = HistorySettings();
        try
        {
            await AcworldStore.EnsureSchemaAsync(acworld, TestToken());
            await AcworldStore.UpsertStagedAsync(acworld, Staged("bb"), TestToken());

            await AcworldStore.UpdateNotesAsync(acworld, Staged("bb").Fingerprint, "edited note", TestToken());
            await AcworldStore.MarkAppliedAsync(acworld, Staged("bb").Fingerprint, TestToken());
            var applied = Assert.Single(await AcworldStore.ListAsync(acworld, "test-source", false, TestToken()));
            Assert.Equal(ChangeStatus.Applied, applied.Status);
            Assert.Equal("edited note", applied.Notes);
            Assert.NotNull(applied.AppliedAt);

            await AcworldStore.MarkRevertedAsync(acworld, Staged("bb").Fingerprint, TestToken());
            var reverted = Assert.Single(await AcworldStore.ListAsync(acworld, "test-source", false, TestToken()));
            Assert.Equal(ChangeStatus.Reverted, reverted.Status);
            Assert.NotNull(reverted.RevertedAt);

            await AcworldStore.MarkFailedAsync(acworld, Staged("bb").Fingerprint, TestToken());
            var still = Assert.Single(await AcworldStore.ListAsync(acworld, "test-source", false, TestToken()));
            Assert.Equal(ChangeStatus.Reverted, still.Status);

            await AcworldStore.DeleteAsync(acworld, Staged("bb").Fingerprint, TestToken());
            Assert.Empty(await AcworldStore.ListAsync(acworld, "test-source", false, TestToken()));
        }
        finally
        {
            await DropDatabaseAsync(acworld);
        }
    }

    [DbWriteFact]
    public async Task ListFiltersBySourceKeyUnlessAskedForAll()
    {
        var acworld = HistorySettings();
        try
        {
            await AcworldStore.EnsureSchemaAsync(acworld, TestToken());
            await AcworldStore.UpsertStagedAsync(acworld, Staged("c1", "source-one"), TestToken());
            await AcworldStore.UpsertStagedAsync(acworld, Staged("c2", "source-two"), TestToken());

            Assert.Single(await AcworldStore.ListAsync(acworld, "source-one", false, TestToken()));
            Assert.Equal(2, (await AcworldStore.ListAsync(acworld, "source-one", true, TestToken())).Count);
            Assert.Equal(2, (await AcworldStore.ListAsync(acworld, null, true, TestToken())).Count);
        }
        finally
        {
            await DropDatabaseAsync(acworld);
        }
    }

    [DbWriteFact]
    public async Task UpsertKeepsAppliedStatusButRefreshesScripts()
    {
        var acworld = HistorySettings();
        try
        {
            await AcworldStore.EnsureSchemaAsync(acworld, TestToken());
            var fingerprint = Staged("dd").Fingerprint;
            await AcworldStore.UpsertStagedAsync(acworld, Staged("dd"), TestToken());
            await AcworldStore.MarkAppliedAsync(acworld, fingerprint, TestToken());

            await AcworldStore.UpsertStagedAsync(acworld, Staged("dd") with { Label = "new label" }, TestToken());
            var back = Assert.Single(await AcworldStore.ListAsync(acworld, "test-source", false, TestToken()));
            Assert.Equal(ChangeStatus.Applied, back.Status);
            Assert.Equal("new label", back.Label);

            await AcworldStore.UpsertStagedAsync(acworld, Staged("dd"), TestToken());
            Assert.Single(await AcworldStore.ListAsync(acworld, "test-source", false, TestToken()));
        }
        finally
        {
            await DropDatabaseAsync(acworld);
        }
    }
}
