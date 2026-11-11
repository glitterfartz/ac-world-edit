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
    public async Task SuccessfulSchemaCheckIsNotRepeated()
    {
        var acworld = HistorySettings();
        try
        {
            await AcworldStore.EnsureSchemaAsync(acworld, TestToken());
            await using (var connection = new MySqlConnection(acworld.BuildConnectionString()))
            {
                await connection.OpenAsync(TestToken());
                await using var drop = new MySqlCommand($"DROP TABLE `{AcworldStore.Table}`", connection);
                await drop.ExecuteNonQueryAsync(TestToken());
            }

            // The cached success means the poll does not run DDL again, so the table stays absent.
            await AcworldStore.EnsureSchemaAsync(acworld, TestToken());
            await using var check = new MySqlConnection(acworld.BuildConnectionString());
            await check.OpenAsync(TestToken());
            await using var count = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'change_log'", check);
            Assert.Equal(0L, Convert.ToInt64(await count.ExecuteScalarAsync(TestToken())));
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
    public async Task EditingTheSameLogicalChangeReplacesItsStagedRow()
    {
        var acworld = HistorySettings();
        try
        {
            await AcworldStore.EnsureSchemaAsync(acworld, TestToken());
            string changeKey = new string('e', 64);
            var first = Staged("aa") with { ChangeKey = changeKey, Notes = "near the tree" };
            await AcworldStore.UpsertStagedAsync(acworld, first, TestToken());

            var edited = Staged("bb") with { ChangeKey = changeKey, Label = "moved again" };
            await AcworldStore.UpsertStagedAsync(acworld, edited, TestToken());

            var back = Assert.Single(await AcworldStore.ListAsync(acworld, "test-source", false, TestToken()));
            Assert.Equal(changeKey, back.ChangeKey);
            Assert.Equal(edited.Fingerprint, back.Fingerprint);
            Assert.Equal("moved again", back.Label);
            Assert.Equal("near the tree", back.Notes);
            Assert.Equal(first.CreatedAt, back.CreatedAt);
        }
        finally
        {
            await DropDatabaseAsync(acworld);
        }
    }

    [DbWriteFact]
    public async Task RestagingAnAppliedChangeKeepsOneAppliedRow()
    {
        var acworld = HistorySettings();
        try
        {
            await AcworldStore.EnsureSchemaAsync(acworld, TestToken());
            string changeKey = new string('f', 64);
            var first = Staged("11") with { ChangeKey = changeKey };
            await AcworldStore.UpsertStagedAsync(acworld, first, TestToken());
            await AcworldStore.MarkAppliedAsync(acworld, first.Fingerprint, TestToken());

            var restaged = Staged("12") with { ChangeKey = changeKey, Label = "same object, new draft" };
            await AcworldStore.UpsertStagedAsync(acworld, restaged, TestToken());

            var back = Assert.Single(await AcworldStore.ListAsync(acworld, "test-source", false, TestToken()));
            Assert.Equal(ChangeStatus.Applied, back.Status);
            Assert.Equal(changeKey, back.ChangeKey);
            Assert.Equal(restaged.Fingerprint, back.Fingerprint);
        }
        finally
        {
            await DropDatabaseAsync(acworld);
        }
    }

    [DbWriteFact]
    public async Task ReconcileRemovesGhostStagedRowsAndKeepsAppliedHistory()
    {
        var acworld = HistorySettings();
        try
        {
            await AcworldStore.EnsureSchemaAsync(acworld, TestToken());
            string keep = new string('1', 64);
            await AcworldStore.UpsertStagedAsync(acworld, Staged("21") with { ChangeKey = keep }, TestToken());
            await AcworldStore.UpsertStagedAsync(acworld, Staged("22") with { ChangeKey = new string('2', 64) }, TestToken());
            await AcworldStore.UpsertStagedAsync(acworld, Staged("23", "other-source") with { ChangeKey = new string('3', 64) }, TestToken());
            var applied = Staged("24") with { ChangeKey = new string('4', 64) };
            await AcworldStore.UpsertStagedAsync(acworld, applied, TestToken());
            await AcworldStore.MarkAppliedAsync(acworld, applied.Fingerprint, TestToken());

            await AcworldStore.ReconcileStagedAsync(acworld, "test-source", [keep], TestToken());

            var rows = await AcworldStore.ListAsync(acworld, "test-source", false, TestToken());
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, r => r.ChangeKey == keep && r.Status == ChangeStatus.Staged);
            Assert.Contains(rows, r => r.ChangeKey == applied.ChangeKey && r.Status == ChangeStatus.Applied);
            Assert.Single(await AcworldStore.ListAsync(acworld, "other-source", false, TestToken()));
        }
        finally
        {
            await DropDatabaseAsync(acworld);
        }
    }

    [DbWriteFact]
    public async Task DeleteStagedNeverDeletesAppliedOrRevertedHistory()
    {
        var acworld = HistorySettings();
        try
        {
            await AcworldStore.EnsureSchemaAsync(acworld, TestToken());
            var applied = Staged("31");
            var staged = Staged("32");
            await AcworldStore.UpsertStagedAsync(acworld, applied, TestToken());
            await AcworldStore.UpsertStagedAsync(acworld, staged, TestToken());
            await AcworldStore.MarkAppliedAsync(acworld, applied.Fingerprint, TestToken());

            await AcworldStore.DeleteStagedAsync(acworld, applied.Fingerprint, TestToken());
            await AcworldStore.DeleteStagedAsync(acworld, staged.Fingerprint, TestToken());

            var back = Assert.Single(await AcworldStore.ListAsync(acworld, "test-source", false, TestToken()));
            Assert.Equal(applied.Fingerprint, back.Fingerprint);
            Assert.Equal(ChangeStatus.Applied, back.Status);
        }
        finally
        {
            await DropDatabaseAsync(acworld);
        }
    }

    [DbWriteFact]
    public async Task BatchUpsertWritesEveryRecordAndSkipsUnchangedScripts()
    {
        var acworld = HistorySettings();
        try
        {
            await AcworldStore.EnsureSchemaAsync(acworld, TestToken());
            var first = Staged("41");
            var second = Staged("42");
            await AcworldStore.UpsertStagedAsync(acworld, [first, second], TestToken());

            var before = await AcworldStore.ListAsync(acworld, "test-source", false, TestToken());
            await AcworldStore.UpsertStagedAsync(acworld, before, TestToken());
            var after = await AcworldStore.ListAsync(acworld, "test-source", false, TestToken());
            Assert.Equal(before.Select(r => r.ApplySql), after.Select(r => r.ApplySql));
            Assert.Equal(2, after.Count);
        }
        finally
        {
            await DropDatabaseAsync(acworld);
        }
    }

    [DbWriteFact]
    public async Task EnsureSchemaUpgradesAnOldTableShape()
    {
        var acworld = HistorySettings();
        try
        {
            await CreateOldTableAsync(acworld);
            await AcworldStore.EnsureSchemaAsync(acworld, TestToken());

            await using var connection = new MySqlConnection(acworld.BuildConnectionString());
            await connection.OpenAsync(TestToken());
            await using var columns = new MySqlCommand(
                "SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'change_log'", connection);
            var names = new List<string>();
            await using (var reader = await columns.ExecuteReaderAsync(TestToken()))
            {
                while (await reader.ReadAsync(TestToken()))
                {
                    names.Add(reader.GetString(0));
                }
            }

            Assert.Contains("change_key", names);
            Assert.Contains("scripts_hash", names);
            Assert.Contains("notes", names);

            // The old row becomes its own change key and gets a valid-looking script seal.
            var old = Assert.Single(await AcworldStore.ListAsync(acworld, "old-source", false, TestToken()));
            Assert.Equal(old.Fingerprint, old.ChangeKey);
            Assert.False(string.IsNullOrEmpty(old.ScriptsHash));
            Assert.True(ChangeIntegrity.ScriptsHashMatches(old));
        }
        finally
        {
            await DropDatabaseAsync(acworld);
        }
    }

    static async Task CreateOldTableAsync(DatabaseSettings settings)
    {
        var builder = new MySqlConnectionStringBuilder(settings.BuildConnectionString()) { Database = "" };
        await using var server = new MySqlConnection(builder.ConnectionString);
        await server.OpenAsync(CancellationToken.None);
        await using (var createDb = new MySqlCommand($"CREATE DATABASE `{settings.Database}` CHARACTER SET utf8mb4", server))
        {
            await createDb.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using var connection = new MySqlConnection(settings.BuildConnectionString());
        await connection.OpenAsync(CancellationToken.None);
        await using (var create = new MySqlCommand($"""
            CREATE TABLE `change_log` (
              `fingerprint` CHAR(64) NOT NULL PRIMARY KEY,
              `source_key` VARCHAR(128) NOT NULL,
              `dialect` VARCHAR(16) NOT NULL DEFAULT 'AzerothCore',
              `label` TEXT NOT NULL,
              `apply_sql` MEDIUMTEXT NOT NULL,
              `revert_sql` MEDIUMTEXT NOT NULL,
              `status` VARCHAR(16) NOT NULL DEFAULT 'staged',
              `created_at` DATETIME(3) NOT NULL,
              INDEX `idx_source_status` (`source_key`, `status`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """, connection))
        {
            await create.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using var insert = new MySqlCommand($"""
            INSERT INTO `change_log` (`fingerprint`, `source_key`, `label`, `apply_sql`, `revert_sql`, `status`, `created_at`)
            VALUES ('{new string('a', 64)}', 'old-source', 'old row', '-- apply', '-- revert', 'staged', UTC_TIMESTAMP(3))
            """, connection);
        await insert.ExecuteNonQueryAsync(CancellationToken.None);
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
