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
    public static ModelData Read(IClientFiles store, string path, string[]? replacements = null, string? baked = null, CancellationToken token = default, ushort animationId = 0, string? hair = null, string? fur = null, int hairGeoset = 1, string? eyes = null, GeosetVariants? geosets = null)
    {
        var variants = geosets?.Parse();
        path = BinaryAsset.ModelPath(path);
        var file = store.Read(path);
        // Legion+ files are chunked: an MD21 chunk holds the classic MD20 block (its offsets are relative to it),
        // SFID names the skin files and TXID the textures by FileDataID. Wrath files are the bare MD20.
        bool chunked = file.Length >= 4 && Encoding.ASCII.GetString(file, 0, 4) == "MD21";
        uint[] skinIds = [], textureIds = [];
        uint skeletonId = 0;
        Dictionary<(ushort, ushort), uint> animFiles = [];
        if (chunked)
        {
            (file, skinIds, textureIds, animFiles, skeletonId) = SplitChunked(file);
        }

        var b = new BinaryAsset(file);
        b.Range(0, 304);
        uint version = b.U(4);
        if (Encoding.ASCII.GetString(b.Bytes, 0, 4) != "MD20" || (chunked ? version < 272 : version != 264))
        {
            throw new NotSupportedException(chunked ? "Expected MD20 version 272 or later inside the MD21 chunk." : "Expected Wrath MD20 version 264.");
        }

        // Character models name a .skel file in SKID that holds the bones (SKB1), sequences and global
        // loops (SKS1) and its own AFID; their MD20 lists none. Skeleton .anim files keep bone tracks
        // in AFSB instead of AFM2.
        BinaryAsset sequenceData = b, boneData = b;
        int sequenceField = 28, globalField = 20, boneField = 44;
        string animChunk = "AFM2";
        if (skeletonId != 0)
        {
            (sequenceData, boneData, animFiles) = ReadSkeleton(store, skeletonId);
            (sequenceField, globalField, boneField, animChunk) = (8, 0, 0, "AFSB");
        }

        // MD20 descriptors store count/byte-offset pairs. Vertex/bone/sequence records are
        // 48/88/64 bytes respectively; offsets below are for Wrath version 264 only.
        var vertices = b.Array(60, 48);
        var bones = boneData.Array(boneField, 88);
        var sequences = sequenceData.Array(sequenceField, 64);
        var textures = b.Array(80, 16);
        var materials = b.Array(112, 4);
        var textureLookup = b.Array(128, 2);
        var globals = sequenceData.Array(globalField, 4);
        // Batch appearance: M2Color is two 20-byte tracks (RGB, fixed16 alpha), texture weights
        // one fixed16 track and texture transforms translation/float-quaternion/scale tracks.
        // Batches reach them through the transparency and texture-transform lookup tables.
        var colors = b.Array(72, 40);
        var textureWeights = b.Array(88, 20);
        var textureTransforms = b.Array(96, 60);
        var weightLookup = b.Array(144, 2);
        var transformLookup = b.Array(152, 2);
        List<string> warnings = [];
        int sequenceIndex = -1;
        var available = Enumerable.Range(0, sequences.Count).Select(i => sequenceData.H(sequences.Offset + i * 64)).Distinct().Order().ToArray();
        for (int i = 0; i < sequences.Count; i++)
        {
            if (sequenceData.H(sequences.Offset + i * 64) == animationId)
            {
                sequenceIndex = i;
                break;
            }
        }

        // GameObject state clips (Closed/Opened) are requested for every door, but many door
        // models only author Stand; the client then plays Stand as well.
        if (sequenceIndex < 0 && animationId != 0)
        {
            for (int i = 0; i < sequences.Count; i++)
            {
                if (sequenceData.H(sequences.Offset + i * 64) == 0)
                {
                    sequenceIndex = i;
                    break;
                }
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
            var s = sequenceData;
            while ((s.U(sequences.Offset + sequenceIndex * 64 + 12) & 0x40) != 0)
            {
                if (!aliases.Add(sequenceIndex))
                {
                    throw new InvalidDataException("Cyclic animation alias.");
                }

                sequenceIndex = s.H(sequences.Offset + sequenceIndex * 64 + 62);
                if (sequenceIndex >= sequences.Count)
                {
                    throw new InvalidDataException("Bad animation alias.");
                }
            }

            int p = sequences.Offset + sequenceIndex * 64;
            duration = s.U(p + 4);
            if ((s.U(p + 12) & 0x20) == 0 && chunked)
            {
                // Chunked models name a sequence's .anim file by FileDataID in AFID; the track data
                // sits in one of its chunks, with offsets relative to the chunk's data.
                external = animFiles.TryGetValue((s.H(p), s.H(p + 2)), out uint animFile) ? ReadAnimChunk(store, animFile, animChunk) : null;
                if (external == null)
                {
                    warnings.Add($"Native animation unavailable: sequence {s.H(p)}-{s.H(p + 2)} has no .anim file.");
                    sequenceIndex = -1;
                }
            }
            else if ((s.U(p + 12) & 0x20) == 0)
            {
                string anim = path[..^3] + $"{s.H(p):D4}-{s.H(p + 2):D2}.anim";
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

        // Bone tracks are described in the skeleton's bone data, the rest in MD20 (`d`).
        ModelTrack<T> Track<T>(int p, int size, Func<BinaryAsset, int, T> read, BinaryAsset? d = null)
        {
            d ??= b;
            ushort interpolation = d.H(p);
            short global = d.S(p + 2);
            var timeArrays = d.Array(p + 4, 8);
            var keyArrays = d.Array(p + 12, 8);
            uint globalDuration = global >= 0 && global < globals.Count ? sequenceData.U(globals.Offset + global * 4) : 0;
            int sequence = global >= 0 ? 0 : sequenceIndex;
            if (sequence < 0 || sequence >= timeArrays.Count || sequence >= keyArrays.Count)
            {
                return new([], [], interpolation, globalDuration, global >= 0);
            }

            // Array descriptors live in MD20 even when ordinary sequence payloads live in
            // an external .anim file. Global tracks continue to use MD20 and their own clock.
            var source = global < 0 && external != null ? new BinaryAsset(external) : d;
            int tp = timeArrays.Offset + sequence * 8, kp = keyArrays.Offset + sequence * 8;
            int count = d.I(tp), offset = d.I(tp + 4), keyCount = d.I(kp), keyOffset = d.I(kp + 4);
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

            return new(times, values, interpolation, globalDuration, global >= 0);
        }

        float Q(short v) => (v < 0 ? v + 32768 : v - 32767) / 32767f;
        ModelTrack<float> Fixed16(int p) => Track(p, 2, (d, o) => d.S(o) / 32767f);
        var appearance = new Dictionary<(int, int, int), SurfaceAnimation>();
        // Resolve one batch's color, weight and first-unit UV transform. Lookup entries of -1
        // mean "no animation": opaque white, full weight and identity UVs.
        SurfaceAnimation Appearance(int color, int weightCombo, int transformCombo)
        {
            int weight = weightCombo < weightLookup.Count ? b.S(weightLookup.Offset + weightCombo * 2) : -1;
            int transform = transformCombo < transformLookup.Count ? b.S(transformLookup.Offset + transformCombo * 2) : -1;
            color = color < colors.Count ? color : -1;
            weight = weight < textureWeights.Count ? weight : -1;
            transform = transform < textureTransforms.Count ? transform : -1;
            if (appearance.TryGetValue((color, weight, transform), out var found))
            {
                return found;
            }

            ModelTrack<Vector3> noVector = new([], [], 0, 0);
            int c = colors.Offset + color * 40, t = textureTransforms.Offset + transform * 60;
            found = new(
                color >= 0 ? Track(c, 12, (d, o) => d.V(o)) : noVector,
                color >= 0 ? Fixed16(c + 20) : new([], [], 0, 0),
                weight >= 0 ? Fixed16(textureWeights.Offset + weight * 20) : new([], [], 0, 0),
                transform >= 0 ? Track(t, 12, (d, o) => d.V(o)) : noVector,
                transform >= 0 ? Track(t + 20, 16, (d, o) => new Quaternion(d.F(o), d.F(o + 4), d.F(o + 8), d.F(o + 12))) : new([], [], 0, 0),
                transform >= 0 ? Track(t + 40, 12, (d, o) => d.V(o)) : noVector);
            appearance[(color, weight, transform)] = found;
            return found;
        }

        ModelBone[] skeleton = new ModelBone[bones.Count];
        for (int i = 0; i < bones.Count; i++)
        {
            int p = bones.Offset + i * 88, parent = boneData.S(p + 8);
            if (parent >= bones.Count || parent == i)
            {
                throw new InvalidDataException("Bad bone parent.");
            }

            skeleton[i] = new(parent, BinaryAsset.View(boneData.V(p + 76)), Track(p + 16, 12, (d, o) => BinaryAsset.View(d.V(o)), boneData), Track(p + 36, 8, (d, o) => BinaryAsset.View(new Quaternion(Q(d.S(o)), Q(d.S(o + 2)), Q(d.S(o + 4)), Q(d.S(o + 6)))), boneData), Track(p + 56, 12, (d, o) =>
            {
                var v = d.V(o);
                return new Vector3(v.Y, v.Z, v.X);
            }, boneData));
        }

        // SKIN triangle entries index a skin lookup table, not the MD20 vertex array
        // directly. Compact each batch after following both indirections.
        string skinPath = chunked ? (skinIds.Length > 0 && skinIds[0] != 0 ? AssetRef.FromFileId(skinIds[0]) : throw new InvalidDataException("Model lists no skin file.")) : path[..^3] + "00.skin";
        var skin = new BinaryAsset(store.Read(skinPath));
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
            // Character base geosets plus the selected hairstyle (group 0), or the variant each group's
            // customization chose; clothing variants need equipment composition.
            if (variants != null ? !GeosetVariants.Shows(variants, geoset) : baked != null && geoset > 0 && (geoset < 100 ? geoset != hairGeoset : geoset % 100 != 1))
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
            else if (type == 0 && tex < textureIds.Length && textureIds[tex] != 0)
            {
                texture = AssetRef.FromFileId(textureIds[tex]);
            }
            else if (type == 1 && baked != null)
            {
                texture = baked;
            }
            else if (type == 6)
            {
                // Unresolvable hair (no matching CharSections texture) is hidden, not drawn pink.
                if (hair == null)
                {
                    continue;
                }

                texture = hair;
            }
            else if (type == 19 && variants != null)
            {
                // Retail eyes: hidden like hair when the customization names no texture.
                if (eyes == null)
                {
                    continue;
                }

                texture = eyes;
            }
            else if (type == 8 && fur != null)
            {
                texture = fur;
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

            surfaces.Add(new(pos.ToArray(), norm.ToArray(), uv.ToArray(), indices.ToArray(), texture.Replace('\\', '/'), b.H(materials.Offset + material * 4 + 2), (b.H(materials.Offset + material * 4) & 4) != 0, bi.ToArray(), weights.ToArray(), Unlit: (b.H(materials.Offset + material * 4) & 1) != 0, Material: Appearance(skin.S(p + 8), skin.H(p + 20), skin.H(p + 22))));
            if (skin.H(p + 14) > 1)
            {
                warnings.Add("Multitexture M2 batch currently uses its base texture.");
            }
        }

        return new(surfaces.ToArray(), skeleton, duration, [], warnings.Distinct().ToArray(), available);
    }

    /// <summary>
    /// Split an MD21 container into its MD20 block, the SFID/TXID/AFID file lists and the SKID skeleton
    /// (0 when none). Chunk tags are stored unreversed in these files; unknown chunks are skipped.
    /// </summary>
    static (byte[] Model, uint[] Skins, uint[] Textures, Dictionary<(ushort, ushort), uint> Animations, uint Skeleton) SplitChunked(byte[] file)
    {
        byte[]? model = null;
        uint[] skins = [], textures = [];
        uint skeleton = 0;
        Dictionary<(ushort, ushort), uint> animations = [];
        foreach (var chunk in Chunks.Read(file, reversed: false))
        {
            uint[] Ids() => Enumerable.Range(0, chunk.Size / 4).Select(i => LittleEndian.U32(file, chunk.Data + i * 4)).ToArray();
            switch (chunk.Tag)
            {
                case "MD21":
                    model = file.AsSpan(chunk.Data, chunk.Size).ToArray();
                    break;
                case "SFID":
                    skins = Ids();
                    break;
                case "TXID":
                    textures = Ids();
                    break;
                case "AFID":
                    animations = AnimationIds(file, chunk.Data, chunk.Size);
                    break;
                case "SKID" when chunk.Size >= 4:
                    skeleton = LittleEndian.U32(file, chunk.Data);
                    break;
            }
        }

        return (model ?? throw new InvalidDataException("Missing MD21 chunk."), skins, textures, animations, skeleton);
    }

    /// <summary>AFID entries are 8 bytes: animation ID, sub-animation (variation) index, FileDataID.</summary>
    static Dictionary<(ushort, ushort), uint> AnimationIds(byte[] file, int data, int size)
    {
        Dictionary<(ushort, ushort), uint> animations = [];
        for (int i = 0; i + 8 <= size; i += 8)
        {
            animations[(LittleEndian.U16(file, data + i), LittleEndian.U16(file, data + i + 2))] = LittleEndian.U32(file, data + i + 4);
        }

        return animations;
    }

    /// <summary>
    /// The SKS1 (global loops, sequences) and SKB1 (bones) chunks of a .skel file, each with offsets
    /// relative to its own data, and the file's AFID animation list. A parent skeleton (SKPD) is not
    /// followed: the character skeletons creatures use carry their own sequences.
    /// </summary>
    static (BinaryAsset Sequences, BinaryAsset Bones, Dictionary<(ushort, ushort), uint> Animations) ReadSkeleton(IClientFiles store, uint fileId)
    {
        byte[] file = store.Read(AssetRef.FromFileId(fileId));
        BinaryAsset? sequences = null, bones = null;
        Dictionary<(ushort, ushort), uint> animations = [];
        foreach (var chunk in Chunks.Read(file, reversed: false))
        {
            switch (chunk.Tag)
            {
                case "SKS1":
                    sequences = new(file.AsSpan(chunk.Data, chunk.Size).ToArray());
                    break;
                case "SKB1":
                    bones = new(file.AsSpan(chunk.Data, chunk.Size).ToArray());
                    break;
                case "AFID":
                    animations = AnimationIds(file, chunk.Data, chunk.Size);
                    break;
            }
        }

        if (sequences == null || bones == null)
        {
            throw new InvalidDataException($"Skeleton {fileId} has no {(sequences == null ? "SKS1" : "SKB1")} chunk.");
        }

        return (sequences, bones, animations);
    }

    /// <summary>One chunk's payload from a chunked .anim file, or null when the file is missing or has none.</summary>
    static byte[]? ReadAnimChunk(IClientFiles store, uint fileId, string tag)
    {
        byte[] data;
        try
        {
            data = store.Read(AssetRef.FromFileId(fileId));
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        foreach (var chunk in Chunks.Read(data, reversed: false))
        {
            if (chunk.Tag == tag)
            {
                return data.AsSpan(chunk.Data, chunk.Size).ToArray();
            }
        }

        return null;
    }
}
