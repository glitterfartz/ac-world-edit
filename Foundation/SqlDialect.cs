using System.Globalization;
using static ACWorldEdit.Foundation.SqlExport;

namespace ACWorldEdit.Foundation;

/// <summary>
/// One planned change: the guarded statement and the preflight/post-check SELECT for each direction.
/// </summary>
public sealed record SqlStep(string Label, string ApplyStatement, string RevertStatement, string ApplyCheck, string RevertCheck);

/// <summary>
/// The statements of one dialect for the edits that are not plain table rows: graveyards
/// and waypoints. Each method returns guarded steps in the form <see cref="SqlExport"/> assembles, or
/// throws <see cref="InvalidDataException"/> when the dialect cannot export that edit yet.
/// </summary>
public interface ISqlDialect
{
    IEnumerable<SqlStep> Graveyard(GraveyardEdit edit);
    SqlStep Waypoint(WaypointEdit edit);
}

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

/// <summary>
/// TrinityCore master statements: graveyards in world_safe_locs and waypoints in waypoint_path_node.
/// </summary>
public sealed class TcSqlDialect : ISqlDialect
{
    public static readonly TcSqlDialect Instance = new();

    /// <summary>conditions source type for a graveyard zone link, and the team condition type.</summary>
    public const int GraveyardCondition = 27, TeamCondition = 6;

