using ACWorldEdit.Foundation;
using MySqlConnector;

namespace ACWorldEdit.Data;

/// <summary>
/// The editor's own tracking database (default: `acworld` on the same server as the world).
/// Keeps one row per net draft change with its guarded apply/revert scripts, lifecycle status,
/// timestamps and the user's optional note. The world connection stays read-only; all writes go
/// here and through <see cref="WorldApplier"/> to the world database.
/// </summary>
public static class AcworldStore
{
    public const string DefaultDatabase = "acworld";
    public const string Table = "change_log";

    /// <summary>Derived settings when the user did not pick a separate server: same host/creds, database `acworld`.</summary>
    public static DatabaseSettings DerivedFrom(DatabaseSettings world) => world with { Database = DefaultDatabase };

    public static string QualifiedTable(DatabaseSettings acworld) => $"`{acworld.Database.Replace("`", "``")}`.`{Table}`";

    /// <summary>
    /// The change_log table definition, extracted so tests pin its shape. TEXT columns carry no
    /// DEFAULT: MySQL before 8.0.13 and MariaDB reject defaults on TEXT/BLOB with error 1101, so
    /// every write binds its own notes value instead.
    /// </summary>
    public static string CreateTableStatement(DatabaseSettings acworld) => $"""
        CREATE TABLE IF NOT EXISTS {QualifiedTable(acworld)} (
          `fingerprint` CHAR(64) NOT NULL PRIMARY KEY,
          `source_key` VARCHAR(128) NOT NULL,
          `dialect` VARCHAR(16) NOT NULL DEFAULT 'AzerothCore',
          `label` TEXT NOT NULL,
          `apply_sql` MEDIUMTEXT NOT NULL,
          `revert_sql` MEDIUMTEXT NOT NULL,
          `status` VARCHAR(16) NOT NULL DEFAULT 'staged',
          `notes` TEXT NOT NULL,
          `created_at` DATETIME(3) NOT NULL,
          `applied_at` DATETIME(3) NULL,
          `reverted_at` DATETIME(3) NULL,
          INDEX `idx_source_status` (`source_key`, `status`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """;

    /// <summary>Best-effort columns for tables created by an earlier version of this DDL.</summary>
    public static IReadOnlyList<string> MigrationStatements(DatabaseSettings acworld) =>
    [
        $"ALTER TABLE {QualifiedTable(acworld)} ADD COLUMN IF NOT EXISTS `notes` TEXT NOT NULL",
        $"ALTER TABLE {QualifiedTable(acworld)} ADD COLUMN IF NOT EXISTS `applied_at` DATETIME(3) NULL",
        $"ALTER TABLE {QualifiedTable(acworld)} ADD COLUMN IF NOT EXISTS `reverted_at` DATETIME(3) NULL",
    ];

