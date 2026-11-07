using ACWorldEdit.Data;
using ACWorldEdit.Tests.Support;
using MySqlConnector;
using Xunit;
using static Test;

namespace ACWorldEdit.Tests.Db;

/// <summary>
/// Live interaction tests for the guarded world apply: run real scripts against a throwaway table
/// in the world database, proving apply, revert, stale-abort and rollback end to end. The table
/// name is unique per test and always dropped, so the world is left exactly as found. Gated on
/// ACWORLD_DB_INFO like every other Db suite.
/// </summary>
[Trait("Category", "Db")]
public class WorldApplierDbTests
{
    static string TempTable() => "acwe_apply_" + Guid.NewGuid().ToString("N")[..12];

    static CancellationToken TestToken() => new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token;

    static string Script(string preflight, string body, string postCheck) => $"""
        -- ACWorldEdit applier interaction test
        SET NAMES utf8mb4;

        -- Preflight (read-only)
        {preflight}

        START TRANSACTION;

        -- test change
        {body}

        COMMIT;

        -- Post-check (read-only)
        {postCheck}
        """;

    static string Check(string table, string guard, string want) =>
        $"SELECT 't' AS `change`, IF(COUNT(*) = {(want == "ok" ? 1 : 0)}, 'ok', 'STALE') AS `state` FROM `{table}` WHERE {guard};";

    static async Task CreateTempTableAsync(string table, CancellationToken token)
    {
        await using var connection = new MySqlConnection(Config.BuildConnectionString());
        await connection.OpenAsync(token);
        await using var create = new MySqlCommand($"CREATE TABLE `{table}` (`id` INT NOT NULL PRIMARY KEY, `v` INT NOT NULL) ENGINE=InnoDB", connection);
        await create.ExecuteNonQueryAsync(token);
    }

    static async Task DropTempTableAsync(string table)
    {
        try
        {
            await using var connection = new MySqlConnection(Config.BuildConnectionString());
            await connection.OpenAsync(CancellationToken.None);
            await using var drop = new MySqlCommand($"DROP TABLE IF EXISTS `{table}`", connection);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    static async Task<long> CountRowsAsync(string table, CancellationToken token)
    {
        await using var connection = new MySqlConnection(Config.BuildConnectionString());
        await connection.OpenAsync(token);
        await using var count = new MySqlCommand($"SELECT COUNT(*) FROM `{table}`", connection);
        return Convert.ToInt64(await count.ExecuteScalarAsync(token));
    }

    [DbWriteFact]
    public async Task ApplyInsertsAndRevertDeletes()
    {
        string table = TempTable();
        var token = TestToken();
        try
        {
            await CreateTempTableAsync(table, token);
            string apply = Script(Check(table, "`id` = 1", "absent"), $"INSERT INTO `{table}` (`id`, `v`) VALUES (1, 2);", Check(table, "`id` = 1", "ok"));
            Assert.Null(await WorldApplier.ExecuteAsync(Config, apply, token));
            Assert.Equal(1, await CountRowsAsync(table, token));

            string revert = Script(Check(table, "`id` = 1", "ok"), $"DELETE FROM `{table}` WHERE `id` = 1;", Check(table, "`id` = 1", "absent"));
            Assert.Null(await WorldApplier.ExecuteAsync(Config, revert, token));
            Assert.Equal(0, await CountRowsAsync(table, token));
        }
        finally
        {
            await DropTempTableAsync(table);
        }
    }

    [DbWriteFact]
    public async Task StalePreflightAbortsBeforeWritingAnything()
    {
        string table = TempTable();
        var token = TestToken();
        try
        {
            await CreateTempTableAsync(table, token);
            // The guard expects a row that is not there, so nothing may run.
            string stale = Script(Check(table, "`id` = 1", "ok"), $"INSERT INTO `{table}` (`id`, `v`) VALUES (1, 2);", Check(table, "`id` = 1", "ok"));
            string? error = await WorldApplier.ExecuteAsync(Config, stale, token);
            Assert.NotNull(error);
            Assert.Contains("STALE", error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, await CountRowsAsync(table, token));
        }
        finally
        {
            await DropTempTableAsync(table);
        }
    }

    [DbWriteFact]
    public async Task FailedBodyRollsBackTheWholeTransaction()
    {
        string table = TempTable();
        var token = TestToken();
        try
        {
            await CreateTempTableAsync(table, token);
            string broken = Script(
                Check(table, "`id` = 1", "absent"),
                $"INSERT INTO `{table}` (`id`, `v`) VALUES (1, 2);\nINSERT INTO `{table}` (`id`, `v`) VALUES (1, 3);",
                Check(table, "`id` = 1", "ok"));
            Assert.NotNull(await WorldApplier.ExecuteAsync(Config, broken, token));
            Assert.Equal(0, await CountRowsAsync(table, token));
        }
        finally
        {
            await DropTempTableAsync(table);
        }
    }
}
