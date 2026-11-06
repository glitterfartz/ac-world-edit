using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The native models of nearby creature markers: appearance caching, variant/clip batches and the
/// label height above each scaled model. Selection can preview one animation without changing the
/// other creatures. The batches are rebuilt only when their membership changes, so flying does not
/// flicker the capsule fallbacks back in for the same visible set.
/// </summary>
internal sealed class CreatureModelView(Node3D owner)
{
    readonly HashSet<ulong> admitted = [];
    readonly Dictionary<uint, CreatureAppearance?> appearances = [];
    readonly Dictionary<ModelKey, (ModelInstance Node, ulong[] Guids)> batches = [];
    /// <summary>Animation clips the client's models actually have, by display: seat and flight
    /// animations are only picked when the model lists them, never blind by name.</summary>
    readonly Dictionary<uint, ushort[]> displayAnims = [];
    long updated;

    public IReadOnlyDictionary<ModelKey, (ModelInstance Node, ulong[] Guids)> Batches => batches;
    public int ReadyCount => batches.Values.Where(b => b.Node.ModelReady).Sum(b => b.Guids.Length);
    public int AdmittedCount => admitted.Count;

    /// <summary>Whether this marker's model was a candidate in the last pass (it is inside the preview range).</summary>
    public bool IsAdmitted(ulong guid) => admitted.Contains(guid);

    public void Forget(ulong guid) => admitted.Remove(guid);

    public void ResetTimer() => updated = 0;

    /// <summary>Free every batch and cached appearance; the markers themselves are owned elsewhere.</summary>
    public void Clear()
    {
        batches.FreeAll(b => b.Node);
        appearances.Clear();
        displayAnims.Clear();
        elevations.Clear();
        admitted.Clear();
        updated = 0;
    }

    /// <summary>Refresh the batches at most every 0.2 s; membership changes rebuild only that batch.</summary>
    public void Update(Camera3D camera, IEnumerable<CreatureMarkers.Marker> markers, SpawnPreview preview, bool layerEnabled, ulong? selected, ushort previewAnimation, ModelLibrary? library,
        IReadOnlyDictionary<ulong, (uint Stand, uint Emote, uint Mount, float MountScale, bool Flying)>? states = null, Func<uint, string?>? emoteName = null)
    {
        if (library?.Catalog == null || System.Diagnostics.Stopwatch.GetElapsedTime(updated).TotalSeconds <= 0.2)
        {
            return;
        }

        var byGuid = markers.ToDictionary(m => m.Spawn.Guid);
        var groups = GroupModels(camera, byGuid, preview, layerEnabled, selected, previewAnimation, library, states, emoteName);
        Reconcile(groups, byGuid, library);
        NoteAvailableAnimations(byGuid, states);
        updated = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>Remember which clips the built models list, so seat and flight animations are only
    /// picked for models that have them (a missing clip would show the reference pose).</summary>
    void NoteAvailableAnimations(Dictionary<ulong, CreatureMarkers.Marker> markers, IReadOnlyDictionary<ulong, (uint Stand, uint Emote, uint Mount, float MountScale, bool Flying)>? states)
    {
        foreach (var batch in batches)
        {
            if (!batch.Value.Node.ModelReady)
            {
                continue;
            }

            foreach (ulong guid in batch.Value.Guids)
            {
                if (!markers.TryGetValue(guid, out var marker))
                {
                    continue;
                }

                uint display = batch.Key.Extra == "mount" && states?.TryGetValue(guid, out var state) == true && state.Mount != 0
                    ? state.Mount
                    : marker.Spawn.DisplayId;
                if (display != 0)
                {
                    displayAnims[display] = batch.Value.Node.AvailableAnimations;
                }
            }
        }
    }

    /// <summary>
    /// Cache appearances and group the admitted creatures by variant/clip. Selection can preview a
    /// separate animation without changing the other creatures; every other marker plays the looping
    /// animation its addon row (stance and emote) resolves to, riding its mount when the row sets one.
    /// </summary>
    Dictionary<ModelKey, List<ulong>> GroupModels(Camera3D camera, Dictionary<ulong, CreatureMarkers.Marker> markers, SpawnPreview preview, bool layerEnabled, ulong? selected, ushort previewAnimation, ModelLibrary library,
        IReadOnlyDictionary<ulong, (uint Stand, uint Emote, uint Mount, float MountScale, bool Flying)>? states, Func<uint, string?>? emoteName)
    {
        var groups = new Dictionary<ModelKey, List<ulong>>();
        elevations.Clear();
        admitted.Clear();
        float viewDistanceSquared = preview.ViewDistance * preview.ViewDistance;
        var candidates = markers.Values
            .Select(m => (Marker: m, Distance: camera.GlobalPosition.DistanceSquaredTo(m.Node.GlobalPosition)))
            .Where(c => selected == c.Marker.Spawn.Guid || c.Distance <= viewDistanceSquared)
            .OrderBy(c => selected == c.Marker.Spawn.Guid ? 0 : 1).ThenBy(c => c.Distance)
            .Take(preview.ModelLimit)
            .Select(c => c.Marker);
        foreach (var marker in candidates)
        {
            admitted.Add(marker.Spawn.Guid);
            if (!layerEnabled)
            {
                continue;
            }

            if (!appearances.TryGetValue(marker.Spawn.DisplayId, out var appearance))
            {
                try
                {
                    appearance = library.Catalog.Resolve(marker.Spawn.DisplayId);
                }
                catch (Exception)
                {
                    appearance = null;
                    GD.PushWarning($"No client appearance for creature display {marker.Spawn.DisplayId}; using labeled marker.");
                }

                appearances[marker.Spawn.DisplayId] = appearance;
            }

            if (appearance == null)
            {
                continue;
            }

            var key = ModelKey.ForCreature(appearance, selected == marker.Spawn.Guid ? previewAnimation : RiderAnimation(marker, states, emoteName, library));
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
            }

            list.Add(marker.Spawn.Guid);
            AddMount(groups, marker, states, library);
        }

        return groups;
    }