    /// <summary>
    /// A graveyard is a world_safe_locs row (position, name), its graveyard_zone links (zone, comment)
    /// and, per link, at most one plain team row in `conditions` (source type 27, SourceGroup = zone,
    /// SourceEntry = graveyard, CONDITION_TEAM with the team in ConditionValue1); no team row is
    /// neutral. Facing is not edited. A link with any other conditions (battleground world states,
    /// area checks, else-groups) never reaches here: the source marks it locked and the editor
    /// refuses faction changes and unlinking for it. A new team row's preflight requires that the link
    /// has no conditions at all, so a link that gained some since the draft reads STALE.
    /// </summary>
    public IEnumerable<SqlStep> Graveyard(GraveyardEdit edit)
    {
        var before = edit.Before.Normalized();
        var after = edit.After.Normalized();
        string pos(GraveyardRecord r) => $"`ID` = {r.Id} AND {Near("LocX", r.X)} AND {Near("LocY", r.Y)} AND {Near("LocZ", r.Z)} AND {TextEq("Comment", r.Comment)}";
        if (before.X != after.X || before.Y != after.Y || before.Z != after.Z || before.Comment != after.Comment)
        {
            string set(GraveyardRecord r) => $"`LocX` = {Num(r.X)}, `LocY` = {Num(r.Y)}, `LocZ` = {Num(r.Z)}, `Comment` = {Quote(r.Comment)}";
            string label = $"graveyard {edit.Id} \"{before.Comment}\": position/name";
            yield return new(label,
                $"UPDATE `world_safe_locs` SET {set(after)} WHERE {pos(before)};",
                $"UPDATE `world_safe_locs` SET {set(before)} WHERE {pos(after)};",
                Check(label, "world_safe_locs", pos(before)), Check(label, "world_safe_locs", pos(after)));
        }

        string key(uint zone) => $"`ID` = {edit.Id} AND `GhostZone` = {zone}";
        string full(GraveyardLink l) => $"{key(l.GhostZone)} AND {TextEq("Comment", l.Comment)}";
        string insert(GraveyardLink l) => $"INSERT INTO `graveyard_zone` (`ID`, `GhostZone`, `Comment`) VALUES ({edit.Id}, {l.GhostZone}, {Quote(l.Comment)});";
        string delete(GraveyardLink l) => $"DELETE FROM `graveyard_zone` WHERE {full(l)};";
        string anyCondition(uint zone) => $"`SourceTypeOrReferenceId` = {GraveyardCondition} AND `SourceGroup` = {zone} AND `SourceEntry` = {edit.Id}";
        string team(uint zone, ushort faction) => $"{anyCondition(zone)} AND `SourceId` = 0 AND `ElseGroup` = 0 AND `ConditionTypeOrReference` = {TeamCondition} AND `ConditionTarget` = 0 AND `ConditionValue1` = {faction} AND `ConditionValue2` = 0 AND `ConditionValue3` = 0 AND `ConditionStringValue1` = '' AND `NegativeCondition` = 0";
        string insertTeam(GraveyardLink l) => "INSERT INTO `conditions` (`SourceTypeOrReferenceId`, `SourceGroup`, `SourceEntry`, `SourceId`, `ElseGroup`, `ConditionTypeOrReference`, `ConditionTarget`, `ConditionValue1`, `ConditionValue2`, `ConditionValue3`, `ConditionStringValue1`, `NegativeCondition`, `ErrorType`, `ErrorTextId`, `ScriptName`, `Comment`) "
            + $"VALUES ({GraveyardCondition}, {l.GhostZone}, {edit.Id}, 0, 0, {TeamCondition}, 0, {l.Faction}, 0, 0, '', 0, 0, 0, '', {Quote(TeamComment(edit.Id, after.Comment, l))});";
        string deleteTeam(GraveyardLink l) => $"DELETE FROM `conditions` WHERE {team(l.GhostZone, l.Faction)};";
        SqlStep addTeam(string label, GraveyardLink l) => new(label, insertTeam(l), deleteTeam(l), Absent(label, "conditions", anyCondition(l.GhostZone)), Check(label, "conditions", team(l.GhostZone, l.Faction)));
        SqlStep removeTeam(string label, GraveyardLink l) => new(label, deleteTeam(l), insertTeam(l), Check(label, "conditions", team(l.GhostZone, l.Faction)), Absent(label, "conditions", anyCondition(l.GhostZone)));
        foreach (var (zone, old, next) in AcSqlDialect.ChangedLinks(before, after))
        {
            if (old == null)
            {
                string label = $"graveyard {edit.Id}: link zone {zone}";
                yield return new(label, insert(next!), delete(next!), Absent(label, "graveyard_zone", key(zone)), Check(label, "graveyard_zone", full(next!)));
                if (next!.Faction != 0)
                {
                    yield return addTeam($"graveyard {edit.Id}: zone {zone} team {next.Faction}", next);
                }
            }
            else if (next == null)
            {
                if (old.Faction != 0)
                {
                    yield return removeTeam($"graveyard {edit.Id}: zone {zone} drop team {old.Faction}", old);
                }

                string label = $"graveyard {edit.Id}: unlink zone {zone}";
                yield return new(label, delete(old), insert(old), Check(label, "graveyard_zone", full(old)), Absent(label, "graveyard_zone", key(zone)));
            }
            else
            {
                if (old.Comment != next.Comment)
                {
                    string label = $"graveyard {edit.Id}: zone {zone} comment";
                    string set(GraveyardLink l) => $"`Comment` = {Quote(l.Comment)}";
                    yield return new(label, $"UPDATE `graveyard_zone` SET {set(next)} WHERE {full(old)};", $"UPDATE `graveyard_zone` SET {set(old)} WHERE {full(next)};", Check(label, "graveyard_zone", full(old)), Check(label, "graveyard_zone", full(next)));
                }

                string factionLabel = $"graveyard {edit.Id}: zone {zone} faction {old.Faction} -> {next.Faction}";
                if (old.Faction == next.Faction)
                {
                    continue;
                }
                else if (old.Faction == 0)
                {
                    yield return addTeam(factionLabel, next);
                }
                else if (next.Faction == 0)
                {
                    yield return removeTeam(factionLabel, old);
                }
                else
                {
                    // Only the team changes; the row's comment keeps its text.
                    yield return new(factionLabel,
                        $"UPDATE `conditions` SET `ConditionValue1` = {next.Faction} WHERE {team(zone, old.Faction)};",
                        $"UPDATE `conditions` SET `ConditionValue1` = {old.Faction} WHERE {team(zone, next.Faction)};",
                        Check(factionLabel, "conditions", team(zone, old.Faction)), Check(factionLabel, "conditions", team(zone, next.Faction)));
                }
            }
        }
    }

