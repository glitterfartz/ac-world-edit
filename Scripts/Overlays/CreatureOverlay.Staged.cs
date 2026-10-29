using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Spawns that exist only in the draft. The region read returns what the database holds, so the
/// overlay merges in the spawns the draft places or duplicates and leaves out the ones it deletes;
/// undo, redo and a discard in Pending Changes all arrive as a draft change and redraw the same way.
/// </summary>
public partial class CreatureOverlay
{
    /// <summary>The last region read, before the draft is applied.</summary>
    IReadOnlyList<CreatureSpawn> databaseSpawns = [];
    readonly Dictionary<uint, CreatureLook> looks = [];
    readonly HashSet<uint> lookRequests = [];

    /// <summary>The spawn the draft places, or the database spawn when it is not deleted, for every spawn on screen.</summary>
    IReadOnlyList<CreatureSpawn> WithDraft()
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
                spawns.Add(SpawnPoses.CreatureOf(row, look, dialect));
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

    ulong? selectWhenDrawn;

    /// <summary>
    /// Place a new spawn of a creature template. The template's name, model, default equipment set and
    /// movement type, and the next free GUID, come from the database; the spawn is then one undoable
    /// draft command and is selected as soon as it is drawn. Returns the new GUID, or null (with a
    /// status message) when nothing was placed.
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
            var read = await source.ReadCreatureLooksAsync([entry], default);
            if (!read.TryGetValue(entry, out var look))
            {
                Say($"Creature template {entry} does not exist.");
                return null;
            }

            ulong max = await source.MaxIdAsync(SpawnEdits.SpawnTable(source.Schema, SpawnKind.Creature), "guid", default);
            ulong guid = poses.NextGuid(key, max);
            var row = SpawnEdits.NewCreature(source.Schema, guid, entry, pose, look.EquipmentId, look.MovementType);
            sourceKey = key;
            RememberLook(entry, look);
            var (ok, message) = poses.Commit(this, key, "Place " + look.Name, SpawnEdits.Place(source.Schema, SpawnKind.Creature, row));
            Say(ok ? $"Placed {look.Name}." : message);
            if (!ok)
            {
                return null;
            }

            selectWhenDrawn = guid;
            return guid;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Say("Could not place the creature: " + error.Message);
            return null;
        }
    }

    /// <summary>The selected creature has its row loaded (or is staged), so it can be copied or deleted.</summary>
    bool RowReady => poses?.Available == true && selectedGuid is ulong guid && (poses.Has(sourceKey, guid) || IsStaged(guid));

    public bool CanDuplicate => RowReady;
    public bool CanDelete => RowReady;

    /// <summary>Delete the selected creature and its addon as one undoable command.</summary>
    public void Delete()
    {
        if (poses == null || !selectedGuid.HasValue || !markers.TryGetValue(selectedGuid.Value, out var marker))
        {
            return;
        }

        gizmo.Finish(false);
        var (ok, message) = poses.Delete(this, ContentDraftFiles.Sources.SchemaFor(sourceKey), sourceKey, selectedGuid.Value);
        Say(ok ? $"Deleted {marker.Spawn.Name}." : message);
    }

    /// <summary>Copy the selected creature two yards ahead of it, on the ground, and select the copy.</summary>
    public async void Duplicate() => await DuplicateAsync();

    public async Task<ulong?> DuplicateAsync()
    {
        if (poses == null || !selectedGuid.HasValue || !markers.TryGetValue(selectedGuid.Value, out var marker))
        {
            return null;
        }

        gizmo.Finish(false);
        var config = Connection();
        var source = DBSource.Open(config);
        string key = sourceKey;
        try
        {
            ulong max = await source.MaxIdAsync(SpawnEdits.SpawnTable(source.Schema, SpawnKind.Creature), "guid", default);
            ulong guid = poses.NextGuid(key, max);
            RememberLook(marker.Spawn.TemplateId, new CreatureLook(marker.Spawn.Name, marker.Spawn.DisplayId, marker.Spawn.DisplayScale));
            var (ok, message) = poses.Duplicate(this, source.Schema, key, marker.Spawn.Guid, guid, CopyPose(PoseOf(marker.Spawn)));
            Say(ok ? $"Duplicated {marker.Spawn.Name}." : message);
            if (!ok)
            {
                return null;
            }

            selectWhenDrawn = guid;
            return guid;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Say("Could not duplicate the creature: " + error.Message);
            return null;
        }
    }

    /// <summary>Two yards along the facing, dropped to the ground when the probe finds one.</summary>
    WorldPose CopyPose(WorldPose pose)
    {
        var ahead = pose with { X = pose.X + 2 * Math.Cos(pose.Heading), Y = pose.Y + 2 * Math.Sin(pose.Heading) };
        return GroundAt?.Invoke(ViewPosition(ahead)).Height is float height ? ahead with { Z = height } : ahead;
    }

    /// <summary>True for a spawn only the draft holds, so there is no database row to read.</summary>
    bool IsStaged(ulong guid) => poses?.Inserted(sourceKey).Any(r => r.UL("guid") == guid) == true;

    /// <summary>Let a placed spawn draw before the database answers (the caller already knows its template).</summary>
    public void RememberLook(uint entry, CreatureLook look) => looks[entry] = look;

    /// <summary>Read the name and model of templates the draft uses, then redraw. A template the database lacks draws as missing.</summary>
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
            var read = await DBSource.Open(config).ReadCreatureLooksAsync(wanted, default);
            foreach (uint entry in wanted)
            {
                looks[entry] = read.GetValueOrDefault(entry, CreatureLook.Missing);
            }
        }
        catch (Exception)
        {
            foreach (uint entry in wanted)
            {
                looks[entry] = CreatureLook.Missing;
            }

            Say("Could not read a creature template; it draws as missing.");
        }

        if (IsInsideTree())
        {
            Resync();
        }
    }
}
