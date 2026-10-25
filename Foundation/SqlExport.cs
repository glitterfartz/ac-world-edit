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
    /// One planned change: the guarded statement and the preflight/post-check SELECT for each direction.
    /// </summary>
    sealed record Step(string Label, string ApplyStatement, string RevertStatement, string ApplyCheck, string RevertCheck);

    /// <summary>
    /// Build the scripts for one database source. Creature moves, graveyards and interaction rows
    /// may each be empty.
    /// </summary>
    public static SqlScripts Build(string source, IEnumerable<CreatureMove> creatures, IEnumerable<GraveyardEdit> graveyards, IEnumerable<RowEdit> rows, DateTime? generated = null, IEnumerable<WaypointEdit>? waypoints = null)
    {
        var steps = new List<Step>();
        foreach (var move in creatures.OrderBy(m => m.Guid))
        {
            steps.Add(Creature(move));
        }

        foreach (var edit in graveyards.OrderBy(e => e.Id))
        {
            steps.AddRange(Graveyard(edit));
        }

        foreach (var edit in rows)
        {
            steps.Add(Row(edit));
        }

        foreach (var edit in (waypoints ?? []).OrderBy(e => e.Path).ThenBy(e => e.Point))
        {
            steps.Add(Waypoint(edit));
        }

        var summary = steps.Select(s => s.Label).ToList();
        string stamp = (generated ?? DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        return new(
            Script("apply", source, stamp, steps, forward: true),
            Script("revert", source, stamp, steps, forward: false),
            steps.Count,
            summary);
    }

    static string Script(string kind, string source, string stamp, List<Step> steps, bool forward)
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
    static IEnumerable<Step> Ordered(List<Step> steps, bool forward) => forward ? steps : Enumerable.Reverse(steps);

    // ---- creature --------------------------------------------------------------------------

    static Step Creature(CreatureMove move)
    {
        string where(WorldPose pose) => $"`guid` = {move.Guid} AND {Near("position_x", pose.X)} AND {Near("position_y", pose.Y)} AND {Near("position_z", pose.Z)} AND {Near("orientation", pose.Heading)}";
        string set(WorldPose pose) => $"`position_x` = {Num(pose.X)}, `position_y` = {Num(pose.Y)}, `position_z` = {Num(pose.Z)}, `orientation` = {Num(pose.Heading)}";
        string label = FormattableString.Invariant($"creature guid {move.Guid}: ({move.Before.X:0.###}, {move.Before.Y:0.###}, {move.Before.Z:0.###}, {move.Before.Heading:0.###}) -> ({move.After.X:0.###}, {move.After.Y:0.###}, {move.After.Z:0.###}, {move.After.Heading:0.###})");
        return new(label,
            $"UPDATE `creature` SET {set(move.After)} WHERE {where(move.Before)};",
            $"UPDATE `creature` SET {set(move.Before)} WHERE {where(move.After)};",
            Check(label, "creature", where(move.Before)), Check(label, "creature", where(move.After)));
    }

    // ---- graveyard -------------------------------------------------------------------------

    static IEnumerable<Step> Graveyard(GraveyardEdit edit)
    {
        var before = edit.Before.Normalized();
        var after = edit.After.Normalized();
        string pos(GraveyardRecord r) => $"`ID` = {r.Id} AND {Near("x", r.X)} AND {Near("y", r.Y)} AND {Near("z", r.Z)} AND {TextEq("Comment", r.Comment)}";
        if (before.X != after.X || before.Y != after.Y || before.Z != after.Z || before.Comment != after.Comment)
        {
            string set(GraveyardRecord r) => $"`x` = {Num(r.X)}, `y` = {Num(r.Y)}, `z` = {Num(r.Z)}, `Comment` = {Quote(r.Comment)}";
            string label = $"graveyard {edit.Id} \"{before.Comment}\": position/name";
            yield return new(label,
                $"UPDATE `game_graveyard` SET {set(after)} WHERE {pos(before)};",
                $"UPDATE `game_graveyard` SET {set(before)} WHERE {pos(after)};",
                Check(label, "game_graveyard", pos(before)), Check(label, "game_graveyard", pos(after)));
        }

        string key(uint zone) => $"`ID` = {edit.Id} AND `GhostZone` = {zone}";
        string full(GraveyardLink l) => $"{key(l.GhostZone)} AND `Faction` = {l.Faction} AND {TextEq("Comment", l.Comment)}";
        string insert(GraveyardLink l) => $"INSERT INTO `graveyard_zone` (`ID`, `GhostZone`, `Faction`, `Comment`) VALUES ({edit.Id}, {l.GhostZone}, {l.Faction}, {Quote(l.Comment)});";
        string delete(GraveyardLink l) => $"DELETE FROM `graveyard_zone` WHERE {full(l)};";
        var oldLinks = before.Links.ToDictionary(l => l.GhostZone);
        var newLinks = after.Links.ToDictionary(l => l.GhostZone);
        foreach (var zone in oldLinks.Keys.Union(newLinks.Keys).OrderBy(z => z))
        {
            oldLinks.TryGetValue(zone, out var old);
            newLinks.TryGetValue(zone, out var next);
            if (old == next)
            {
                continue;
            }

            if (old == null)
            {
                string label = $"graveyard {edit.Id}: link zone {zone} (faction {next!.Faction})";
                yield return new(label, insert(next), delete(next), Absent(label, "graveyard_zone", key(zone)), Check(label, "graveyard_zone", full(next)));
            }
            else if (next == null)
            {
                string label = $"graveyard {edit.Id}: unlink zone {zone} (faction {old.Faction})";
                yield return new(label, delete(old), insert(old), Check(label, "graveyard_zone", full(old)), Absent(label, "graveyard_zone", key(zone)));
            }
            else
            {
                string set(GraveyardLink l) => $"`Faction` = {l.Faction}, `Comment` = {Quote(l.Comment)}";
                string label = $"graveyard {edit.Id}: zone {zone} faction {old.Faction} -> {next.Faction}";
                yield return new(label, $"UPDATE `graveyard_zone` SET {set(next)} WHERE {full(old)};", $"UPDATE `graveyard_zone` SET {set(old)} WHERE {full(next)};", Check(label, "graveyard_zone", full(old)), Check(label, "graveyard_zone", full(next)));
            }
        }
    }

    // ---- waypoint --------------------------------------------------------------------------

    /// <summary>
    /// Only the columns that changed are set and guarded (a position change sets all three
    /// coordinates), so edits to other columns made since the draft do not block the script. An
    /// edit that only moves a point reads and exports exactly as it did before properties were
    /// editable, which keeps already-exported moves recognizable.
    /// </summary>
    static Step Waypoint(WaypointEdit edit)
    {
        var (before, after) = edit.Resolve(edit.BeforeProps ?? default);
        bool propsKnown = edit.BeforeProps != null && edit.AfterProps != null;
        var changes = new List<(string Label, Func<WaypointState, string> Guard, Func<WaypointState, string> Set)>();
        if (before.Position != after.Position)
        {
            changes.Add((FormattableString.Invariant($"({before.Position.X:0.###}, {before.Position.Y:0.###}, {before.Position.Z:0.###}) -> ({after.Position.X:0.###}, {after.Position.Y:0.###}, {after.Position.Z:0.###})"),
                s => $"{Near("position_x", s.Position.X)} AND {Near("position_y", s.Position.Y)} AND {Near("position_z", s.Position.Z)}",
                s => $"`position_x` = {Num(s.Position.X)}, `position_y` = {Num(s.Position.Y)}, `position_z` = {Num(s.Position.Z)}"));
        }

        if (propsKnown)
        {
            void Column<T>(string name, string column, Func<WaypointProps, T> value, Func<WaypointProps, string> guard, Func<WaypointProps, string> literal)
            {
                if (!EqualityComparer<T>.Default.Equals(value(before.Props), value(after.Props)))
                {
                    changes.Add((FormattableString.Invariant($"{name} {value(before.Props)} -> {value(after.Props)}"), s => guard(s.Props), s => $"{Ident(column)} = {literal(s.Props)}"));
                }
            }

            Column("facing", "orientation", p => p.Orientation, p => p.Orientation is double o ? Near("orientation", o) : "`orientation` IS NULL", p => p.Orientation is double o ? Num(o) : "NULL");
            Column("velocity", "velocity", p => p.Velocity, p => Near("velocity", p.Velocity), p => Num(p.Velocity));
            Column("delay", "delay", p => p.Delay, p => $"`delay` = {p.Delay}", p => p.Delay.ToString(CultureInfo.InvariantCulture));
            Column("smooth", "smoothTransition", p => p.Smooth, p => $"`smoothTransition` = {(p.Smooth ? 1 : 0)}", p => p.Smooth ? "1" : "0");
            Column("move type", "move_type", p => p.MoveType, p => $"`move_type` = {p.MoveType}", p => p.MoveType.ToString(CultureInfo.InvariantCulture));
            Column("action", "action", p => p.Action, p => $"`action` = {p.Action}", p => p.Action.ToString(CultureInfo.InvariantCulture));
            Column("action chance", "action_chance", p => p.ActionChance, p => $"`action_chance` = {p.ActionChance}", p => p.ActionChance.ToString(CultureInfo.InvariantCulture));
        }

        string key = $"`id` = {edit.Path} AND `point` = {edit.Point}";
        string where(WaypointState s) => string.Join(" AND ", changes.Select(c => c.Guard(s)).Prepend(key));
        string set(WaypointState s) => string.Join(", ", changes.Select(c => c.Set(s)));
        string label = $"waypoint path {edit.Path} point {edit.Point}: {string.Join("; ", changes.Select(c => c.Label))}";
        return new(label,
            $"UPDATE `waypoint_data` SET {set(after)} WHERE {where(before)};",
            $"UPDATE `waypoint_data` SET {set(before)} WHERE {where(after)};",
            Check(label, "waypoint_data", where(before)), Check(label, "waypoint_data", where(after)));
    }

    // ---- content rows ----------------------------------------------------------------------

    static Step Row(RowEdit edit)
    {
        var schema = InteractionTables.Find(edit.Table);
        string table = Ident(schema.SqlName);
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
    static string Check(string label, string table, string guard) => $"SELECT {Quote(label)} AS `change`, IF(COUNT(*) = 1, 'ok', 'STALE') AS `state` FROM {Ident(table)} WHERE {guard};";

    /// <summary>
    /// Preflight/post-check row for a row that must not exist.
    /// </summary>
    static string Absent(string label, string table, string guard) => $"SELECT {Quote(label)} AS `change`, IF(COUNT(*) = 0, 'ok', 'STALE') AS `state` FROM {Ident(table)} WHERE {guard};";

    static string Ident(string name) => "`" + name.Replace("`", "``", StringComparison.Ordinal) + "`";

    static string Near(string column, double value) => $"ABS({Ident(column)} - ({Num(value)})) < {Tolerance}";

    // NULL text reads as empty so rows with NULL and '' compare alike; BINARY keeps the comparison
    // case- and accent-sensitive despite the column's case-insensitive collation.
    static string TextEq(string column, string value) => $"BINARY COALESCE({Ident(column)}, '') = BINARY {Quote(value)}";

    static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// Literal for a validated row value. Numeric kinds were parse-checked by Row.Validate, but are
    /// re-parsed here so only a canonical number can ever be emitted unquoted.
    /// </summary>
    static string Literal(ColumnKind kind, string value) => kind switch
    {
        ColumnKind.Int => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        ColumnKind.UInt => uint.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
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
