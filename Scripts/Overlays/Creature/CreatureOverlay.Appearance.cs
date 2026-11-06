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

    /// <summary>Stance, emote and resolved mount display/scale per marker with a loaded addon row, for the model batches.</summary>
    Dictionary<ulong, (uint Stand, uint Emote, uint Mount, float MountScale)> AppearanceStates()
    {
        var states = new Dictionary<ulong, (uint, uint, uint, float)>();
        if (poses == null || sourceKey.Length == 0)
        {
            return states;
        }

        var dialect = ContentDraftFiles.Sources.DialectOf(sourceKey);
        foreach (var marker in markers.Values)
        {
            Row? addon = null;
            try
            {
                addon = poses.AddonOf(sourceKey, marker.Spawn.Guid)
                    ?? (templateAddons?.IsLoaded(sourceKey, marker.Spawn.TemplateId) == true
                        ? templateAddons.RowOf(sourceKey, marker.Spawn.TemplateId)
                        : null);
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
