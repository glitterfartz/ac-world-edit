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
        bool committing = false;
        try
        {
            await using var connection = new MySqlConnection(world.BuildConnectionString());
            await connection.OpenAsync(token).ConfigureAwait(false);
            await using (var names = new MySqlCommand("SET NAMES utf8mb4", connection))
            {
                await names.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            string? stale = await RunChecksAsync(connection, checks, null, token).ConfigureAwait(false);
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

                // Post-checks run inside the transaction, through the transaction connection. A
                // failing post-check rolls the body back, so the world never keeps an unverified
                // change that the history would then record as failed.
                string? post = await RunChecksAsync(connection, postChecks, transaction, token).ConfigureAwait(false);
                if (post != null)
                {
                    await transaction.RollbackAsync(token).ConfigureAwait(false);
                    return post + " The transaction rolled back, so nothing was applied.";
                }

                committing = true;
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

            return null;
        }
        catch (MySqlException error)
        {
            return error.ErrorCode switch
            {
                MySqlErrorCode.AccessDenied => $"Access denied for user '{world.User}'. The apply needs write access to the world database.",
                MySqlErrorCode.DatabaseAccessDenied => $"User '{world.User}' may not write to database '{world.Database}'.",
                _ when committing => $"MySQL error {error.Number} while committing. The commit outcome is unknown: check the affected rows before retrying.",
                _ => $"MySQL error {error.Number} while applying. Nothing was partially applied (the transaction rolled back)."
            };
        }
        catch (OperationCanceledException)
        {
            return committing
                ? "Timed out while committing to the world database. The commit outcome is unknown: check the affected rows before retrying."
                : "Timed out talking to the world database. Nothing was partially applied (the transaction rolled back).";
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

    static async Task<string?> RunChecksAsync(MySqlConnection connection, List<string> checks, MySqlTransaction? transaction, CancellationToken token)
    {
        foreach (string check in checks)
        {
            await using var command = transaction == null
                ? new MySqlCommand(check, connection)
                : new MySqlCommand(check, connection, transaction);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            var names = new List<string>();
            var values = new List<string?>();
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    names.Add(reader.GetName(i));
                    values.Add(reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i)));
                }
            }

            var (label, ok) = EvaluateCheckResult(names, values);
            if (!ok)
            {
                return string.IsNullOrEmpty(label)
                    ? "A preflight check did not read ok, so nothing was applied. Refresh and re-stage the change."
                    : $"A row changed since the edit was made, so nothing was applied ({label}). Refresh and re-stage the change.";
            }
        }

        return null;
    }

    /// <summary>
    /// Whether one preflight row reads ok. The scripts report the verdict in the `state` column and
    /// the human-readable row in `change`; the label alone must never decide, because labels like
    /// "creature 11800166: insert" contain neither "ok" nor "STALE".
    /// </summary>
    internal static (string Label, bool Ok) EvaluateCheckResult(IReadOnlyList<string> names, IReadOnlyList<string?> values)
    {
        string label = "", state = "";
        for (int i = 0; i < names.Count && i < values.Count; i++)
        {
            if (names[i].Equals("state", StringComparison.OrdinalIgnoreCase))
            {
                state = values[i] ?? "";
            }
            else if (names[i].Equals("change", StringComparison.OrdinalIgnoreCase))
            {
                label = values[i] ?? "";
            }
        }

        return (label, state.Equals("ok", StringComparison.OrdinalIgnoreCase));
    }
}
