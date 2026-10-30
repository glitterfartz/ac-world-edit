using System.Globalization;
using System.Text;

namespace ACWorldEdit.Foundation;

/// <summary>
/// A reviewable pair of SQL scripts for one set of net draft changes. Apply moves rows from their
/// recorded source values to the draft values; Revert is its exact inverse. Both are plain text;
/// nothing here connects to a database.
/// </summary>
public sealed record SqlScripts(string Apply, string Revert, int Changes, IReadOnlyList<string> Summary);

/// <summary>
/// Turns net draft changes into forward and reverse SQL for world-database review. Every statement
/// carries a freshness guard: its WHERE clause repeats the row's expected current values (the source
/// values when applying, the draft values when reverting), so a row someone else changed since the
/// draft was made matches nothing and is left alone. Each script opens with a read-only preflight
/// SELECT per change reporting ok/STALE, and ends with the same SELECTs as a post-check.
/// Float columns are compared with a small tolerance because MySQL does not match a FLOAT column
/// against a decimal literal exactly. Only whitelisted columns appear, so unknown columns keep their
/// database values. INSERT is unguarded on purpose: a duplicate primary key makes it fail.
/// </summary>
public static class SqlExport
{
    const string Tolerance = "0.0001";

    /// <summary>
    /// Build the scripts for one database source. Graveyards, content rows (which include spawn moves,
    /// placements and deletes) and waypoints may each be empty. The schema is the source's dialect
    /// (AzerothCore when omitted): its table catalog validates the rows and its
    /// <see cref="ISqlDialect"/> writes the other statements.
    /// </summary>
    public static SqlScripts Build(string source, IEnumerable<GraveyardEdit> graveyards, IEnumerable<RowEdit> rows, DateTime? generated = null, IEnumerable<WaypointEdit>? waypoints = null, IWorldSchema? schema = null)
    {
        schema ??= AcSchema.Instance;
        var steps = new List<SqlStep>();
        foreach (var edit in graveyards.OrderBy(e => e.Id))
        {
            steps.AddRange(schema.Sql.Graveyard(edit));
        }

        foreach (var edit in rows)
        {
            steps.Add(Row(edit, schema.Tables));
        }

        foreach (var edit in (waypoints ?? []).OrderBy(e => e.Path).ThenBy(e => e.Point))
        {
            steps.Add(schema.Sql.Waypoint(edit));
        }

        var summary = steps.Select(s => s.Label).ToList();
        string stamp = (generated ?? DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        return new(
            Script("apply", source, stamp, steps, forward: true),
            Script("revert", source, stamp, steps, forward: false),
            steps.Count,
            summary);
    }

    static string Script(string kind, string source, string stamp, List<SqlStep> steps, bool forward)
    {
        var text = new StringBuilder();
        text.Append("-- ACWorldEdit ").Append(kind).Append(" script · generated ").Append(stamp).Append('\n');
        text.Append("-- Source database: ").Append(Comment(source)).Append('\n');
        text.Append("-- ").Append(steps.Count).Append(" change(s). Review before running; ACWorldEdit never applies this itself.\n");
        text.Append(forward
            ? "-- Guards match the recorded source values: a row changed since the draft was made is left untouched.\n"
            : "-- Reverts only rows that still hold the draft values; run it on the same database the apply script ran on.\n");
        text.Append("-- Back up the affected tables first. Check the preflight result: every row must read ok.\n\n");
        if (steps.Count == 0)
        {
            text.Append("-- Nothing to export: the drafts hold no net changes.\n");
            return text.ToString();
        }

        text.Append("SET NAMES utf8mb4;\n\n-- Preflight (read-only)\n");
        foreach (var step in steps)
        {
            text.Append(forward ? step.ApplyCheck : step.RevertCheck).Append('\n');
        }

        text.Append("\nSTART TRANSACTION;\n\n");
        foreach (var step in Ordered(steps, forward))
        {
            text.Append("-- ").Append(Comment(step.Label)).Append('\n');
            text.Append(forward ? step.ApplyStatement : step.RevertStatement).Append("\n\n");
        }

        text.Append("COMMIT;\n\n-- Post-check (read-only): every row must read ok\n");
        foreach (var step in steps)
        {
            text.Append(forward ? step.RevertCheck : step.ApplyCheck).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// Reverts run in reverse order so dependent inserts are undone before what they refer to.
    /// </summary>
    static IEnumerable<SqlStep> Ordered(List<SqlStep> steps, bool forward) => forward ? steps : Enumerable.Reverse(steps);

    // ---- content rows ----------------------------------------------------------------------

    static SqlStep Row(RowEdit edit, ITableCatalog tables)
    {
        var schema = tables.Find(edit.Table);
        string table = TableIdent(schema.SqlName);
        string keyOf(Row r) => string.Join(" AND ", schema.Key.Select(k => Eq(schema, k, r[k])));
        string all(Row r) => string.Join(" AND ", schema.Columns.Select(c => Eq(schema, c.Name, r[c.Name])));
        string insert(Row r) => $"INSERT INTO {table} ({string.Join(", ", schema.Columns.Select(c => Ident(c.Name)))}) VALUES ({string.Join(", ", schema.Columns.Select(c => Literal(c.Kind, r[c.Name])))});";
        string delete(Row r) => $"DELETE FROM {table} WHERE {all(r)};";
        string keyText = edit.Key.Replace('|', '/');
        if (edit.Before == null)
        {
            var row = edit.After!;
            string label = $"{schema.Name} {keyText}: insert";
            return new(label, insert(row), delete(row), Absent(label, schema.SqlName, keyOf(row)), Check(label, schema.SqlName, all(row)));
        }

        if (edit.After == null)
        {
            var row = edit.Before;
            string label = $"{schema.Name} {keyText}: delete";
            return new(label, delete(row), insert(row), Check(label, schema.SqlName, all(row)), Absent(label, schema.SqlName, keyOf(row)));
        }

        // Update: set and guard only the columns that changed, so unrelated concurrent edits to
        // other columns do not block the script. A logical table may be a column group of a larger
        // physical table (creature_ai), so unlisted columns are never touched.
        var changed = schema.Columns.Where(c => !schema.Key.Contains(c.Name) && edit.Before[c.Name] != edit.After[c.Name]).ToList();
        string guard(Row r) => string.Join(" AND ", schema.Key.Select(k => Eq(schema, k, r[k])).Concat(changed.Select(c => Eq(schema, c.Name, r[c.Name]))));
        string assign(Row r) => string.Join(", ", changed.Select(c => $"{Ident(c.Name)} = {Literal(c.Kind, r[c.Name])}"));
        string updateLabel = $"{schema.Name} {keyText}: update {string.Join(", ", changed.Select(c => c.Name))}";
        string guardBefore = guard(edit.Before);
        string guardAfter = guard(edit.After);
        return new(updateLabel,
            $"UPDATE {table} SET {assign(edit.After)} WHERE {guardBefore};",
            $"UPDATE {table} SET {assign(edit.Before)} WHERE {guardAfter};",
            Check(updateLabel, schema.SqlName, guardBefore), Check(updateLabel, schema.SqlName, guardAfter));
    }

    static string Eq(TableSchema schema, string column, string value)
    {
        // "= NULL" never matches in SQL, so a NULL guard must be IS NULL.
        if (value == Foundation.Row.NullValue)
        {
            return $"{Ident(column)} IS NULL";
        }

        var kind = schema.Columns.First(c => c.Name == column).Kind;
        return kind switch
        {
            ColumnKind.Float => Near(column, double.Parse(value, CultureInfo.InvariantCulture)),
            ColumnKind.Text => TextEq(column, value),
            _ => $"{Ident(column)} = {Literal(kind, value)}"
        };
    }

    // ---- primitives ------------------------------------------------------------------------

    /// <summary>
    /// Preflight/post-check row: "ok" when exactly one row matches the guard.
    /// </summary>
    internal static string Check(string label, string table, string guard) => $"SELECT {Quote(label)} AS `change`, IF(COUNT(*) = 1, 'ok', 'STALE') AS `state` FROM {TableIdent(table)} WHERE {guard};";

    /// <summary>
    /// Preflight/post-check row for a row that must not exist.
    /// </summary>
    internal static string Absent(string label, string table, string guard) => $"SELECT {Quote(label)} AS `change`, IF(COUNT(*) = 0, 'ok', 'STALE') AS `state` FROM {TableIdent(table)} WHERE {guard};";

    /// <summary>A table name, quoted per part: <c>hotfixes.broadcast_text</c> becomes <c>`hotfixes`.`broadcast_text`</c> (a row of another database on the same server).</summary>
    internal static string TableIdent(string name) => string.Join('.', name.Split('.').Select(Ident));

    internal static string Ident(string name) => "`" + name.Replace("`", "``", StringComparison.Ordinal) + "`";

    internal static string Near(string column, double value) => $"ABS({Ident(column)} - ({Num(value)})) < {Tolerance}";

    // NULL text reads as empty so rows with NULL and '' compare alike; BINARY keeps the comparison
    // case- and accent-sensitive despite the column's case-insensitive collation.
    internal static string TextEq(string column, string value) => $"BINARY COALESCE({Ident(column)}, '') = BINARY {Quote(value)}";

    internal static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// Literal for a validated row value. Numeric kinds were parse-checked by Row.Validate, but are
    /// re-parsed here so only a canonical number can ever be emitted unquoted.
    /// </summary>
    static string Literal(ColumnKind kind, string value) => value == Foundation.Row.NullValue ? "NULL" : kind switch
    {
        ColumnKind.Int => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        ColumnKind.UInt => uint.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        ColumnKind.Long => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        ColumnKind.ULong => ulong.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        ColumnKind.Float => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
        _ => Quote(value)
    };

    /// <summary>
    /// MySQL string literal with backslash escaping (the default sql_mode). Control characters are
    /// escaped so the script stays one statement per line and survives any editor.
    /// </summary>
    public static string Quote(string text)
    {
        var result = new StringBuilder(text.Length + 2).Append('\'');
        foreach (char c in text)
        {
            result.Append(c switch
            {
                '\'' => "\\'",
                '\\' => "\\\\",
                '\0' => "\\0",
                '\n' => "\\n",
                '\r' => "\\r",
                '\u001a' => "\\Z",
                _ => c.ToString()
            });
        }

        return result.Append('\'').ToString();
    }

    /// <summary>
    /// Single-line text safe inside a "--" comment.
    /// </summary>
    static string Comment(string text) => text.Replace('\r', ' ').Replace('\n', ' ');
}
