using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using Godot;

namespace ACWorldEdit;

/// <summary>
/// What each visible creature plays in the viewport: the stance and emote from its own addon row,
/// else its template's addon row (both with the draft applied). Rows load in the background, so a
/// marker with nothing loaded yet keeps the idle animation until its rows arrive.
/// </summary>
public partial class CreatureOverlay
{
    InteractionCatalog? emoteCatalog;
    IClientFiles? emoteCatalogStore;
    IClientLookup? emoteLookup;
    Dictionary<uint, string> emoteLookupNames = [];

    /// <summary>
    /// Stance, emote, mount and flight per marker with a loaded addon row, for the
    /// model batches. Rebuilt at most twice a second (or when the markers, rows or draft change):
    /// every lookup used to clone the whole draft snapshot, so doing it per marker per frame ate
    /// the frame rate in crowded cities. Ground probes run only for mounted markers; an unknown
    /// height keeps the previous flight state instead of guessing.
    /// </summary>
    Dictionary<ulong, (uint Stand, uint Emote, uint Mount, float MountScale, bool Flying)> AppearanceStates()
    {
        if (!appearanceDirty && System.Diagnostics.Stopwatch.GetElapsedTime(appearanceCacheAt).TotalSeconds <= AppearanceCacheSeconds)
        {
            return appearanceCache;
        }

        var states = ReadAppearanceStates();
        appearanceCache = states;
        appearanceCacheAt = System.Diagnostics.Stopwatch.GetTimestamp();
        appearanceDirty = false;
        return states;
    }

    const double AppearanceCacheSeconds = 0.5;
    /// <summary>More than a step-up above probed ground counts as airborne.</summary>
    const float FlyingThresholdYards = 4.0f;
    /// <summary>Re-probe flight when the marker moved more than this (squared compare).</summary>
    const float ReprobeMovedSquared = 0.25f;
    Dictionary<ulong, (uint Stand, uint Emote, uint Mount, float MountScale, bool Flying)> appearanceCache = [];
    long appearanceCacheAt;
    bool appearanceDirty = true;
    /// <summary>Flight state per mounted marker and where it was probed; static spawns probe once ever.</summary>
    readonly Dictionary<ulong, (bool Flying, Vector3 At)> flyingCache = [];
    /// <summary>Set when the region result changes, so the per-frame step fetches template addons once for the new entries instead of every frame.</summary>
    bool templatePrefetchDirty = true;

    Dictionary<ulong, (uint Stand, uint Emote, uint Mount, float MountScale, bool Flying)> ReadAppearanceStates()
    {
        if (PerfProbe)
        {
            perfAppearanceRuns++;
        }

        var states = new Dictionary<ulong, (uint, uint, uint, float, bool)>();
        if (poses == null || sourceKey.Length == 0)
        {
            return states;
        }

        foreach (ulong guid in flyingCache.Keys.Where(g => !markers.ContainsKey(g)).ToArray())
        {
            flyingCache.Remove(guid);
        }

        var dialect = ContentDraftFiles.Sources.DialectOf(sourceKey);
        var ownAddons = poses.AddonsOf(sourceKey, markers.Keys);
        var templateRows = templateAddons?.RowsOf(sourceKey, markers.Values.Select(m => m.Spawn.TemplateId))
            ?? new Dictionary<uint, Row?>();
        foreach (var marker in markers.Values)
        {
            Row? addon = ownAddons.GetValueOrDefault(marker.Spawn.Guid);
            try
            {
                addon ??= templateAddons?.IsLoaded(sourceKey, marker.Spawn.TemplateId) == true
                    ? templateRows.GetValueOrDefault(marker.Spawn.TemplateId)
                    : null;
            }
            catch (Exception)
            {
                continue;
            }

            if (addon == null)
            {
                continue;
            }

            try
            {
                var (display, creature) = SpawnAppearance.MountOf(dialect, addon);
                uint mount = display;
                float scale = 1;
                if (mount == 0 && creature != 0)
                {
                    if (looks.TryGetValue(creature, out var look) && look.DisplayId != 0)
                    {
                        mount = look.DisplayId;
                        scale = look.Scale;
                    }
                    else
                    {
                        FetchLooks([creature]);
                    }
                }

                bool flying = flyingCache.GetValueOrDefault(marker.Spawn.Guid).Flying;
                if (mount != 0 && (!flyingCache.TryGetValue(marker.Spawn.Guid, out var probed) || probed.At.DistanceSquaredTo(marker.Node.Position) > ReprobeMovedSquared))
                {
                    flying = ProbeFlying(marker.Node.Position, flying);
                    flyingCache[marker.Spawn.Guid] = (flying, marker.Node.Position);
                }

                states[marker.Spawn.Guid] = (SpawnAppearance.StandStateOf(dialect, addon), SpawnAppearance.EmoteOf(addon), mount, scale, flying);
            }
            catch (Exception)
            {
            }
        }

        return states;
    }

    /// <summary>
    /// Whether a mounted marker is airborne: clearly above the cheap terrain height, confirmed by
    /// the full probe (terrain plus building floors) so perched mounts stay grounded. Unknown
    /// ground keeps the previous answer instead of guessing. Probes run once per resting place;
    /// the per-triangle WMO walk is what used to eat the frame in crowded cities.
    /// </summary>
    bool ProbeFlying(Vector3 pos, bool previous)
    {
        if (TerrainHeightAt?.Invoke(pos.X, pos.Z) is float terrain && pos.Y - terrain <= FlyingThresholdYards)
        {
            return false;
        }

        GroundHit? hit = null;
        if (GroundAt == null)
        {
            return previous;
        }

        if (PerfProbe)
        {
            long mark = PerfMark();
            hit = GroundAt.Invoke(pos);
            perfGroundCalls++;
            perfGroundMs += System.Diagnostics.Stopwatch.GetElapsedTime(mark, PerfMark()).TotalMilliseconds;
        }
        else
        {
            hit = GroundAt.Invoke(pos);
        }

        return hit is { Height: float ground } ? pos.Y - ground > FlyingThresholdYards : previous;
    }

    /// <summary>The emote's client name for animation matching: Wrath DBC names, else retail DB2 slash commands.</summary>
    string? AppearanceEmoteName(uint emote)
    {
        if (emote == 0)
        {
            return null;
        }

        var store = Store();
        if (!ReferenceEquals(store, emoteCatalogStore))
        {
            emoteCatalogStore = store;
            emoteCatalog = null;
            LoadEmoteCatalog(store);
        }

        if (emoteCatalog?.EmoteName(emote) is { } wrath)
        {
            return wrath;
        }

        var lookup = Lookup();
        if (!ReferenceEquals(lookup, emoteLookup))
        {
            emoteLookup = lookup;
            emoteLookupNames = lookup?.Emotes().Where(e => e.Name.Length > 0).DistinctBy(e => e.Id)
                .ToDictionary(e => e.Id, e => e.Name) ?? [];
        }

        return emoteLookupNames.GetValueOrDefault(emote);
    }

    async void LoadEmoteCatalog(IClientFiles? store)
    {
        var catalog = await ContentEditorWindow.LoadCatalog(store);
        if (ReferenceEquals(store, emoteCatalogStore))
        {
            emoteCatalog = catalog;
        }
    }
}
