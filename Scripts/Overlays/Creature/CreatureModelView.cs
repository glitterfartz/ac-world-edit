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
        admitted.Clear();
        updated = 0;
    }

    /// <summary>
    /// Refresh the batches at most every 0.2 s: cache appearances, admit the nearest models and diff
    /// the variant/clip groups rather than rebuilding them. <paramref name="previewAnimation"/>
    /// applies to the selected creature only.
    /// </summary>
    public void Update(Camera3D camera, IEnumerable<CreatureMarkers.Marker> markers, SpawnPreview preview, bool layerEnabled, ulong? selected, ushort previewAnimation, ModelLibrary? library)
    {
        if (library?.Catalog == null || System.Diagnostics.Stopwatch.GetElapsedTime(updated).TotalSeconds <= 0.2)
        {
            return;
        }

        var byGuid = markers.ToDictionary(m => m.Spawn.Guid);
        var groups = new Dictionary<ModelKey, List<ulong>>();
        admitted.Clear();
        float viewDistanceSquared = preview.ViewDistance * preview.ViewDistance;
        var candidates = byGuid.Values
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

            var key = ModelKey.ForCreature(appearance, selected == marker.Spawn.Guid ? previewAnimation : (ushort)0);
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
            }

            list.Add(marker.Spawn.Guid);
        }

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
                if (byGuid.TryGetValue(guid, out var marker))
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
                batches[group.Key] = (library.Attach(owner, group.Key, group.Value.Select(guid => ModelTransform(byGuid[guid])).ToArray()), group.Value.ToArray());
            }
        }

        updated = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>Update one creature's transform across its batch, for a gizmo preview or commit.</summary>
    public void Place(ulong guid, CreatureMarkers.Marker marker)
    {
        foreach (var batch in batches.Values)
        {
            int index = Array.IndexOf(batch.Guids, guid);
            if (index >= 0)
            {
                batch.Node.SetPlacement(index, ModelTransform(marker));
            }
        }
    }

    /// <summary>Place the name label just above the scaled native model's reference-pose top.</summary>
    public float LabelHeight(CreatureMarkers.Marker marker, Aabb bounds)
    {
        float scale = DisplayScale(marker.Spawn);
        float top = bounds.End.Y * scale;
        return float.IsFinite(top) && top > 0 ? top + 0.6f : CreatureMarkers.FallbackLabelHeight;
    }

    /// <summary>The template/display scale a creature is drawn at.</summary>
    public float DisplayScale(CreatureSpawn spawn) =>
        spawn.DisplayScale * (appearances.GetValueOrDefault(spawn.DisplayId)?.Scale ?? 1);

    /// <summary>Compose creature heading, effective draft position and template/display scale.</summary>
    Transform3D ModelTransform(CreatureMarkers.Marker marker) =>
        new(marker.Node.Basis.Scaled(Vector3.One * DisplayScale(marker.Spawn)), marker.Node.Position);
}