    /// <summary>The comment TDB gives a graveyard team row: "Graveyard - ID - zone - name - zone name - Team Alliance".</summary>
    static string TeamComment(uint id, string graveyard, GraveyardLink link) =>
        $"Graveyard - {id} - {link.GhostZone} - {graveyard} - {link.Comment} - Team {(link.Faction == GraveyardLink.Horde ? "Horde" : "Alliance")}";

    /// <summary>
    /// A node of waypoint_path_node, keyed by (PathId, NodeId). Only position, facing and delay are
    /// node columns: move type and velocity belong to waypoint_path (shared by every user of the
    /// path) and smooth transition and actions do not exist, so an edit that changes them is refused
    /// instead of being dropped. As in AzerothCore only the columns that changed are set and guarded.
    /// </summary>
    public SqlStep Waypoint(WaypointEdit edit)
    {
        var (before, after) = edit.Resolve(edit.BeforeProps ?? default);
        bool propsKnown = edit.BeforeProps != null && edit.AfterProps != null;
        if (propsKnown && !before.Props.EditableOn(WorldDialect.TrinityCore, after.Props))
        {
            throw new InvalidDataException("Only position, facing and delay can be exported for a TrinityCore waypoint; move type and velocity belong to the whole path.");
        }

        var changes = new List<(string Label, Func<WaypointState, string> Guard, Func<WaypointState, string> Set)>();
        if (before.Position != after.Position)
        {
            changes.Add((FormattableString.Invariant($"({before.Position.X:0.###}, {before.Position.Y:0.###}, {before.Position.Z:0.###}) -> ({after.Position.X:0.###}, {after.Position.Y:0.###}, {after.Position.Z:0.###})"),
                s => $"{Near("PositionX", s.Position.X)} AND {Near("PositionY", s.Position.Y)} AND {Near("PositionZ", s.Position.Z)}",
                s => $"`PositionX` = {Num(s.Position.X)}, `PositionY` = {Num(s.Position.Y)}, `PositionZ` = {Num(s.Position.Z)}"));
        }

        if (propsKnown && before.Props.Orientation != after.Props.Orientation)
        {
            changes.Add((FormattableString.Invariant($"facing {before.Props.Orientation} -> {after.Props.Orientation}"),
                s => s.Props.Orientation is double o ? Near("Orientation", o) : "`Orientation` IS NULL",
                s => $"`Orientation` = {(s.Props.Orientation is double o ? Num(o) : "NULL")}"));
        }

        if (propsKnown && before.Props.Delay != after.Props.Delay)
        {
            changes.Add(($"delay {before.Props.Delay} -> {after.Props.Delay}", s => $"`Delay` = {s.Props.Delay}", s => $"`Delay` = {s.Props.Delay}"));
        }

        string key = $"`PathId` = {edit.Path} AND `NodeId` = {edit.Point}";
        string where(WaypointState s) => string.Join(" AND ", changes.Select(c => c.Guard(s)).Prepend(key));
        string set(WaypointState s) => string.Join(", ", changes.Select(c => c.Set(s)));
        string label = $"waypoint path {edit.Path} node {edit.Point}: {string.Join("; ", changes.Select(c => c.Label))}";
        return new(label,
            $"UPDATE `waypoint_path_node` SET {set(after)} WHERE {where(before)};",
            $"UPDATE `waypoint_path_node` SET {set(before)} WHERE {where(after)};",
            Check(label, "waypoint_path_node", where(before)), Check(label, "waypoint_path_node", where(after)));
    }
}
