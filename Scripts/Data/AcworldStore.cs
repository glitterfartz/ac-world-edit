using ACWorldEdit.Foundation;
using MySqlConnector;

namespace ACWorldEdit.Data;

/// <summary>
/// The editor's own tracking database (default: `acworld` on the same server as the world).
/// Keeps one row per net draft change with its guarded apply/revert scripts, lifecycle status,
/// timestamps and the user's optional note. Reads stay in read-only transactions; history writes go
/// here, and explicit applies/reverts from Change History go through <see cref="WorldApplier"/>.
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
    /// every write binds its own notes value instead. `change_key` is the stable logical identity;
    /// `fingerprint` remains the script hash for exports and integrity checks.
    /// </summary>
    public static string CreateTableStatement(DatabaseSettings acworld) => $"""
        CREATE TABLE IF NOT EXISTS {QualifiedTable(acworld)} (
          `fingerprint` CHAR(64) NOT NULL,
          `change_key` CHAR(64) NOT NULL,
          `source_key` VARCHAR(128) NOT NULL,
          `dialect` VARCHAR(16) NOT NULL DEFAULT 'AzerothCore',
          `label` TEXT NOT NULL,
          `apply_sql` MEDIUMTEXT NOT NULL,
          `revert_sql` MEDIUMTEXT NOT NULL,
          `scripts_hash` CHAR(64) NOT NULL DEFAULT '',
          `status` VARCHAR(16) NOT NULL DEFAULT 'staged',
          `applied_by` VARCHAR(64) NOT NULL DEFAULT '',
          `notes` TEXT NOT NULL,
          `created_at` DATETIME(3) NOT NULL,
          `applied_at` DATETIME(3) NULL,
          `reverted_at` DATETIME(3) NULL,
          PRIMARY KEY (`fingerprint`),
          UNIQUE KEY `uq_source_change` (`source_key`, `change_key`),
          INDEX `idx_source_status` (`source_key`, `status`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """;

    /// <summary>Every column the store reads or writes, with the definition used when adding it to an older table.</summary>
    public static IReadOnlyList<(string Column, string Definition)> RequiredColumns { get; } =
    [
        ("notes", "`notes` TEXT NOT NULL"),
        ("applied_at", "`applied_at` DATETIME(3) NULL"),
        ("reverted_at", "`reverted_at` DATETIME(3) NULL"),
        ("change_key", "`change_key` CHAR(64) NOT NULL DEFAULT ''"),
        ("scripts_hash", "`scripts_hash` CHAR(64) NOT NULL DEFAULT ''"),
        ("applied_by", "`applied_by` VARCHAR(64) NOT NULL DEFAULT ''"),
    ];

    /// <summary>
    /// Only the missing columns, as plain <c>ALTER TABLE ... ADD COLUMN</c> statements. Nothing is
    /// emitted when the table already has every column, so a DML-only history database needs no DDL.
    /// </summary>
    public static IReadOnlyList<string> AddColumnStatements(DatabaseSettings acworld, IEnumerable<string> existingColumns)
    {
        var present = new HashSet<string>(existingColumns, StringComparer.OrdinalIgnoreCase);
        return RequiredColumns
            .Where(column => !present.Contains(column.Column))
            .Select(column => $"ALTER TABLE {QualifiedTable(acworld)} ADD COLUMN {column.Definition}")
            .ToList();
    }

    // A successful check is remembered per server/database/user, so the five-second history poll
    // never runs DDL or information-schema reads again. Failures are deliberately not cached.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> SchemaChecked = new(StringComparer.Ordinal);

    /// <summary>Forget the cached schema result, e.g. after the connection form applies new settings.</summary>
    public static void InvalidateSchemaCache() => SchemaChecked.Clear();

    static string SchemaCacheKey(DatabaseSettings acworld) =>
        $"{acworld.Host}\n{acworld.Port}\n{acworld.User}\n{acworld.Database}";

    /// <summary>
    /// Create the database and the change_log table when missing, and add only the columns an older
    /// table lacks. When everything already exists this performs no DDL at all, so a user with just
    /// SELECT/INSERT/UPDATE/DELETE on a DBA-provisioned database can use the history. The check runs
    /// once per connection/profile; a failed check is not cached as success.
    /// </summary>
    public static async Task EnsureSchemaAsync(DatabaseSettings acworld, CancellationToken token)
    {
        if (string.IsNullOrEmpty(acworld.Database))
        {
            throw new DatabaseFeatureException("Enter the change-history database name.");
        }

        string cacheKey = SchemaCacheKey(acworld);
        if (SchemaChecked.ContainsKey(cacheKey))
        {
            return;
        }

        bool databaseExists = await DatabaseExistsAsync(acworld, token).ConfigureAwait(false);
        if (!databaseExists)
        {
            await CreateDatabaseAsync(acworld, token).ConfigureAwait(false);
        }

        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);

        bool tableExists = await TableExistsAsync(connection, acworld, token).ConfigureAwait(false);
        if (!tableExists)
        {
            await CreateTableAsync(connection, acworld, token).ConfigureAwait(false);
        }
        else
        {
            var columns = await ColumnNamesAsync(connection, acworld, token).ConfigureAwait(false);
            foreach (string alter in AddColumnStatements(acworld, columns))
            {
                await ExecuteDdlAsync(connection, alter, acworld, token).ConfigureAwait(false);
            }

            await BackfillAsync(connection, acworld, token).ConfigureAwait(false);
            await EnsureUniqueKeyAsync(connection, acworld, token).ConfigureAwait(false);
        }

        SchemaChecked[cacheKey] = 0;
    }

    static async Task<bool> DatabaseExistsAsync(DatabaseSettings acworld, CancellationToken token)
    {
        var builder = new MySqlConnectionStringBuilder(acworld.BuildConnectionString()) { Database = "" };
        try
        {
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync(token).ConfigureAwait(false);
            await using var command = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = @name", connection);
            command.Parameters.AddWithValue("@name", acworld.Database);
            return Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false)) > 0;
        }
        catch (MySqlException error) when (error.Number is 1044 or 1045 or 1142 or 1143)
        {
            throw new DatabaseFeatureException(AcworldDiagnostics.DescribeCode(error.Number, acworld));
        }
    }

    static async Task CreateDatabaseAsync(DatabaseSettings acworld, CancellationToken token)
    {
        var builder = new MySqlConnectionStringBuilder(acworld.BuildConnectionString()) { Database = "" };
        try
        {
            await using var server = new MySqlConnection(builder.ConnectionString);
            await server.OpenAsync(token).ConfigureAwait(false);
            await using var create = new MySqlCommand(
                $"CREATE DATABASE `{acworld.Database.Replace("`", "``")}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci", server);
            await create.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        catch (MySqlException error) when (error.Number is 1044 or 1045 or 1142 or 1143)
        {
            throw new DatabaseFeatureException(AcworldDiagnostics.DescribeCode(error.Number, acworld));
        }
    }

    static async Task<bool> TableExistsAsync(MySqlConnection connection, DatabaseSettings acworld, CancellationToken token)
    {
        await using var command = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @table", connection);
        command.Parameters.AddWithValue("@db", acworld.Database);
        command.Parameters.AddWithValue("@table", Table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false)) > 0;
    }

    static async Task CreateTableAsync(MySqlConnection connection, DatabaseSettings acworld, CancellationToken token)
    {
        await using var table = new MySqlCommand(CreateTableStatement(acworld), connection);
        try
        {
            await table.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        catch (MySqlException error) when (error.Number is 1044 or 1045 or 1142 or 1143)
        {
            throw new DatabaseFeatureException(AcworldDiagnostics.DescribeCode(error.Number, acworld));
        }
    }

    static async Task<HashSet<string>> ColumnNamesAsync(MySqlConnection connection, DatabaseSettings acworld, CancellationToken token)
    {
        await using var command = new MySqlCommand(
            "SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @table", connection);
        command.Parameters.AddWithValue("@db", acworld.Database);
        command.Parameters.AddWithValue("@table", Table);
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    static async Task ExecuteDdlAsync(MySqlConnection connection, string statement, DatabaseSettings acworld, CancellationToken token)
    {
        try
        {
            await using var command = new MySqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        catch (MySqlException error) when (error.Number is 1044 or 1045 or 1142 or 1143)
        {
            throw new DatabaseFeatureException(AcworldDiagnostics.DescribeCode(error.Number, acworld));
        }
    }

    /// <summary>
    /// Fill the new identity columns for rows written by an earlier version. The old fingerprint is
    /// the only stable key those rows had, so it becomes their change key; the script seal is
    /// computed from their existing script pair.
    /// </summary>
    static async Task BackfillAsync(MySqlConnection connection, DatabaseSettings acworld, CancellationToken token)
    {
        await ExecuteDdlAsync(connection,
            $"UPDATE {QualifiedTable(acworld)} SET `change_key`=`fingerprint` WHERE `change_key` IS NULL OR `change_key`=''",
            acworld, token);
        await ExecuteDdlAsync(connection,
            $"UPDATE {QualifiedTable(acworld)} SET `scripts_hash`=UPPER(SHA2(CONCAT(`source_key`, CHAR(10), `apply_sql`, CHAR(10), `revert_sql`), 256)) WHERE `scripts_hash` IS NULL OR `scripts_hash`=''",
            acworld, token);
    }

    static async Task EnsureUniqueKeyAsync(MySqlConnection connection, DatabaseSettings acworld, CancellationToken token)
    {
        await using var command = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @table AND INDEX_NAME = 'uq_source_change'", connection);
        command.Parameters.AddWithValue("@db", acworld.Database);
        command.Parameters.AddWithValue("@table", Table);
        if (Convert.ToInt64(await command.ExecuteScalarAsync(token).ConfigureAwait(false)) > 0)
        {
            return;
        }

        await ExecuteDdlAsync(connection,
            $"ALTER TABLE {QualifiedTable(acworld)} ADD UNIQUE KEY `uq_source_change` (`source_key`, `change_key`)",
            acworld, token);
    }

    public static async Task UpsertStagedAsync(DatabaseSettings acworld, ChangeRecord record, CancellationToken token) =>
        await UpsertStagedAsync(acworld, [record], token).ConfigureAwait(false);

    /// <summary>
    /// Stage a whole refresh over one connection instead of reconnecting per change. The upsert is
    /// keyed by the stable (source_key, change_key) pair, so editing a draft updates its existing row
    /// in place and keeps the user's note and first-seen timestamp.
    /// </summary>
    public static async Task UpsertStagedAsync(DatabaseSettings acworld, IReadOnlyList<ChangeRecord> records, CancellationToken token)
    {
        if (records.Count == 0)
        {
            return;
        }

        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);
        foreach (var record in records)
        {
            // A staged ghost may still carry this script fingerprint under an older change key.
            // Remove it first so the primary-key update below cannot collide with it.
            await using (var ghost = new MySqlCommand($"""
                DELETE FROM {QualifiedTable(acworld)}
                WHERE `source_key`=@source AND `fingerprint`=@fp AND `status`='staged' AND `change_key`<>@ck
                """, connection))
            {
                ghost.Parameters.AddWithValue("@source", record.SourceKey);
                ghost.Parameters.AddWithValue("@fp", record.Fingerprint);
                ghost.Parameters.AddWithValue("@ck", record.ChangeKey);
                await ghost.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            await using var command = new MySqlCommand($"""
                INSERT INTO {QualifiedTable(acworld)} (`fingerprint`, `change_key`, `source_key`, `dialect`, `label`, `apply_sql`, `revert_sql`, `scripts_hash`, `status`, `notes`, `created_at`)
                VALUES (@fp, @ck, @source, @dialect, @label, @apply, @revert, @hash, 'staged', @notes, @created)
                ON DUPLICATE KEY UPDATE
                  `fingerprint`=VALUES(`fingerprint`), `change_key`=VALUES(`change_key`), `dialect`=VALUES(`dialect`),
                  `label`=VALUES(`label`), `apply_sql`=VALUES(`apply_sql`), `revert_sql`=VALUES(`revert_sql`),
                  `scripts_hash`=VALUES(`scripts_hash`),
                  `status`=IF(`status` IN ('applied','reverted'), `status`, 'staged')
                """, connection);
            command.Parameters.AddWithValue("@fp", record.Fingerprint);
            command.Parameters.AddWithValue("@ck", record.ChangeKey);
            command.Parameters.AddWithValue("@source", record.SourceKey);
            command.Parameters.AddWithValue("@dialect", record.Dialect.ToString());
            command.Parameters.AddWithValue("@label", record.Label);
            command.Parameters.AddWithValue("@apply", record.ApplySql);
            command.Parameters.AddWithValue("@revert", record.RevertSql);
            command.Parameters.AddWithValue("@hash", record.ScriptsHash);
            command.Parameters.AddWithValue("@notes", record.Notes ?? "");
            command.Parameters.AddWithValue("@created", record.CreatedAt);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Delete staged rows for one source that no longer match a current draft. Applied, reverted and
    /// failed rows are never touched, and other sources are left alone.
    /// </summary>
    public static async Task ReconcileStagedAsync(DatabaseSettings acworld, string sourceKey, IReadOnlyCollection<string> keepChangeKeys, CancellationToken token)
    {
        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = new MySqlCommand();
        command.Connection = connection;
        if (keepChangeKeys.Count == 0)
        {
            command.CommandText = $"DELETE FROM {QualifiedTable(acworld)} WHERE `source_key`=@source AND `status`='staged'";
        }
        else
        {
            var names = new List<string>();
            int i = 0;
            foreach (string key in keepChangeKeys)
            {
                string name = "@k" + i++;
                names.Add(name);
                command.Parameters.AddWithValue(name, key);
            }

            command.CommandText = $"DELETE FROM {QualifiedTable(acworld)} WHERE `source_key`=@source AND `status`='staged' AND `change_key` NOT IN ({string.Join(", ", names)})";
        }

        command.Parameters.AddWithValue("@source", sourceKey);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    static string SelectColumns =>
        "`fingerprint`, `change_key`, `source_key`, `dialect`, `label`, `apply_sql`, `revert_sql`, `scripts_hash`, `status`, `applied_by`, `notes`, `created_at`, `applied_at`, `reverted_at`";

    public static async Task<List<ChangeRecord>> ListAsync(DatabaseSettings acworld, string? sourceKey, bool includeAllSources, CancellationToken token)
    {
        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);
        string sql = includeAllSources || string.IsNullOrEmpty(sourceKey)
            ? $"SELECT {SelectColumns} FROM {QualifiedTable(acworld)} ORDER BY `created_at` ASC, `change_key` ASC"
            : $"SELECT {SelectColumns} FROM {QualifiedTable(acworld)} WHERE `source_key`=@source ORDER BY `created_at` ASC, `change_key` ASC";
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
                reader.GetString(2),
                Enum.TryParse<WorldDialect>(reader.GetString(3), out var dialect) ? dialect : WorldDialect.AzerothCore,
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                Enum.TryParse<ChangeStatus>(reader.GetString(8), true, out var status) ? status : ChangeStatus.Staged,
                reader.GetDateTime(11),
                reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                reader.IsDBNull(13) ? null : reader.GetDateTime(13),
                reader.IsDBNull(10) ? "" : reader.GetString(10))
            {
                ChangeKey = reader.IsDBNull(1) || reader.GetString(1).Length == 0 ? reader.GetString(0) : reader.GetString(1),
                ScriptsHash = reader.IsDBNull(7) ? "" : reader.GetString(7),
                AppliedBy = reader.IsDBNull(9) ? "" : reader.GetString(9)
            });
        }

        return records;
    }

    public static async Task MarkAppliedAsync(DatabaseSettings acworld, string fingerprint, CancellationToken token) =>
        await MarkAppliedAsync(acworld, fingerprint, "", token).ConfigureAwait(false);

    /// <summary>Mark a row applied and record the client profile that ran it, so another client cannot revert it.</summary>
    public static async Task MarkAppliedAsync(DatabaseSettings acworld, string fingerprint, string appliedBy, CancellationToken token)
    {
        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = new MySqlCommand($"UPDATE {QualifiedTable(acworld)} SET `status`='applied', `applied_by`=@by, `applied_at`=UTC_TIMESTAMP(3) WHERE `fingerprint`=@fp", connection);
        command.Parameters.AddWithValue("@by", appliedBy ?? "");
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

    /// <summary>Delete one row regardless of status (used by tests and explicit maintenance).</summary>
    public static async Task DeleteAsync(DatabaseSettings acworld, string fingerprint, CancellationToken token)
    {
        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = new MySqlCommand($"DELETE FROM {QualifiedTable(acworld)} WHERE `fingerprint`=@fp", connection);
        command.Parameters.AddWithValue("@fp", fingerprint);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    /// <summary>Delete only a staged row; applied/reverted history must survive a discard or cache clear.</summary>
    public static async Task DeleteStagedAsync(DatabaseSettings acworld, string fingerprint, CancellationToken token)
    {
        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = new MySqlCommand($"DELETE FROM {QualifiedTable(acworld)} WHERE `fingerprint`=@fp AND `status`='staged'", connection);
        command.Parameters.AddWithValue("@fp", fingerprint);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    /// <summary>Delete every staged row for a source, or for the whole history database when no source is given.</summary>
    public static async Task DeleteAllStagedAsync(DatabaseSettings acworld, string? sourceKey, CancellationToken token)
    {
        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = new MySqlCommand(sourceKey == null
            ? $"DELETE FROM {QualifiedTable(acworld)} WHERE `status`='staged'"
            : $"DELETE FROM {QualifiedTable(acworld)} WHERE `status`='staged' AND `source_key`=@source", connection);
        if (sourceKey != null)
        {
            command.Parameters.AddWithValue("@source", sourceKey);
        }

        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    /// Delete finished history for one source whose last lifecycle timestamp is older than
    /// <paramref name="olderThanUtc"/>. Only applied and reverted rows are touched: staged drafts are
    /// still open edits and failed attempts stay for review, and another source is left alone.
    /// Returns the number of rows removed.
    /// </summary>
    public static async Task<int> DeleteOldAsync(DatabaseSettings acworld, string sourceKey, DateTime olderThanUtc, CancellationToken token)
    {
        await using var connection = new MySqlConnection(acworld.BuildConnectionString());
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = new MySqlCommand($"""
            DELETE FROM {QualifiedTable(acworld)}
            WHERE `source_key`=@source AND `status` IN ('applied','reverted')
              AND CASE `status` WHEN 'reverted' THEN COALESCE(`reverted_at`, `created_at`) ELSE COALESCE(`applied_at`, `created_at`) END < @before
            """, connection);
        command.Parameters.AddWithValue("@source", sourceKey);
        command.Parameters.AddWithValue("@before", olderThanUtc);
        return await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