    /// <summary>A marker's mount as its own batch (tagged, so it never merges with a rider batch):
    /// the rider's elevation is remembered for its own transform, label and gizmo moves. An airborne
    /// mount flaps when its model has a flight clip, otherwise it stands.</summary>
    void AddMount(Dictionary<ModelKey, List<ulong>> groups, CreatureMarkers.Marker marker, IReadOnlyDictionary<ulong, (uint Stand, uint Emote, uint Mount, float MountScale, bool Flying)>? states, ModelLibrary library)
    {
        if (states?.TryGetValue(marker.Spawn.Guid, out var state) != true || state.Mount == 0)
        {
            return;
        }

        if (!appearances.TryGetValue(state.Mount, out var mount))
        {
            var catalog = library.Catalog;
            if (catalog == null)
            {
                return;
            }

            try
            {
                mount = catalog.Resolve(state.Mount);
            }
            catch (Exception)
            {
                mount = null;
                GD.PushWarning($"No client appearance for mount display {state.Mount}; riding without a mount model.");
            }

            appearances[state.Mount] = mount;
        }

        if (mount == null)
        {
            return;
        }

        ushort anim = 0;
        if (state.Flying && displayAnims.TryGetValue(state.Mount, out var clips) &&
            SpawnAppearance.FlyAnimation(n => AnimByName(library, n), id => clips.Contains(id)) is ushort fly)
        {
            anim = fly;
        }

        var key = ModelKey.ForCreature(mount, anim) with { Extra = "mount" };
        if (!groups.TryGetValue(key, out var list))
        {
            groups[key] = list = [];
        }

        list.Add(marker.Spawn.Guid);
        float scale = state.MountScale * mount.Scale;
        elevations[marker.Spawn.Guid] = 1.5f * (float.IsFinite(scale) && scale > 0 ? scale : 1);
    }

    readonly Dictionary<ulong, float> elevations = [];

    /// <summary>
    /// The looping animation a marker plays: dead always plays Death, else the emote's animation,
    /// else the seat animation when mounted and the rider's model has one, else the stance's, else
    /// Stand. Unknown names fall back instead of failing, and a clip the model lacks is never
    /// picked: the loader would show the reference pose.
    /// </summary>
    ushort RiderAnimation(CreatureMarkers.Marker marker, IReadOnlyDictionary<ulong, (uint Stand, uint Emote, uint Mount, float MountScale, bool Flying)>? states, Func<uint, string?>? emoteName, ModelLibrary library)
    {
        if (states?.TryGetValue(marker.Spawn.Guid, out var state) != true)
        {
            return 0;
        }

        if (state.Stand == 7)
        {
            return SpawnAppearance.AnimDeath;
        }

        if (state.Emote != 0 && emoteName?.Invoke(state.Emote) is { } name && AnimByName(library, name) is ushort fromEmote)
        {
            return fromEmote;
        }

        if (state.Mount != 0 && displayAnims.TryGetValue(marker.Spawn.DisplayId, out var clips) &&
            SpawnAppearance.SeatAnimation(n => AnimByName(library, n), id => clips.Contains(id)) is ushort seat)
        {
            return seat;
        }

        if (state.Stand != 0 && SpawnAppearance.StandAnimation(state.Stand) is { } stance && AnimByName(library, stance) is ushort fromStance)
        {
            return fromStance;
        }

        return 0;
    }

