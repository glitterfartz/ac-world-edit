using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// GameObject spawns that exist only in the draft. The region read returns what the database holds,
/// so the overlay merges in the spawns the draft places or duplicates and leaves out the ones it
/// deletes. Undo, redo and a discard in Pending Changes all arrive as a draft change and redraw the
/// same way; the model batches are re-diffed from the merged records on the next tick.
/// </summary>
public partial class GameObjectOverlay
{
    /// <summary>The last region read, before the draft is applied.</summary>
    IReadOnlyList<GameObjectSpawn> databaseSpawns = [];
    readonly Dictionary<uint, GameObjectLook> looks = [];
    readonly HashSet<uint> lookRequests = [];

    /// <summary>The database spawns the draft does not delete, plus the spawns it places on this map.</summary>
    IReadOnlyList<GameObjectSpawn> WithDraft()
    {
        if (poses == null || !poses.Available || sourceKey.Length == 0)
        {
            return databaseSpawns;
        }

        var spawns = databaseSpawns.Where(s => !poses.Deleted(sourceKey, s.Guid)).ToList();
        if (region == null)
        {
            return spawns;
        }

        var dialect = Connection().Dialect;
        var missing = new List<uint>();
        foreach (var row in poses.Inserted(sourceKey))
        {
            if (SpawnEdits.PoseOf(row).Map != region.Map)
            {
                continue;
            }

            uint entry = row.U("id");
            if (looks.TryGetValue(entry, out var look))
            {
                spawns.Add(SpawnPoses.GameObjectOf(row, look, dialect));
            }
            else
            {
                missing.Add(entry);
            }
        }

        if (missing.Count > 0)
        {
            FetchLooks(missing);
        }

        return spawns;
    }

    /// <summary>Rebuild the records and the lookup from the last read and the draft; drop a selection that is gone.</summary>
    void Recompose()
    {
        var spawns = WithDraft();
        var incoming = spawns.ToDictionary(s => s.Guid);
        foreach (ulong guid in lookup.Keys.Where(g => !incoming.ContainsKey(g)).ToArray())
        {
            lookup.Remove(guid);
        }

        records = spawns;
        foreach (var spawn in spawns)
        {
            lookup[spawn.Guid] = spawn;
        }

        if (selected.HasValue && !lookup.ContainsKey(selected.Value))
        {
            Deselect();
        }

        Populate();
    }

    /// <summary>The selected GameObject has its row loaded (or is staged), so it can be copied or deleted.</summary>
    bool RowReady => poses?.Available == true && selected is ulong guid && (poses.Has(sourceKey, guid) || IsStaged(guid));

    public bool CanDuplicate => RowReady;
    public bool CanDelete => RowReady;

    /// <summary>Delete the selected GameObject and its addon as one undoable command.</summary>
    public void Delete()
    {
        if (poses == null || !selected.HasValue || !lookup.TryGetValue(selected.Value, out var spawn))
        {
            return;
        }

        gizmo.Finish(false);
        var (ok, message) = poses.Delete(this, ContentDraftFiles.Sources.SchemaFor(sourceKey), sourceKey, spawn.Guid);
        Say(ok ? $"Deleted {spawn.Name}." : message);
    }

    /// <summary>Copy the selected GameObject two yards ahead of it, on the ground, and select the copy.</summary>
    public async void Duplicate() => await DuplicateAsync();

    public async Task<ulong?> DuplicateAsync()
    {
        if (poses == null || !selected.HasValue || !lookup.TryGetValue(selected.Value, out var source))
        {
            return null;
        }

        gizmo.Finish(false);
        var config = Connection();
        var database = DBSource.Open(config);
        string key = sourceKey;
        try
        {
            ulong max = await database.MaxIdAsync(SpawnEdits.SpawnTable(database.Schema, SpawnKind.GameObject), "guid", default);
            ulong guid = poses.NextGuid(key, max);
            RememberLook(source.TemplateId, new GameObjectLook(source.Name, source.DisplayId, source.Scale, source.Type));
            var (ok, message) = poses.Duplicate(this, database.Schema, key, source.Guid, guid, CopyPose(Effective(source).Pose));
            Say(ok ? $"Duplicated {source.Name}." : message);
            if (!ok)
            {
                return null;
            }

            Select(guid);
            return guid;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Say("Could not duplicate the GameObject: " + error.Message);
            return null;
        }
    }

    /// <summary>Two yards along the facing, dropped to the ground when the probe finds one.</summary>
    WorldPose CopyPose(WorldPose pose)
    {
        var ahead = pose with { X = pose.X + 2 * Math.Cos(pose.Heading), Y = pose.Y + 2 * Math.Sin(pose.Heading) };
        return GroundAt?.Invoke(ViewSpace.ToView(ahead.X, ahead.Y, ahead.Z)).Height is float height ? ahead with { Z = height } : ahead;
    }

    /// <summary>True for a spawn only the draft holds, so there is no database row to read.</summary>
    bool IsStaged(ulong guid) => poses?.Inserted(sourceKey).Any(r => r.UL("guid") == guid) == true;

    /// <summary>Let a placed spawn draw before the database answers (the caller already knows its template).</summary>
    public void RememberLook(uint entry, GameObjectLook look) => looks[entry] = look;

    /// <summary>
    /// Place a new spawn of a GameObject template. The template's look and the next free GUID come
    /// from the database; the spawn is one undoable draft command and is selected at once. Returns
    /// the new GUID, or null (with a status message) when nothing was placed.
    /// </summary>
    public async Task<ulong?> PlaceAsync(uint entry, WorldPose pose)
    {
        if (poses == null || !poses.Available)
        {
            Say("Editing is disabled.");
            return null;
        }

        var config = Connection();
        var source = DBSource.Open(config);
        string key = source.SourceKey;
        ContentDraftFiles.Sources.Remember(key, config.Dialect);
        try
        {
            var read = await source.ReadGameObjectLooksAsync([entry], default);
            if (!read.TryGetValue(entry, out var look))
            {
                Say($"GameObject template {entry} does not exist.");
                return null;
            }

            ulong max = await source.MaxIdAsync(SpawnEdits.SpawnTable(source.Schema, SpawnKind.GameObject), "guid", default);
            ulong guid = poses.NextGuid(key, max);
            var row = SpawnEdits.NewGameObject(source.Schema, guid, entry, pose);
            sourceKey = key;
            RememberLook(entry, look);
            var (ok, message) = poses.Commit(this, key, "Place " + look.Name, SpawnEdits.Place(source.Schema, SpawnKind.GameObject, row));
            Say(ok ? $"Placed {look.Name}." : message);
            if (!ok)
            {
                return null;
            }

            Select(guid);
            return guid;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Say("Could not place the GameObject: " + error.Message);
            return null;
        }
    }

    /// <summary>Read the look of templates the draft uses, then redraw. A template the database lacks draws as missing.</summary>
    async void FetchLooks(IEnumerable<uint> entries)
    {
        var wanted = entries.Where(lookRequests.Add).ToArray();
        if (wanted.Length == 0)
        {
            return;
        }

        var config = Connection();
        try
        {
            var read = await DBSource.Open(config).ReadGameObjectLooksAsync(wanted, default);
            foreach (uint entry in wanted)
            {
                looks[entry] = read.GetValueOrDefault(entry, GameObjectLook.Missing);
            }
        }
        catch (Exception)
        {
            foreach (uint entry in wanted)
            {
                looks[entry] = GameObjectLook.Missing;
            }

            Say("Could not read a GameObject template; it draws as missing.");
        }

        if (IsInsideTree())
        {
            Resync();
        }
    }
}
