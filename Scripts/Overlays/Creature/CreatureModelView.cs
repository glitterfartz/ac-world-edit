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

    public void Forget(ulong guid)
    {
        admitted.Remove(guid);
        riderAnims.Remove(guid);
        mountScales.Remove(guid);
    }

    /// <summary>The rider clip last computed for a marker (its stance/seat animation), so selection
    /// can keep playing it instead of resetting to Stand.</summary>
    public ushort RiderAnimFor(ulong guid) => riderAnims.GetValueOrDefault(guid);

    public void ResetTimer() => updated = 0;

    /// <summary>Free every batch and cached appearance; the markers themselves are owned elsewhere.</summary>
    public void Clear()
    {
        batches.FreeAll(b => b.Node);
        appearances.Clear();
        displayAnims.Clear();
        elevations.Clear();
        mountScales.Clear();
        riderAnims.Clear();
        seatFollows.Clear();
        admitted.Clear();
        updated = 0;
    }

    /// <summary>Refresh the batches at most every 0.2 s; membership changes rebuild only that batch.</summary>
    public void Update(Camera3D camera, IEnumerable<CreatureMarkers.Marker> markers, SpawnPreview preview, bool layerEnabled, ulong? selected, ushort previewAnimation, ModelLibrary? library,
        IReadOnlyDictionary<ulong, (uint Stand, uint Emote, uint Mount, float MountScale, bool Flying)>? states = null, Func<uint, ushort?>? emoteAnim = null)
    {
        if (library?.Catalog == null || System.Diagnostics.Stopwatch.GetElapsedTime(updated).TotalSeconds <= 0.2)
        {
            return;
        }

        var byGuid = markers.ToDictionary(m => m.Spawn.Guid);
        var groups = GroupModels(camera, byGuid, preview, layerEnabled, selected, previewAnimation, library, states, emoteAnim);
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
        IReadOnlyDictionary<ulong, (uint Stand, uint Emote, uint Mount, float MountScale, bool Flying)>? states, Func<uint, ushort?>? emoteAnim)
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

            var rider = RiderAnimation(marker, states, emoteAnim);
            riderAnims[marker.Spawn.Guid] = rider;
            var key = ModelKey.ForCreature(appearance, selected == marker.Spawn.Guid ? previewAnimation : rider);
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
    /// the rider's offset above its marker is remembered for its own transform, label and gizmo
    /// moves. An airborne mount flaps when its model has a flight clip, otherwise it stands.</summary>
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

        ushort anim = SpawnAppearance.MountClip(state.Flying,
            id => displayAnims.TryGetValue(state.Mount, out var clips) && clips.Contains(id));

        var key = ModelKey.ForCreature(mount, anim) with { Extra = "mount" };
        if (!groups.TryGetValue(key, out var list))
        {
            groups[key] = list = [];
        }

        list.Add(marker.Spawn.Guid);
        float scale = state.MountScale * mount.Scale;
        float factor = float.IsFinite(scale) && scale > 0 ? scale : 1;
        mountScales[marker.Spawn.Guid] = factor;
        // Static fallback until FollowSeats glues the rider to the animated seat bone each frame:
        // the mount's back height (CreatureModelData.mountHeight), or the old 1.5 for displays
        // that name none.
        elevations[marker.Spawn.Guid] = new Vector3(0, (mount.SeatHeight > 0 ? mount.SeatHeight : 1.5f) * factor, 0);
    }

    /// <summary>
    /// Glue each mounted rider to its mount's animated seat bone: the rider instance transform is
    /// the mount instance transform times the seat bone's current world matrix applied to the seat
    /// position, re-scaled to the rider. Runs every frame (unlike the 0.2 s batch rebuild): the
    /// shader advances the mount's bones with the shared bone clock, and the CPU samples the same
    /// baked palette frames with the same value (ClientShaderGlobals.CreatureClock), so the rider's
    /// root rides the saddle instead of floating statically while the mount flaps. Markers without
    /// a seat track keep the static fallback from AddMount.
    /// </summary>
    public void FollowSeats(CreatureMarkers markers, IReadOnlyDictionary<ulong, (uint Stand, uint Emote, uint Mount, float MountScale, bool Flying)>? states, ModelLibrary? library)
    {
        if (states == null || library?.Catalog == null)
        {
            return;
        }

        // Identical value to the shader's creature_clock (float seconds): same frame, same phase.
        double now = Time.GetTicksMsec() / 1000.0f;
        var riders = new Dictionary<ulong, (ModelInstance Node, int Index)>();
        foreach (var batch in batches)
        {
            if (batch.Key.Extra == "mount" || !batch.Value.Node.ModelReady)
            {
                continue;
            }

            for (int i = 0; i < batch.Value.Guids.Length; i++)
            {
                riders[batch.Value.Guids[i]] = (batch.Value.Node, i);
            }
        }

        if (riders.Count == 0)
        {
            return;
        }

        foreach (var batch in batches)
        {
            if (batch.Key.Extra != "mount" || !batch.Value.Node.ModelReady || batch.Value.Node.Transforms == null)
            {
                continue;
            }

            if (!seatFollows.TryGetValue(batch.Key, out var follow))
            {
                if (library.SeatTrack(batch.Key) is not { } track || track.Count <= 0)
                {
                    continue;
                }

                follow = SeatFollow.For(track);
                seatFollows[batch.Key] = follow;
            }

            for (int i = 0; i < batch.Value.Guids.Length; i++)
            {
                ulong guid = batch.Value.Guids[i];
                if (!riders.TryGetValue(guid, out var rider) || !markers.TryGetValue(guid, out var marker))
                {
                    continue;
                }

                rider.Node.SetPlacement(rider.Index, FollowTransform(batch.Value.Node.Transforms[i], marker, follow, now));
            }
        }
    }

    /// <summary>A mount variant's baked seat follow: seat-bone world matrices per palette frame
    /// plus the clip duration and the seat position in that bone's space.</summary>
    sealed record SeatFollow(float[] Frames, int Count, float Duration, System.Numerics.Vector3 Seat)
    {
        public static SeatFollow For(SeatTrack track) => new(track.Frames, track.Count, track.Duration, track.Seat);
    }

    readonly Dictionary<ModelKey, SeatFollow> seatFollows = [];

    Transform3D FollowTransform(Transform3D mount, CreatureMarkers.Marker marker, SeatFollow follow, double now)
    {
        // The shader's frame formula, replicated on the CPU clock: mod(TIME, duration) maps to the
        // baked rows with linear interpolation, so the rider matches the mount's skinned pose. The
        // client glues an attached model to boneMatrix * position, so the rider's root sits at the
        // seat bone's current transform of the seat, upright in mount space.
        double time = follow.Duration > 0 ? now % follow.Duration : 0;
        double exact = follow.Duration > 0 ? time / follow.Duration * (follow.Count - 1) : 0;
        int a = Math.Clamp((int)exact, 0, follow.Count - 1);
        int b = Math.Min(a + 1, follow.Count - 1);
        float f = (float)(exact - a);
        var mat = LerpMatrix(follow.Frames, a, b, f, follow.Count);
        var seat = System.Numerics.Vector3.Transform(follow.Seat, mat);
        var world = mount * new Transform3D(Basis.Identity, new Vector3(seat.X, seat.Y, seat.Z));
        float scale = DisplayScale(marker.Spawn);
        return new(world.Basis.Orthonormalized().Scaled(Vector3.One * scale), world.Origin);
    }

    static System.Numerics.Matrix4x4 LerpMatrix(float[] frames, int a, int b, float f, int count)
    {
        if (a == b || f <= 0)
        {
            a = Math.Clamp(a, 0, count - 1);
            int p = a * 16;
            return new(frames[p], frames[p + 1], frames[p + 2], frames[p + 3], frames[p + 4], frames[p + 5], frames[p + 6], frames[p + 7], frames[p + 8], frames[p + 9], frames[p + 10], frames[p + 11], frames[p + 12], frames[p + 13], frames[p + 14], frames[p + 15]);
        }

        var ma = LerpMatrix(frames, a, a, 0, count);
        var mb = LerpMatrix(frames, Math.Min(b, count - 1), Math.Min(b, count - 1), 0, count);
        return new(
            ma.M11 + (mb.M11 - ma.M11) * f, ma.M12 + (mb.M12 - ma.M12) * f, ma.M13 + (mb.M13 - ma.M13) * f, ma.M14 + (mb.M14 - ma.M14) * f,
            ma.M21 + (mb.M21 - ma.M21) * f, ma.M22 + (mb.M22 - ma.M22) * f, ma.M23 + (mb.M23 - ma.M23) * f, ma.M24 + (mb.M24 - ma.M24) * f,
            ma.M31 + (mb.M31 - ma.M31) * f, ma.M32 + (mb.M32 - ma.M32) * f, ma.M33 + (mb.M33 - ma.M33) * f, ma.M34 + (mb.M34 - ma.M34) * f,
            ma.M41 + (mb.M41 - ma.M41) * f, ma.M42 + (mb.M42 - ma.M42) * f, ma.M43 + (mb.M43 - ma.M43) * f, ma.M44 + (mb.M44 - ma.M44) * f);
    }

    /// <summary>Rider offsets above their markers (global space): the static mountHeight fallback
    /// until FollowSeats glues the rider to the animated seat bone each frame.</summary>
    readonly Dictionary<ulong, Vector3> elevations = [];
    /// <summary>Mount render scale per marker (template look times the mount's display scale), so a
    /// mount draws at its own size instead of its rider's.</summary>
    readonly Dictionary<ulong, float> mountScales = [];
    /// <summary>Last computed rider clip per marker, so selecting a creature keeps playing its stance
    /// or seat animation instead of resetting to Stand.</summary>
    readonly Dictionary<ulong, ushort> riderAnims = [];

    /// <summary>
    /// The animation a marker plays: dead always plays Death (held on its last frame, like the
    /// client), else the emote's clip when it
    /// names one the model has, else the seat clip when mounted and the model has it, else the stance
    /// clip when the model has it, else Stand. A clip the model lacks is never picked: the loader
    /// would show the reference pose.
    /// </summary>
    ushort RiderAnimation(CreatureMarkers.Marker marker, IReadOnlyDictionary<ulong, (uint Stand, uint Emote, uint Mount, float MountScale, bool Flying)>? states, Func<uint, ushort?>? emoteAnim)
    {
        if (states?.TryGetValue(marker.Spawn.Guid, out var state) != true)
        {
            return 0;
        }

        displayAnims.TryGetValue(marker.Spawn.DisplayId, out var clips);
        bool available(ushort id) => clips != null && clips.Contains(id);
        return SpawnAppearance.RiderClip(state.Stand, state.Emote != 0 ? emoteAnim?.Invoke(state.Emote) : null, state.Mount != 0, available);
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
                batches[group.Key] = (library.Attach(owner, group.Key, group.Value.Select(guid => ModelTransform(markers[guid], mount ? mountScales.GetValueOrDefault(guid, DisplayScale(markers[guid].Spawn)) : DisplayScale(markers[guid].Spawn), mount ? Vector3.Zero : elevations.GetValueOrDefault(guid))).ToArray()), group.Value.ToArray());
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
                bool mount = batch.Key.Extra == "mount";
                batch.Value.Node.SetPlacement(index, ModelTransform(marker, mount ? mountScales.GetValueOrDefault(guid, DisplayScale(marker.Spawn)) : DisplayScale(marker.Spawn), mount ? Vector3.Zero : elevations.GetValueOrDefault(guid)));
            }
        }
    }

    /// <summary>Place the name label just above the scaled native model (plus its mount's seat height).</summary>
    public float LabelHeight(CreatureMarkers.Marker marker, Aabb bounds)
    {
        float scale = DisplayScale(marker.Spawn);
        float top = bounds.End.Y * scale;
        float height = float.IsFinite(top) && top > 0 ? top + 0.6f : CreatureMarkers.FallbackLabelHeight;
        return height + elevations.GetValueOrDefault(marker.Spawn.Guid).Y;
    }

    /// <summary>The template/display scale a creature is drawn at.</summary>
    public float DisplayScale(CreatureSpawn spawn) =>
        spawn.DisplayScale * (appearances.GetValueOrDefault(spawn.DisplayId)?.Scale ?? 1);

    /// <summary>Compose creature heading, effective draft position, draw scale and mount seat offset.</summary>
    Transform3D ModelTransform(CreatureMarkers.Marker marker, float scale, Vector3 offset) =>
        new(marker.Node.Basis.Scaled(Vector3.One * scale), marker.Node.Position + offset);
}
