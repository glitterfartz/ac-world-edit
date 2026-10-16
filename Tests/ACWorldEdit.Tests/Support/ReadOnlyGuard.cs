using System.Text.RegularExpressions;
using ACWorldEdit.Data;

namespace ACWorldEdit.Tests.Support;

/// <summary>
/// Test-side safety net for database tests: only a single read statement (SELECT, SHOW, DESCRIBE)
/// may reach the server. The app itself already runs read-only transactions; this guard
/// makes a mistyped test query fail before it is sent. It is unit tested on text only, never by
/// sending a write to a live database.
/// </summary>
public static partial class ReadOnlyGuard
{
    static readonly Regex literals = new(@"'([^'\\]|\\.|'')*'|""([^""\\]|\\.|"""")*""|`[^`]*`");

    [GeneratedRegex(@"/\*.*?\*/|--[^\n]*|#[^\n]*", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex(@"\b(INTO|FOR\s+UPDATE|LOCK\s+IN\s+SHARE\s+MODE|SLEEP|BENCHMARK|GET_LOCK)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Forbidden();

    /// <summary>Throws InvalidOperationException unless <paramref name="sql"/> is one read-only statement.</summary>
    public static string Require(string sql)
    {
        string text = Comments().Replace(literals.Replace(sql, "''"), " ").Trim().TrimEnd(';').Trim();
        if (text.Length == 0)
        {
            throw new InvalidOperationException("Empty SQL in a database test.");
        }

        if (text.Contains(';'))
        {
            throw new InvalidOperationException("Multiple statements are not allowed in database tests.");
        }

        string keyword = text.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0].TrimStart('(').ToUpperInvariant();
        if (keyword is not ("SELECT" or "SHOW" or "DESCRIBE" or "DESC"))
        {
            throw new InvalidOperationException($"Only read statements are allowed in database tests, not {keyword}.");
        }

        if (Forbidden().IsMatch(text))
        {
            throw new InvalidOperationException("Locking, file-writing or timing constructs are not allowed in database tests.");
        }

        return sql;
    }

    /// <summary>A command on the session's read-only transaction, after the guard has accepted its text.</summary>
    public static MySqlConnector.MySqlCommand ReadOnly(this WorldReadSession session, string sql) => session.Command(Require(sql));
}
