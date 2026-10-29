namespace ACWorldEdit.Foundation;

/// <summary>How many rows of one table name a spawn.</summary>
public sealed record SpawnImpactItem(SpawnReference Reference, int Count);

/// <summary>
/// What deleting spawns leaves behind: the rows in other tables that still name their GUIDs. The
/// exported delete removes the spawn and its addon only, so these rows would dangle.
/// </summary>
public static class SpawnImpact
{
    public static int Total(IEnumerable<SpawnImpactItem> items) => items.Sum(i => i.Count);

    /// <summary>One line per table, e.g. "2 game event spawn rows (game_event_creature)".</summary>
    public static IReadOnlyList<string> Lines(IEnumerable<SpawnImpactItem> items) => items
        .Where(i => i.Count > 0)
        .Select(i => $"{i.Count} {i.Reference.Meaning} ({i.Reference.Table})")
        .ToArray();

    /// <summary>The question a delete asks when something still references the spawns.</summary>
    public static string Prompt(string subject, IEnumerable<SpawnImpactItem> items) =>
        $"{subject} is still referenced:\n\n{string.Join('\n', Lines(items).Select(l => "• " + l))}\n\nDeleting it leaves these rows behind. Delete anyway?";
}
