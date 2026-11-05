namespace ACWorldEdit.Foundation;

/// <summary>
/// Picks the ID of a new row (a spawn GUID, a template entry, a menu, a loot entry). The database
/// never hands one out for the app, so the next free ID has to clear three things at once: every row
/// already in the database, every row the draft has staged (including undone ones, so a redo cannot
/// collide), and the start of the range the user reserved for custom content, which keeps new IDs
/// from landing inside a later official data import.
/// </summary>
public static class IdAllocator
{
    /// <summary>The first ID past <paramref name="databaseMax"/> and <paramref name="stagedMax"/>, and not below <paramref name="rangeStart"/> (0 for no reserved range).</summary>
    public static ulong Next(ulong databaseMax, ulong stagedMax, ulong rangeStart = 0)
    {
        ulong floor = rangeStart == 0 ? 0 : rangeStart - 1;
        return Math.Max(Math.Max(databaseMax, stagedMax), floor) + 1;
    }

    /// <summary>
    /// Several consecutive new IDs, for a command that inserts a group at once (a paste of ten spawns).
    /// They are contiguous, so a command's IDs read as one block in the exported script.
    /// </summary>
    public static ulong[] Block(int count, ulong databaseMax, ulong stagedMax, ulong rangeStart = 0)
    {
        ulong first = Next(databaseMax, stagedMax, rangeStart);
        return Enumerable.Range(0, count).Select(i => first + (ulong)i).ToArray();
    }
}