    /// <summary>
    /// Create the database and the change_log table when missing. Needs CREATE privileges once.
    /// When the user may not create databases (error 1044 on hosted servers) but the database
    /// already exists and is usable, table creation is still attempted so a DBA-provisioned
    /// database keeps working.
    /// </summary>
    public static async Task EnsureSchemaAsync(DatabaseSettings acworld, CancellationToken token)
    {
        if (string.IsNullOrEmpty(acworld.Database))
        {
            throw new DatabaseFeatureException("Enter the change-history database name.");
        }

        var builder = new MySqlConnectionStringBuilder(acworld.BuildConnectionString()) { Database = "" };
        try
        {
            await using var server = new MySqlConnection(builder.ConnectionString);
            await server.OpenAsync(token).ConfigureAwait(false);
            await using (var create = new MySqlCommand($"CREATE DATABASE IF NOT EXISTS `{acworld.Database.Replace("`", "``")}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci", server))
            {
                await create.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }
        catch (MySqlException error) when (error.Number is 1044 or 1045)
        {
            // No permission to create databases: carry on only when the database already exists and
            // this user can open it; otherwise report exactly what to ask the admin for.
            if (!await DatabaseUsableAsync(acworld, token).ConfigureAwait(false))
            {
                throw new DatabaseFeatureException(AcworldDiagnostics.DescribeCode(error.Number, acworld));
            }
        }

        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var table = new MySqlCommand(CreateTableStatement(acworld), connection);
        await table.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        // Best-effort column additions for databases created by an earlier version of this table.
        foreach (string alter in MigrationStatements(acworld))
        {
            try
            {
                await using var add = new MySqlCommand(alter, connection);
                await add.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            catch (MySqlException)
            {
                // Old server without IF NOT EXISTS support or column already there; the table works either way.
            }
        }
    }

    static async Task<bool> DatabaseUsableAsync(DatabaseSettings acworld, CancellationToken token)
    {
        try
        {
            await using var connection = new MySqlConnection(acworld.BuildConnectionString());
            await connection.OpenAsync(token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task UpsertStagedAsync(DatabaseSettings acworld, ChangeRecord record, CancellationToken token)
    {
        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = new MySqlCommand($"""
            INSERT INTO {QualifiedTable(acworld)} (`fingerprint`, `source_key`, `dialect`, `label`, `apply_sql`, `revert_sql`, `status`, `notes`, `created_at`)
            VALUES (@fp, @source, @dialect, @label, @apply, @revert, 'staged', @notes, @created)
            ON DUPLICATE KEY UPDATE `label`=VALUES(`label`), `apply_sql`=VALUES(`apply_sql`), `revert_sql`=VALUES(`revert_sql`),
              `status`=IF(`status` IN ('applied','reverted'), `status`, 'staged')
            """, connection);
        command.Parameters.AddWithValue("@fp", record.Fingerprint);
        command.Parameters.AddWithValue("@source", record.SourceKey);
        command.Parameters.AddWithValue("@dialect", record.Dialect.ToString());
        command.Parameters.AddWithValue("@label", record.Label);
        command.Parameters.AddWithValue("@apply", record.ApplySql);
        command.Parameters.AddWithValue("@revert", record.RevertSql);
        command.Parameters.AddWithValue("@notes", record.Notes ?? "");
        command.Parameters.AddWithValue("@created", record.CreatedAt);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public static async Task<List<ChangeRecord>> ListAsync(DatabaseSettings acworld, string? sourceKey, bool includeAllSources, CancellationToken token)
    {
        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);
        string sql = includeAllSources || string.IsNullOrEmpty(sourceKey)
            ? $"SELECT `fingerprint`, `source_key`, `dialect`, `label`, `apply_sql`, `revert_sql`, `status`, `notes`, `created_at`, `applied_at`, `reverted_at` FROM {QualifiedTable(acworld)} ORDER BY `created_at` ASC"
            : $"SELECT `fingerprint`, `source_key`, `dialect`, `label`, `apply_sql`, `revert_sql`, `status`, `notes`, `created_at`, `applied_at`, `reverted_at` FROM {QualifiedTable(acworld)} WHERE `source_key`=@source ORDER BY `created_at` ASC";
        await using var command = new MySqlCommand(sql, connection);
        if (!includeAllSources && !string.IsNullOrEmpty(sourceKey))
        {
            command.Parameters.AddWithValue("@source", sourceKey);
        }

        var records = new List<ChangeRecord>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            records.Add(new ChangeRecord(
                reader.GetString(0),
                reader.GetString(1),
                Enum.TryParse<WorldDialect>(reader.GetString(2), out var dialect) ? dialect : WorldDialect.AzerothCore,
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                Enum.TryParse<ChangeStatus>(reader.GetString(6), true, out var status) ? status : ChangeStatus.Staged,
                reader.GetDateTime(8),
                reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                reader.IsDBNull(7) ? "" : reader.GetString(7)));
        }

        return records;
    }

    public static async Task MarkAppliedAsync(DatabaseSettings acworld, string fingerprint, CancellationToken token)
    {
        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = new MySqlCommand($"UPDATE {QualifiedTable(acworld)} SET `status`='applied', `applied_at`=UTC_TIMESTAMP(3) WHERE `fingerprint`=@fp", connection);
        command.Parameters.AddWithValue("@fp", fingerprint);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public static async Task MarkRevertedAsync(DatabaseSettings acworld, string fingerprint, CancellationToken token)
    {
        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = new MySqlCommand($"UPDATE {QualifiedTable(acworld)} SET `status`='reverted', `reverted_at`=UTC_TIMESTAMP(3) WHERE `fingerprint`=@fp", connection);
        command.Parameters.AddWithValue("@fp", fingerprint);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public static async Task MarkFailedAsync(DatabaseSettings acworld, string fingerprint, CancellationToken token)
    {
        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = new MySqlCommand($"UPDATE {QualifiedTable(acworld)} SET `status`='failed' WHERE `fingerprint`=@fp AND `status`='staged'", connection);
        command.Parameters.AddWithValue("@fp", fingerprint);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public static async Task UpdateNotesAsync(DatabaseSettings acworld, string fingerprint, string notes, CancellationToken token)
    {
        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = new MySqlCommand($"UPDATE {QualifiedTable(acworld)} SET `notes`=@notes WHERE `fingerprint`=@fp", connection);
        command.Parameters.AddWithValue("@notes", notes ?? "");
        command.Parameters.AddWithValue("@fp", fingerprint);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public static async Task DeleteAsync(DatabaseSettings acworld, string fingerprint, CancellationToken token)
    {
        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = new MySqlCommand($"DELETE FROM {QualifiedTable(acworld)} WHERE `fingerprint`=@fp", connection);
        command.Parameters.AddWithValue("@fp", fingerprint);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
