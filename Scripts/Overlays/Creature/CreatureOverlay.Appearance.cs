using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

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
    /// Stance, emote and resolved mount display/scale per marker with a loaded addon row, for the
    /// model batches. Rebuilt at most twice a second (or when the markers, rows or draft change):
    /// every lookup used to clone the whole draft snapshot, so doing it per marker per frame ate
    /// the frame rate in crowded cities.
    /// </summary>
    Dictionary<ulong, (uint Stand, uint Emote, uint Mount, float MountScale)> AppearanceStates()
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
    Dictionary<ulong, (uint Stand, uint Emote, uint Mount, float MountScale)> appearanceCache = [];
    long appearanceCacheAt;
    bool appearanceDirty = true;
    /// <summary>Set when the region result changes, so the per-frame step fetches template addons once for the new entries instead of every frame.</summary>
    bool templatePrefetchDirty = true;

    Dictionary<ulong, (uint Stand, uint Emote, uint Mount, float MountScale)> ReadAppearanceStates()
    {
        var states = new Dictionary<ulong, (uint, uint, uint, float)>();
        if (poses == null || sourceKey.Length == 0)
        {
            return states;
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

                states[marker.Spawn.Guid] = (SpawnAppearance.StandStateOf(dialect, addon), SpawnAppearance.EmoteOf(addon), mount, scale);
            }
            catch (Exception)
            {
            }
        }

        return states;
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
