using System.Security.Cryptography;
using System.Text;

namespace ACWorldEdit.Foundation;

/// <summary>
/// Which draft a pending change lives in.
/// </summary>
public enum PendingKind
{
    Creature,
    GameObject,
    Graveyard,
    Row,
    Waypoint
}

/// <summary>
/// One discardable unit of unsaved work: a creature's net move, a graveyard's net edit, one
/// content row's net edit or one waypoint's net move. Id fingerprints the generated apply statements
/// (not the timestamp), so the same net change keeps its Id across sessions and a changed one gets
/// a new Id.
/// </summary>
public sealed record PendingChange(string Id, PendingKind Kind, string Source, string Label, CreatureMove? Move, GraveyardEdit? Graveyard, RowEdit? Row, WaypointEdit? Waypoint = null, GameObjectMove? GameObject = null);

/// <summary>
/// Lists the net draft changes as individual pending changes and remembers which were exported.
/// An exported change is no longer "unsaved": it stays applied in the viewer but leaves the list
/// until it changes again. The exported set is a plain list of fingerprints and holds no row data.
/// </summary>
public sealed class PendingChanges
{
    static readonly DateTime FixedStamp = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    readonly string path;
    readonly HashSet<string> exported = [];

    public PendingChanges(string path)
    {
        this.path = path;
        try
        {
            if (File.Exists(path))
            {
                foreach (string id in System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [])
                {
                    exported.Add(id);
                }
            }
        }
        catch (Exception)
        {
            // An unreadable record only means exported changes show as pending again.
            exported.Clear();
        }
    }

    /// <summary>
    /// Every net change not yet exported, in creature, GameObject, graveyard, row, waypoint order.
    /// Each change is fingerprinted with its source's schema (AzerothCore when none is given).
    /// </summary>
    public List<PendingChange> Collect(IEnumerable<CreatureMove> moves, IEnumerable<GraveyardEdit> graveyards, IEnumerable<(string Source, RowEdit Edit)> rows, IEnumerable<WaypointEdit>? waypoints = null, IEnumerable<GameObjectMove>? gameObjects = null, Func<string, IWorldSchema>? schemaFor = null)
    {
        IWorldSchema? Schema(string source) => schemaFor?.Invoke(source);
        var all = new List<PendingChange>();
        foreach (var move in moves.OrderBy(m => m.Guid))
        {
            var scripts = SqlExport.Build(move.Source, [move], [], [], FixedStamp, null, Schema(move.Source));
            all.Add(new(Fingerprint(move.Source, scripts), PendingKind.Creature, move.Source, string.Join("; ", scripts.Summary), move, null, null));
        }

        foreach (var move in (gameObjects ?? []).OrderBy(m => m.Guid))
        {
            var scripts = SqlExport.Build(move.Source, [], [], [], FixedStamp, null, Schema(move.Source), [move]);
            all.Add(new(Fingerprint(move.Source, scripts), PendingKind.GameObject, move.Source, string.Join("; ", scripts.Summary), null, null, null, null, move));
        }

        foreach (var edit in graveyards.OrderBy(e => e.Id))
        {
            var scripts = SqlExport.Build(edit.Source, [], [edit], [], FixedStamp, null, Schema(edit.Source));
            all.Add(new(Fingerprint(edit.Source, scripts), PendingKind.Graveyard, edit.Source, string.Join("; ", scripts.Summary), null, edit, null));
        }

        foreach (var (source, edit) in rows)
        {
            var scripts = SqlExport.Build(source, [], [], [edit], FixedStamp, null, Schema(source));
            all.Add(new(Fingerprint(source, scripts), PendingKind.Row, source, string.Join("; ", scripts.Summary), null, null, edit));
        }

        foreach (var edit in (waypoints ?? []).OrderBy(e => e.Path).ThenBy(e => e.Point))
        {
            var scripts = SqlExport.Build(edit.Source, [], [], [], FixedStamp, [edit], Schema(edit.Source));
            all.Add(new(Fingerprint(edit.Source, scripts), PendingKind.Waypoint, edit.Source, string.Join("; ", scripts.Summary), null, null, null, edit));
        }

        return all.Where(c => !exported.Contains(c.Id)).ToList();
    }

    static string Fingerprint(string source, SqlScripts scripts) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source + "\n" + scripts.Apply)));

    /// <summary>
    /// Forget which changes were exported and delete the saved list.
    /// </summary>
    public void ClearExported()
    {
        exported.Clear();
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // A list that cannot be deleted only means old fingerprints come back next session.
        }
    }

    /// <summary>
    /// Record these changes as exported and save the list.
    /// </summary>
    public void MarkExported(IEnumerable<PendingChange> changes)
    {
        foreach (var change in changes)
        {
            exported.Add(change.Id);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(exported.Order(StringComparer.Ordinal).ToArray()), new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // Not persisting only means the exported changes reappear next session.
        }
    }
}
