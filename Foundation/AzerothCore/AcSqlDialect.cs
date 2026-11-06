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
}
