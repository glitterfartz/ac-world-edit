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
