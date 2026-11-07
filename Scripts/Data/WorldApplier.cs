using MySqlConnector;

namespace ACWorldEdit.Data;

/// <summary>
/// Runs a tracked change's guarded SQL against the world database. Preflight SELECTs run first and
/// any STALE row aborts the apply before anything is written; the body then runs in one transaction
/// with a post-check. Read-only preflights never commit; only the body transaction writes.
/// </summary>
public static class WorldApplier
{
    /// <summary>Run one stored script (apply or revert text from <see cref="AcworldStore"/>).</summary>
    /// <returns>Null on success, otherwise a user-facing reason (stale rows, MySQL error).</returns>
    public static async Task<string?> ExecuteAsync(DatabaseSettings world, string script, CancellationToken token)
    {
        var (checks, body, postChecks) = SplitScript(script);
        try
        {
            await using var connection = new MySqlConnection(world.BuildConnectionString());
            await connection.OpenAsync(token).ConfigureAwait(false);
            await using (var names = new MySqlCommand("SET NAMES utf8mb4", connection))
            {
                await names.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            string? stale = await RunChecksAsync(connection, checks, token).ConfigureAwait(false);
            if (stale != null)
            {
                return stale;
            }

            await using var transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
            try
            {
                foreach (string statement in body)
                {
                    await using var command = new MySqlCommand(statement, connection, transaction);
                    command.CommandTimeout = 30;
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await transaction.CommitAsync(token).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    await transaction.RollbackAsync(token).ConfigureAwait(false);
                }
                catch
                {
                }

                throw;
            }

            string? post = await RunChecksAsync(connection, postChecks, token).ConfigureAwait(false);
            if (post != null)
            {
                return post + " The statements ran, but the post-check did not confirm them.";
            }

            return null;
        }
        catch (MySqlException error)
        {
            return error.ErrorCode switch
            {
                MySqlErrorCode.AccessDenied => $"Access denied for user '{world.User}'. The apply needs write access to the world database.",
                MySqlErrorCode.DatabaseAccessDenied => $"User '{world.User}' may not write to database '{world.Database}'.",
                _ => $"MySQL error {error.Number} while applying. Nothing was partially applied (the transaction rolled back)."
            };
        }
        catch (OperationCanceledException)
        {
            return "Timed out talking to the world database. Nothing was partially applied.";
        }
    }

    internal static (List<string> Checks, List<string> Body, List<string> PostChecks) SplitScript(string script)
    {
        var checks = new List<string>();
        var body = new List<string>();
        var post = new List<string>();
        bool inBody = false, afterCommit = false;
        foreach (string raw in script.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("START TRANSACTION", StringComparison.OrdinalIgnoreCase))
            {
                inBody = true;
                continue;
            }

            if (line.StartsWith("COMMIT", StringComparison.OrdinalIgnoreCase))
            {
                inBody = false;
                afterCommit = true;
                continue;
            }

            if (line.Length == 0 || line.StartsWith("--", StringComparison.Ordinal) || line.StartsWith("SET NAMES", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (inBody)
            {
                body.Add(raw.TrimEnd());
            }
            else if (!afterCommit && line.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                checks.Add(raw.TrimEnd());
            }
            else if (afterCommit && line.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                post.Add(raw.TrimEnd());
            }
        }

        return (checks, body, post);
    }

    static async Task<string?> RunChecksAsync(MySqlConnection connection, List<string> checks, CancellationToken token)
    {
        foreach (string check in checks)
        {
            await using var command = new MySqlCommand(check, connection);
            object? result = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
            // Checks report 'ok' vs 'STALE' as the `state` column; a single-column scalar is the state.
            // Some drivers return the first column instead, so accept any field containing ok.
            string text = Convert.ToString(result) ?? "";
            if (!text.Contains("ok", StringComparison.OrdinalIgnoreCase))
            {
                // Re-read the full row for a named stale change when the scalar was the change label.
                try
                {
                    await using var readerCommand = new MySqlCommand(check, connection);
                    await using var reader = await readerCommand.ExecuteReaderAsync(token).ConfigureAwait(false);
                    if (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        string row = string.Join(" | ", Enumerable.Range(0, reader.FieldCount).Select(i => Convert.ToString(reader.GetValue(i)) ?? ""));
                        if (row.Contains("STALE", StringComparison.OrdinalIgnoreCase))
                        {
                            return $"A row changed since the edit was made, so nothing was applied ({row}). Refresh and re-stage the change.";
                        }
                    }
                }
                catch
                {
                }

                return $"A preflight check did not read ok, so nothing was applied ({text}). Refresh and re-stage the change.";
            }
        }

        return null;
    }
}
