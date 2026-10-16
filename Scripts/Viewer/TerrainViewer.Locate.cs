using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Locate a creature by template entry from editor links: select a loaded spawn when there is one;
/// otherwise read its spawns (bounded, read-only), prefer the current map and the spawn nearest the
/// camera, open that map when needed, move there and select the spawn once the streamed creature
/// region contains it. Selection can fail when SQL visibility (phase, event, pool) hides the spawn;
/// the camera still arrives and the status says why.
/// </summary>
public partial class TerrainViewer
{
    /// <summary>
    /// Spawn to select when it appears in the creature region, with its name and a deadline.
    /// </summary>
    (uint Guid, string Name, double Remaining)? pendingLocate;
    int locateGeneration;

    /// <summary>
    /// Returns a user-facing status line (empty when the creature was selected immediately).
    /// </summary>
    async Task<string> LocateCreature(uint entry, string name)
    {
        if (creatures.FlyToEntry(entry, camera.Position))
        {
            pendingLocate = null;
            return "";
        }

        int current = ++locateGeneration;
        IReadOnlyList<(uint Guid, uint Map, double X, double Y, double Z)> spawns;
        try
        {
            var source = new ContentSource(connection.Settings);
            spawns = await Task.Run(() => source.FindSpawnsAsync(entry, CancellationToken.None));
        }
        catch (Exception error)
        {
            return CreatureSource.SafeError(error);
        }

        if (current != locateGeneration)
        {
            return "";
        }

        var available = spawns.Where(s => mapList.Any(m => m.Id == s.Map)).ToList();
        if (available.Count == 0)
        {
            return spawns.Count == 0 ? $"{name} has no spawns in the database." : $"{name} only spawns on maps this client does not have.";
        }

        var here = WorldCoordinates.ToServer(new(camera.Position.X, camera.Position.Y, camera.Position.Z));
        var target = available
            .OrderBy(s => s.Map == currentMapId ? 0 : 1)
            .ThenBy(s => s.Map == currentMapId ? Math.Pow(s.X - here.X, 2) + Math.Pow(s.Y - here.Y, 2) : 0)
            .ThenBy(s => s.Guid)
            .First();
        if (target.Map != currentMapId)
        {
            if (busy)
            {
                return "The viewer is busy loading; try again in a moment.";
            }

            // Open the map at the spawn's tile; OpenMap falls back to the nearest available tile.
            var view = WorldCoordinates.ToView(new(target.X, target.Y, target.Z));
            maps.Select(mapList.FindIndex(m => m.Id == target.Map));
            tileX.Value = Math.Clamp((int)Math.Floor(view.X / TerrainTile.Size + 32), 0, 63);
            tileY.Value = Math.Clamp((int)Math.Floor(view.Z / TerrainTile.Size + 32), 0, 63);
            await OpenMap();
            if (current != locateGeneration || currentMapId != target.Map)
            {
                return current != locateGeneration ? "" : $"Could not open map {target.Map}.";
            }
        }

        FocusPose(new WorldPose((ushort)target.Map, target.X, target.Y, target.Z, 0));
        pendingLocate = (target.Guid, name, 30);
        string where = mapList.First(m => m.Id == target.Map).Name;
        string others = available.Count > 1 ? $" (1 of {available.Count} spawns)" : "";
        return $"Travelling to {name} in {where}{others}; it is selected when its area loads.";
    }

    /// <summary>
    /// Select the located spawn once streaming has loaded it; give up quietly after the deadline.
    /// </summary>
    void UpdatePendingLocate(double delta)
    {
        if (pendingLocate is not { } pending || busy || creatures.IsLoading)
        {
            return;
        }

        if (creatures.Records.Any(s => s.Guid == pending.Guid))
        {
            pendingLocate = null;
            creatures.Select(pending.Guid);
            creatures.FlyToSelection();
            return;
        }

        pendingLocate = pending.Remaining - delta > 0 ? pending with { Remaining = pending.Remaining - delta } : null;
        if (pendingLocate == null)
        {
            message = $"{pending.Name} (spawn #{pending.Guid}) is hidden by the current visibility filters (phase, event or pool); the camera is at its position.";
        }
    }
}
