using System.Numerics;
using System.Text;

namespace ACWorldEdit.Assets;

/// <summary>
/// Wrath MD20/SKIN reader for native clips, aliases/external tracks, material batches and skin
/// influences. Unsupported appearance/material details produce explicit warnings.
/// </summary>
public static class M2Model
{
    /// <summary>
    /// Read the model/first skin, choose a native sequence, resolve texture replacements and compact
    /// referenced vertices. Preserve bone lookup chains and winding across the viewer basis.
    /// </summary>
    public static ModelData Read(AssetStore store, string path, string[]? replacements = null, string? baked = null, CancellationToken token = default, ushort animationId = 0)
    {
        path = BinaryAsset.ModelPath(path);
        var b = new BinaryAsset(store.Read(path));
        b.Range(0, 304);
        if (Encoding.ASCII.GetString(b.Bytes, 0, 4) != "MD20" || b.U(4) != 264)
        {
            throw new NotSupportedException("Expected Wrath MD20 version 264.");
        }

        // MD20 descriptors store count/byte-offset pairs. Vertex/bone/sequence records are
        // 48/88/64 bytes respectively; offsets below are for Wrath version 264 only.
        var vertices = b.Array(60, 48);
        var bones = b.Array(44, 88);
        var sequences = b.Array(28, 64);
        var textures = b.Array(80, 16);
        var materials = b.Array(112, 4);
        var textureLookup = b.Array(128, 2);
        var globals = b.Array(20, 4);
        List<string> warnings = [];
        int sequenceIndex = -1;
        var available = Enumerable.Range(0, sequences.Count).Select(i => b.H(sequences.Offset + i * 64)).Distinct().Order().ToArray();
        for (int i = 0; i < sequences.Count; i++)
        {
            if (b.H(sequences.Offset + i * 64) == animationId)
            {
                sequenceIndex = i;
                break;
            }
        }

        if (sequenceIndex < 0 && bones.Count > 0)
        {
            warnings.Add($"Native animation {animationId} unavailable; showing reference pose.");
        }

        uint duration = 0;
        byte[]? external = null;
        if (sequenceIndex >= 0)
        {
            // Alias records redirect to another sequence. Track lookup must use the resolved
            // sequence, while available IDs still describe the native menu exposed to users.
            var aliases = new HashSet<int>();
            while ((b.U(sequences.Offset + sequenceIndex * 64 + 12) & 0x40) != 0)
            {
                if (!aliases.Add(sequenceIndex))
                {
                    throw new InvalidDataException("Cyclic animation alias.");
                }

                sequenceIndex = b.H(sequences.Offset + sequenceIndex * 64 + 62);
                if (sequenceIndex >= sequences.Count)
                {
                    throw new InvalidDataException("Bad animation alias.");
                }
            }

            int p = sequences.Offset + sequenceIndex * 64;
            duration = b.U(p + 4);
            if ((b.U(p + 12) & 0x20) == 0)
            {
                string anim = path[..^3] + $"{b.H(p):D4}-{b.H(p + 2):D2}.anim";
                try
                {
                    external = store.Read(anim);
                }
                catch (FileNotFoundException)
                {
                    warnings.Add("Native animation unavailable: " + anim);
                    sequenceIndex = -1;
                }
            }
        }

        ModelTrack<T> Track<T>(int p, int size, Func<BinaryAsset, int, T> read)
        {
            ushort interpolation = b.H(p);
            short global = b.S(p + 2);
            var timeArrays = b.Array(p + 4, 8);
            var keyArrays = b.Array(p + 12, 8);
            uint globalDuration = global >= 0 && global < globals.Count ? b.U(globals.Offset + global * 4) : 0;
            int sequence = global >= 0 ? 0 : sequenceIndex;
            if (sequence < 0 || sequence >= timeArrays.Count || sequence >= keyArrays.Count)
            {
                return new([], [], interpolation, globalDuration);
            }

            // Array descriptors live in MD20 even when ordinary sequence payloads live in
            // an external .anim file. Global tracks continue to use MD20 and their own clock.
            var source = global < 0 && external != null ? new BinaryAsset(external) : b;
            int tp = timeArrays.Offset + sequence * 8, kp = keyArrays.Offset + sequence * 8;
            int count = b.I(tp), offset = b.I(tp + 4), keyCount = b.I(kp), keyOffset = b.I(kp + 4);
            if (count != keyCount)
            {
                throw new InvalidDataException("Animation timestamp/key count mismatch.");
            }

            source.Range(offset, count, 4);
            source.Range(keyOffset, count, size * (interpolation > 1 ? 3 : 1));
            uint[] times = new uint[count];
            T[] values = new T[count];
            for (int j = 0; j < count; j++)
            {
                times[j] = source.U(offset + j * 4);
                values[j] = read(source, keyOffset + j * size * (interpolation > 1 ? 3 : 1));
                if (j > 0 && times[j] < times[j - 1])
                {
                    throw new InvalidDataException("Unordered animation keys.");
                }
            }

            if (interpolation > 1)
            {
                warnings.Add("Spline animation track approximated with linear interpolation.");
            }

            return new(times, values, interpolation, globalDuration);
        }

        float Q(short v) => (v < 0 ? v + 32768 : v - 32767) / 32767f;
        ModelBone[] skeleton = new ModelBone[bones.Count];
        for (int i = 0; i < bones.Count; i++)
        {
            int p = bones.Offset + i * 88, parent = b.S(p + 8);
            if (parent >= bones.Count || parent == i)
            {
                throw new InvalidDataException("Bad bone parent.");
            }

            skeleton[i] = new(parent, BinaryAsset.View(b.V(p + 76)), Track(p + 16, 12, (d, o) => BinaryAsset.View(d.V(o))), Track(p + 36, 8, (d, o) => BinaryAsset.View(new Quaternion(Q(d.S(o)), Q(d.S(o + 2)), Q(d.S(o + 4)), Q(d.S(o + 6))))), Track(p + 56, 12, (d, o) =>
            {
                var v = d.V(o);
                return new Vector3(v.Y, v.Z, v.X);
            }));
        }

        // SKIN triangle entries index a skin lookup table, not the MD20 vertex array
        // directly. Compact each batch after following both indirections.
        var skin = new BinaryAsset(store.Read(path[..^3] + "00.skin"));
        skin.Range(0, 48);
        if (Encoding.ASCII.GetString(skin.Bytes, 0, 4) != "SKIN")
        {
            throw new InvalidDataException("Bad M2 skin magic.");
        }

        var lookup = skin.Array(4, 2);
        var triangles = skin.Array(12, 2);
        var submeshes = skin.Array(28, 48);
        var batches = skin.Array(36, 24);
        var properties = skin.Array(20, 4);
        var boneLookup = b.Array(120, 2);
        List<ModelSurface> surfaces = [];
        for (int i = 0; i < batches.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            int p = batches.Offset + i * 24, section = skin.H(p + 4);
            if (section >= submeshes.Count)
            {
                throw new InvalidDataException("Bad skin section.");
            }

            int s = submeshes.Offset + section * 48, geoset = skin.H(s);
            // Character base geosets; additional clothing/hair variants need appearance composition.
            if (baked != null && geoset > 0 && geoset % 100 != 1)
            {
                continue;
            }

            int material = skin.H(p + 10), combo = skin.H(p + 16), boneStart = skin.H(s + 14);
            if (material >= materials.Count || combo >= textureLookup.Count)
            {
                throw new InvalidDataException("Bad skin material lookup.");
            }

            int tex = b.H(textureLookup.Offset + combo * 2);
            if (tex >= textures.Count)
            {
                throw new InvalidDataException("Bad M2 texture lookup.");
            }

            int t = textures.Offset + tex * 16;
            uint type = b.U(t);
            string texture = "";
            if (type == 0 && b.U(t + 8) != 0)
            {
                texture = b.Str(b.I(t + 12));
            }
            else if (type == 1 && baked != null)
            {
                texture = baked;
            }
            else if (type is >= 11 and <= 13 && replacements != null)
            {
                texture = replacements[type - 11];
            }

            if (texture.Length == 0)
            {
                warnings.Add($"Unresolved replaceable texture type {type}.");
            }

            int first = skin.H(s + 8) + (skin.H(s + 2) << 16), count = skin.H(s + 10);
            skin.Range(triangles.Offset + first * 2, count, 2);
            var remap = new Dictionary<int, int>();
            List<Vector3> pos = [], norm = [];
            List<Vector2> uv = [];
            List<int> bi = [], indices = [];
            List<float> weights = [];
            for (int j = 0; j < count; j++)
            {
                int local = skin.H(triangles.Offset + (first + j) * 2);
                if (local >= lookup.Count)
                {
                    throw new InvalidDataException("Bad skin vertex lookup.");
                }

                if (!remap.TryGetValue(local, out int index))
                {
                    index = remap.Count;
                    remap.Add(local, index);
                    int vertex = skin.H(lookup.Offset + local * 2);
                    if (vertex >= vertices.Count)
                    {
                        throw new InvalidDataException("Bad M2 vertex.");
                    }

                    int v = vertices.Offset + vertex * 48;
                    pos.Add(BinaryAsset.View(b.V(v)));
                    norm.Add(BinaryAsset.View(b.V(v + 20)));
                    uv.Add(new(b.F(v + 32), b.F(v + 36)));
                    for (int k = 0; k < 4; k++)
                    {
                        // Skin properties map vertex influences through this submesh's bone combo.
                        int bone = b.Bytes[v + 16 + k];
                        if (local < properties.Count)
                        {
                            int slot = boneStart + skin.Bytes[properties.Offset + local * 4 + k];
                            if (slot < boneLookup.Count)
                            {
                                bone = b.H(boneLookup.Offset + slot * 2);
                            }
                        }

                        bi.Add(bone < bones.Count ? bone : 0);
                        weights.Add(b.Bytes[v + 12 + k] / 255f);
                    }
                }

                indices.Add(index);
            }

            if (count % 3 != 0)
            {
                throw new InvalidDataException("Incomplete M2 triangles.");
            }

            // Godot uses clockwise front faces; the basis rotation preserves orientation.
            for (int j = 0; j < indices.Count; j += 3)
            {
                (indices[j + 1], indices[j + 2]) = (indices[j + 2], indices[j + 1]);
            }

            surfaces.Add(new(pos.ToArray(), norm.ToArray(), uv.ToArray(), indices.ToArray(), texture.Replace('\\', '/'), b.H(materials.Offset + material * 4 + 2), (b.H(materials.Offset + material * 4) & 4) != 0, bi.ToArray(), weights.ToArray(), Unlit: (b.H(materials.Offset + material * 4) & 1) != 0));
            if (skin.H(p + 14) > 1)
            {
                warnings.Add("Multitexture M2 batch currently uses its base texture.");
            }
        }

        return new(surfaces.ToArray(), skeleton, duration, [], warnings.Distinct().ToArray(), available);
    }
}