    static object? animCatalog;
    static Dictionary<string, ushort> animNames = new(StringComparer.Ordinal);

    /// <summary>Client AnimationData names (normalized) to IDs, rebuilt when the client changes.</summary>
    static ushort? AnimByName(ModelLibrary library, string name)
    {
        if (!ReferenceEquals(animCatalog, library.Catalog))
        {
            animCatalog = library.Catalog;
            animNames = new Dictionary<string, ushort>(StringComparer.Ordinal);
            if (library.Catalog?.AnimationNames is { } names)
            {
                foreach (var pair in names)
                {
                    animNames[Normalize(pair.Value)] = pair.Key;
                }
            }
        }

        return animNames.TryGetValue(Normalize(name), out ushort id) ? id : null;
    }

    static string Normalize(string name)
    {
        var letters = name.Where(char.IsLetterOrDigit).ToArray();
        return new string(letters).ToLowerInvariant();
    }

    /// <summary>Diff the grouped membership against the current batches; unchanged batches keep their nodes.</summary>
    void Reconcile(Dictionary<ModelKey, List<ulong>> groups, Dictionary<ulong, CreatureMarkers.Marker> markers, ModelLibrary library)
    {
        foreach (var batch in batches.ToArray())
        {
            // Compare membership, not order: distance-sorted candidates reorder every tick while
            // flying even when the visible set is unchanged, and rebuilding the batch on pure
            // reordering is what flickers the fallback marker back in for no reason.
            if (groups.TryGetValue(batch.Key, out var guids) && guids.Count == batch.Value.Guids.Length && guids.ToHashSet().SetEquals(batch.Value.Guids))
            {
                continue;
            }

            foreach (ulong guid in batch.Value.Guids)
            {
                if (markers.TryGetValue(guid, out var marker))
                {
                    marker.Body.Visible = true;
                    marker.Label.Position = new(0, CreatureMarkers.FallbackLabelHeight, 0);
                }
            }

            batches.Free(batch.Key, b => b.Node);
        }

        foreach (var group in groups)
        {
            if (!batches.ContainsKey(group.Key))
            {
                bool mount = group.Key.Extra == "mount";
                batches[group.Key] = (library.Attach(owner, group.Key, group.Value.Select(guid => ModelTransform(markers[guid], mount ? 0 : elevations.GetValueOrDefault(guid))).ToArray()), group.Value.ToArray());
            }
        }
    }

    /// <summary>Update one creature's transform across its batches (rider and mount), for a gizmo preview or commit.</summary>
    public void Place(ulong guid, CreatureMarkers.Marker marker)
    {
        foreach (var batch in batches)
        {
            int index = Array.IndexOf(batch.Value.Guids, guid);
            if (index >= 0)
            {
                batch.Value.Node.SetPlacement(index, ModelTransform(marker, batch.Key.Extra == "mount" ? 0 : elevations.GetValueOrDefault(guid)));
            }
        }
    }

    /// <summary>Place the name label just above the scaled native model (plus its mount's height).</summary>
    public float LabelHeight(CreatureMarkers.Marker marker, Aabb bounds)
    {
        float scale = DisplayScale(marker.Spawn);
        float top = bounds.End.Y * scale;
        float height = float.IsFinite(top) && top > 0 ? top + 0.6f : CreatureMarkers.FallbackLabelHeight;
        return height + elevations.GetValueOrDefault(marker.Spawn.Guid);
    }

    /// <summary>The template/display scale a creature is drawn at.</summary>
    public float DisplayScale(CreatureSpawn spawn) =>
        spawn.DisplayScale * (appearances.GetValueOrDefault(spawn.DisplayId)?.Scale ?? 1);

    /// <summary>Compose creature heading, effective draft position, template/display scale and mount elevation.</summary>
    Transform3D ModelTransform(CreatureMarkers.Marker marker, float elevation = 0) =>
        new(marker.Node.Basis.Scaled(Vector3.One * DisplayScale(marker.Spawn)), marker.Node.Position + new Vector3(0, elevation, 0));
}
