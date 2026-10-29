namespace ACWorldEdit.Foundation;

/// <summary>How many rows of one table name a spawn.</summary>
public sealed record SpawnImpactItem(SpawnReference Reference, int Count);

/// <summary>
/// The waypoint path a creature walks (its addon's path ID): how many nodes it has and how many other
/// creatures and templates use it. Deleting the creature removes its addon row, the only link to a path
/// nothing else uses, so that path is left behind.
/// </summary>
public sealed record SpawnPathUse(ulong Guid, ulong PathId, int Nodes, int OtherUsers)
{
    /// <summary>True when nothing but this creature walks the path.</summary>
    public bool WouldBeOrphaned => OtherUsers == 0;
}

/// <summary>
/// What deleting spawns leaves behind: the rows in other tables that still name their GUIDs, and the
/// waypoint paths nothing else would use. The exported delete removes the spawn and its addon only.
/// </summary>
public static class SpawnImpact
{
    public static int Total(IEnumerable<SpawnImpactItem> items) => items.Sum(i => i.Count);

    /// <summary>The paths that would be left unused (a path other creatures walk is not a problem).</summary>
    public static IReadOnlyList<SpawnPathUse> Orphaned(IEnumerable<SpawnPathUse>? paths) => (paths ?? []).Where(p => p.WouldBeOrphaned && p.Nodes > 0).ToArray();

    /// <summary>One line per table or path, e.g. "2 game event spawn rows (game_event_creature)".</summary>
    public static IReadOnlyList<string> Lines(IEnumerable<SpawnImpactItem> items, IEnumerable<SpawnPathUse>? paths = null) => items
        .Where(i => i.Count > 0)
        .Select(i => $"{i.Count} {i.Reference.Meaning} ({i.Reference.Table})")
        .Concat(Orphaned(paths).Select(p => $"waypoint path {p.PathId} ({p.Nodes} node{(p.Nodes == 1 ? "" : "s")}) that nothing else uses stays in the database unused"))
        .ToArray();

    /// <summary>True when a delete has something to warn about.</summary>
    public static bool Any(IEnumerable<SpawnImpactItem> items, IEnumerable<SpawnPathUse>? paths = null) => Total(items) > 0 || Orphaned(paths).Count > 0;

    /// <summary>The question a delete asks when something still references the spawns.</summary>
    public static string Prompt(string subject, IEnumerable<SpawnImpactItem> items, IEnumerable<SpawnPathUse>? paths = null) =>
        $"{subject} is still referenced:\n\n{string.Join('\n', Lines(items, paths).Select(l => "• " + l))}\n\nDeleting it leaves these rows behind. Delete anyway?";
}
