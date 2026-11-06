using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>One spawn of a GameObject template, for the Spawns page and locate actions.</summary>
public sealed record GameObjectSpawnSample(ulong Guid, uint Map, double X, double Y, double Z);

/// <summary>
/// Read-only database snapshot of one `gameobject_template` entry: the template, its optional
/// addon, quest links, quest items and the spawns that use it. Rows are captured with the
/// dialect's table whitelist only; the draft is layered on top by the caller.
/// </summary>
public sealed class GameObjectSnapshot(
    uint entry,
    string name,
    int type,
    int spawnCount,
    IReadOnlyList<GameObjectSpawnSample> spawns,
    IReadOnlyDictionary<uint, string> questTitles,
    RowSet rows,
    string sourceKey)
{
    public uint Entry => entry;
    public string Name => name;
    public int Type => type;
    public int SpawnCount => spawnCount;
    public IReadOnlyList<GameObjectSpawnSample> Spawns => spawns;
    /// <summary>Quest titles for the starter/ender links, so the page can name them without another read.</summary>
    public IReadOnlyDictionary<uint, string> QuestTitles => questTitles;
    public RowSet Rows => rows;
    public string SourceKey => sourceKey;
}
