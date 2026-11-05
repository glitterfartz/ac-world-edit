using static ACWorldEdit.Foundation.SqlExport;

namespace ACWorldEdit.Foundation;

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
        if (PositionStep(edit, before, after) is { } position)
        {
            yield return position;
        }

        foreach (var step in LinkSteps(edit, before, after))
        {
            yield return step;
        }
    }

    /// <summary>The world_safe_locs row, when its position or name changed.</summary>
    static SqlStep? PositionStep(GraveyardEdit edit, GraveyardRecord before, GraveyardRecord after)
    {
        if (before.X == after.X && before.Y == after.Y && before.Z == after.Z && before.Comment == after.Comment)
        {
            return null;
        }

        string pos(GraveyardRecord r) => $"`ID` = {r.Id} AND {Near("LocX", r.X)} AND {Near("LocY", r.Y)} AND {Near("LocZ", r.Z)} AND {TextEq("Comment", r.Comment)}";
        string set(GraveyardRecord r) => $"`LocX` = {Num(r.X)}, `LocY` = {Num(r.Y)}, `LocZ` = {Num(r.Z)}, `Comment` = {Quote(r.Comment)}";
        string label = $"graveyard {edit.Id} \"{before.Comment}\": position/name";
        return new(label,
            $"UPDATE `world_safe_locs` SET {set(after)} WHERE {pos(before)};",
            $"UPDATE `world_safe_locs` SET {set(before)} WHERE {pos(after)};",
            Check(label, "world_safe_locs", pos(before)), Check(label, "world_safe_locs", pos(after)));
    }

    /// <summary>The graveyard_zone link rows and, per link, the team row in `conditions`.</summary>
    static IEnumerable<SqlStep> LinkSteps(GraveyardEdit edit, GraveyardRecord before, GraveyardRecord after)
    {
        foreach (var (zone, old, next) in AcSqlDialect.ChangedLinks(before, after))
        {
            if (old == null)
            {
                string label = $"graveyard {edit.Id}: link zone {zone}";
                yield return new(label, InsertLink(edit.Id, next!), DeleteLink(edit.Id, next!), Absent(label, "graveyard_zone", LinkKey(edit.Id, zone)), Check(label, "graveyard_zone", LinkFull(edit.Id, next!)));
                if (next!.Faction != 0)
                {
                    yield return AddTeam(edit.Id, after.Comment, $"graveyard {edit.Id}: zone {zone} team {next.Faction}", next);
                }
            }
            else if (next == null)
            {
                if (old.Faction != 0)
                {
                    yield return RemoveTeam(edit.Id, after.Comment, $"graveyard {edit.Id}: zone {zone} drop team {old.Faction}", old);
                }

                string label = $"graveyard {edit.Id}: unlink zone {zone}";
                yield return new(label, DeleteLink(edit.Id, old), InsertLink(edit.Id, old), Check(label, "graveyard_zone", LinkFull(edit.Id, old)), Absent(label, "graveyard_zone", LinkKey(edit.Id, zone)));
            }
            else
            {
                if (old.Comment != next.Comment)
                {
                    string label = $"graveyard {edit.Id}: zone {zone} comment";
                    yield return new(label,
                        $"UPDATE `graveyard_zone` SET `Comment` = {Quote(next.Comment)} WHERE {LinkFull(edit.Id, old)};",
                        $"UPDATE `graveyard_zone` SET `Comment` = {Quote(old.Comment)} WHERE {LinkFull(edit.Id, next)};",
                        Check(label, "graveyard_zone", LinkFull(edit.Id, old)), Check(label, "graveyard_zone", LinkFull(edit.Id, next)));
                }

                string factionLabel = $"graveyard {edit.Id}: zone {zone} faction {old.Faction} -> {next.Faction}";
                if (old.Faction == next.Faction)
                {
                    continue;
                }
                else if (old.Faction == 0)
                {
                    yield return AddTeam(edit.Id, after.Comment, factionLabel, next);
                }
                else if (next.Faction == 0)
                {
                    yield return RemoveTeam(edit.Id, after.Comment, factionLabel, old);
                }
                else
                {
                    // Only the team changes; the row's comment keeps its text.
                    yield return new(factionLabel,
                        $"UPDATE `conditions` SET `ConditionValue1` = {next.Faction} WHERE {TeamGuard(edit.Id, zone, old.Faction)};",
                        $"UPDATE `conditions` SET `ConditionValue1` = {old.Faction} WHERE {TeamGuard(edit.Id, zone, next.Faction)};",
                        Check(factionLabel, "conditions", TeamGuard(edit.Id, zone, old.Faction)), Check(factionLabel, "conditions", TeamGuard(edit.Id, zone, next.Faction)));
                }
            }
        }
    }

    static string LinkKey(uint id, uint zone) => $"`ID` = {id} AND `GhostZone` = {zone}";

    static string LinkFull(uint id, GraveyardLink l) => $"{LinkKey(id, l.GhostZone)} AND {TextEq("Comment", l.Comment)}";

    static string InsertLink(uint id, GraveyardLink l) => $"INSERT INTO `graveyard_zone` (`ID`, `GhostZone`, `Comment`) VALUES ({id}, {l.GhostZone}, {Quote(l.Comment)});";

    static string DeleteLink(uint id, GraveyardLink l) => $"DELETE FROM `graveyard_zone` WHERE {LinkFull(id, l)};";

    static string ConditionGroup(uint id, uint zone) => $"`SourceTypeOrReferenceId` = {GraveyardCondition} AND `SourceGroup` = {zone} AND `SourceEntry` = {id}";

    static string TeamGuard(uint id, uint zone, ushort faction) => $"{ConditionGroup(id, zone)} AND `SourceId` = 0 AND `ElseGroup` = 0 AND `ConditionTypeOrReference` = {TeamCondition} AND `ConditionTarget` = 0 AND `ConditionValue1` = {faction} AND `ConditionValue2` = 0 AND `ConditionValue3` = 0 AND `ConditionStringValue1` = '' AND `NegativeCondition` = 0";

    static string InsertTeam(uint id, string graveyard, GraveyardLink l) => "INSERT INTO `conditions` (`SourceTypeOrReferenceId`, `SourceGroup`, `SourceEntry`, `SourceId`, `ElseGroup`, `ConditionTypeOrReference`, `ConditionTarget`, `ConditionValue1`, `ConditionValue2`, `ConditionValue3`, `ConditionStringValue1`, `NegativeCondition`, `ErrorType`, `ErrorTextId`, `ScriptName`, `Comment`) "
        + $"VALUES ({GraveyardCondition}, {l.GhostZone}, {id}, 0, 0, {TeamCondition}, 0, {l.Faction}, 0, 0, '', 0, 0, 0, '', {Quote(TeamComment(id, graveyard, l))});";

    static string DeleteTeam(uint id, GraveyardLink l) => $"DELETE FROM `conditions` WHERE {TeamGuard(id, l.GhostZone, l.Faction)};";

    static SqlStep AddTeam(uint id, string graveyard, string label, GraveyardLink l) =>
        new(label, InsertTeam(id, graveyard, l), DeleteTeam(id, l), Absent(label, "conditions", ConditionGroup(id, l.GhostZone)), Check(label, "conditions", TeamGuard(id, l.GhostZone, l.Faction)));

    static SqlStep RemoveTeam(uint id, string graveyard, string label, GraveyardLink l) =>
        new(label, DeleteTeam(id, l), InsertTeam(id, graveyard, l), Check(label, "conditions", TeamGuard(id, l.GhostZone, l.Faction)), Absent(label, "conditions", ConditionGroup(id, l.GhostZone)));

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
