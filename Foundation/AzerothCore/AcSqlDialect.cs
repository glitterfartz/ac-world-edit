using System.Globalization;
using static ACWorldEdit.Foundation.SqlExport;

namespace ACWorldEdit.Foundation;

/// <summary>AzerothCore 3.3.5 statements: game_graveyard, graveyard_zone.Faction and waypoint_data.</summary>
public sealed class AcSqlDialect : ISqlDialect
{
    public static readonly AcSqlDialect Instance = new();

    public IEnumerable<SqlStep> Graveyard(GraveyardEdit edit)
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
        foreach (var (zone, old, next) in ChangedLinks(before, after))
        {
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

    /// <summary>The zones whose link was added, removed or changed, in zone order (null = no link on that side).</summary>
    internal static IEnumerable<(uint Zone, GraveyardLink? Old, GraveyardLink? Next)> ChangedLinks(GraveyardRecord before, GraveyardRecord after)
    {
        var oldLinks = before.Links.ToDictionary(l => l.GhostZone);
        var newLinks = after.Links.ToDictionary(l => l.GhostZone);
        foreach (var zone in oldLinks.Keys.Union(newLinks.Keys).OrderBy(z => z))
        {
            oldLinks.TryGetValue(zone, out var old);
            newLinks.TryGetValue(zone, out var next);
            if (old != next)
            {
                yield return (zone, old, next);
            }
        }
    }

    /// <summary>
    /// Only the columns that changed are set and guarded (a position change sets all three
    /// coordinates), so edits to other columns made since the draft do not block the script. An
    /// edit that only moves a point reads and exports exactly as it did before properties were
    /// editable, which keeps already-exported moves recognizable.
    /// </summary>
    public SqlStep Waypoint(WaypointEdit edit)
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
}
