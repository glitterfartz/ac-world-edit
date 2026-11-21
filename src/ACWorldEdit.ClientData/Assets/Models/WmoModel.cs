using System.Numerics;

namespace ACWorldEdit.Assets;

/// <summary>
/// Version-17 root/group WMO reader for buildings and interiors. Preserve native material
/// batches, baked colors, liquid grids and default/selected embedded doodad sets.
/// </summary>
public static class WmoModel
{
    /// <summary>
    /// Read bounded groups, adjust winding, decode liquids and compact compatible materials. Default
    /// doodad set zero is always included; repeated set entries are deduplicated.
    /// </summary>
    public static ModelData Read(IClientFiles store, string path, ushort doodadSet = 0, CancellationToken token = default)
    {
        var bytes = store.Read(path);
        var b = new BinaryAsset(bytes);
        var root = Chunks.Read(bytes).ToDictionary(c => c.Tag);
        var version = root["MVER"];
        if (b.U(version.Data) != 17)
        {
            throw new NotSupportedException("Expected WMO version 17.");
        }

        var header = root["MOHD"];
        b.Range(header.Data, 64);
        int groups = b.I(header.Data + 4);
        // MOHD flag 0x2: the retail lighting path, where the header ambient is added at runtime instead of baked into MOCV.
        bool unifiedRenderPath = (b.H(header.Data + 60) & 2) != 0;
        if (groups > 4096)
        {
            throw new InvalidDataException("Unbounded WMO group count.");
        }

        var materials = root["MOMT"];
        // Retail roots carry no MOTX: material textures, group files (GFID) and doodad files (MODI) are
        // FileDataIDs instead of names.
        root.TryGetValue("MOTX", out var names);
        uint[] groupIds = FileIds(b, root, "GFID"), doodadIds = FileIds(b, root, "MODI");
        List<ModelSurface> surfaces = [];
        List<string> warnings = [];
        List<WmoLight> lights = ReadLights(b, root, warnings);
        for (int group = 0; group < groups; group++)
        {
            token.ThrowIfCancellationRequested();
            string file;
            if (AssetRef.IsFileReference(path))
            {
                if (group >= groupIds.Length)
                {
                    throw new InvalidDataException("WMO lists fewer group files than its header declares.");
                }

                if (groupIds[group] == 0)
                {
                    continue;
                }

                file = AssetRef.FromFileId(groupIds[group]);
            }
            else
            {
                file = path[..^4] + $"_{group:D3}.wmo";
            }

            var gb = new BinaryAsset(store.Read(file));
            var top = Chunks.Read(gb.Bytes).ToList();
            var gp = top.First(c => c.Tag == "MOGP");
            if (gp.Size < 68)
            {
                throw new InvalidDataException("Short WMO group header.");
            }

            var subChunks = Chunks.Read(gb.Bytes, gp.Data + 68, gp.Data + gp.Size).ToList();
            var sub = subChunks.GroupBy(c => c.Tag).ToDictionary(g => g.Key, g => g.First());
            // Group liquid IDs can use legacy numbering. The root flag determines
            // interpretation; a legacy zero derives its category from visible tile flags.
            // Retail liquid types come from the LiquidType DB2 table; without it the liquid is skipped.
            if (sub.TryGetValue("MLIQ", out var lq) && store.Kind == ClientKind.Retail && RetailLiquids.For(store) == null)
            {
                warnings.Add("Retail WMO liquids are not loaded: the liquid tables are unavailable.");
            }
            else if (sub.TryGetValue("MLIQ", out lq))
            {
                uint type = LiquidData.WmoType(gb.U(gp.Data + 52), b.U(header.Data + 60), gb.U(gp.Data + 8));
                if (type == 0)
                {
                    int verticesCount = checked(gb.I(lq.Data) * gb.I(lq.Data + 4));
                    gb.Range(lq.Data + 30, verticesCount, 8);
                    int flags = lq.Data + 30 + verticesCount * 8;
                    gb.Range(flags, checked(gb.I(lq.Data + 8) * gb.I(lq.Data + 12)));
                    byte value = gb.Bytes.Skip(flags).Take(gb.I(lq.Data + 8) * gb.I(lq.Data + 12)).FirstOrDefault(v => (v & 15) != 15);
                    type = LiquidData.WmoType((uint)(value & 15), 0, gb.U(gp.Data + 8));
                }

                surfaces.Add(LiquidData.Wmo(gb.Bytes.AsSpan(lq.Data, lq.Size).ToArray(), type, LiquidTextures.For(store).Kind(type)) with { LiquidStyle = LiquidTextures.For(store).Style(type) });
            }

            if (!sub.TryGetValue("MOVT", out var vt) || !sub.TryGetValue("MOVI", out var ix) || !sub.TryGetValue("MOBA", out var ba))
            {
                continue;
            }

            int vertices = vt.Size / 12;
            if (vt.Size % 12 != 0 || ix.Size % 6 != 0 || ba.Size % 24 != 0)
            {
                throw new InvalidDataException("Invalid WMO geometry arrays.");
            }

            var nr = sub.GetValueOrDefault("MONR");
            var uv = sub.GetValueOrDefault("MOTV");
            var colors = sub.GetValueOrDefault("MOCV");
            // Retail groups repeat MOCV and MOTV for a second vertex-color and UV set; composite shaders read them.
            var colors2 = subChunks.Where(c => c.Tag == "MOCV").Skip(1).FirstOrDefault();
            var uvChunk2 = subChunks.Where(c => c.Tag == "MOTV").Skip(1).FirstOrDefault();
            bool interior = (gb.U(gp.Data + 8) & 0x2000) != 0;
            var colorMode = colors.Size == 0 ? VertexColorMode.None : interior ? (unifiedRenderPath ? VertexColorMode.RetailInterior : VertexColorMode.BakedInterior) : VertexColorMode.ExteriorEmissive;
            Vector3[] positions = new Vector3[vertices], normals = new Vector3[vertices];
            Vector2[] uvs = new Vector2[vertices];
            Vector4[] tint = new Vector4[vertices];
            Vector2[] uvs2 = new Vector2[vertices];
            float[] alpha2 = new float[vertices];
            for (int i = 0; i < vertices; i++)
            {
                positions[i] = BinaryAsset.View(gb.V(vt.Data + i * 12));
                normals[i] = nr.Size >= vertices * 12 ? BinaryAsset.View(gb.V(nr.Data + i * 12)) : Vector3.UnitY;
                if (uv.Size >= vertices * 8)
                {
                    // Some stock groups (Horde barracks) contain NaN UVs. Keep their valid
                    // geometry visible and isolate those undefined texels instead of losing the WMO.
                    float u = LittleEndian.F32(gb.Bytes, uv.Data + i * 8), v = LittleEndian.F32(gb.Bytes, uv.Data + i * 8 + 4);
                    if (!float.IsFinite(u) || !float.IsFinite(v))
                    {
                        u = v = 0;
                        warnings.Add($"Group {group} contains undefined UVs; those texels use (0,0).");
                    }

                    uvs[i] = new(u, v);
                }

                int p = colors.Data + i * 4;
                tint[i] = colors.Size >= vertices * 4 ? new(gb.Bytes[p + 2] / 255f, gb.Bytes[p + 1] / 255f, gb.Bytes[p] / 255f, gb.Bytes[p + 3] / 255f) : Vector4.One;
                alpha2[i] = colors2.Size >= vertices * 4 ? gb.Bytes[colors2.Data + i * 4 + 3] / 255f : 1f;
                // Without a second UV set the layers reuse the first.
                uvs2[i] = uvChunk2.Size >= vertices * 8 && float.IsFinite(LittleEndian.F32(gb.Bytes, uvChunk2.Data + i * 8)) && float.IsFinite(LittleEndian.F32(gb.Bytes, uvChunk2.Data + i * 8 + 4))
                    ? new(LittleEndian.F32(gb.Bytes, uvChunk2.Data + i * 8), LittleEndian.F32(gb.Bytes, uvChunk2.Data + i * 8 + 4))
                    : uvs[i];
            }

            // A retail interior group (unified render path) lights itself as mix(interior, world, alpha): the interior
            // term is its color plus the header ambient, which the client adds at runtime, and the alpha weights world
            // lighting, high near an opening to the outdoors. Stored scaled by (1 - alpha), with half the ambient
            // because the shader doubles it; the alpha-weighted world term is added by the shader through normal lighting.
            if (colorMode == VertexColorMode.RetailInterior)
            {
                var ambient = new Vector3(b.Bytes[header.Data + 30], b.Bytes[header.Data + 29], b.Bytes[header.Data + 28]) / 255f;
                for (int i = 0; i < vertices; i++)
                {
                    tint[i] = new((new Vector3(tint[i].X, tint[i].Y, tint[i].Z) + ambient * 0.5f) * (1 - tint[i].W), tint[i].W);
                }
            }

            // MOBA is a 24-byte render-batch record. A flag redirects the material ID
            // into its alternate ushort field; indices still address the group arrays.
            for (int i = 0; i < ba.Size / 24; i++)
            {
                int p = ba.Data + i * 24, first = gb.I(p + 12), count = gb.H(p + 16);
                int mat = (gb.Bytes[p + 22] & 2) != 0 ? gb.H(p + 10) : gb.Bytes[p + 23];
                if (mat >= materials.Size / 64 || count % 3 != 0 || (long)first + count > ix.Size / 2)
                {
                    throw new InvalidDataException("Invalid WMO render batch.");
                }

                int m = materials.Data + mat * 64;
                // A material's texture slots hold a MOTX offset, or a FileDataID in retail roots.
                string Slot(int field)
                {
                    if (names.Size == 0)
                    {
                        uint fileId = b.U(m + field);
                        return fileId == 0 ? "" : AssetRef.FromFileId(fileId);
                    }

                    int texOffset = b.I(m + field);
                    if (texOffset < 0 || texOffset >= names.Size)
                    {
                        throw new InvalidDataException("Invalid WMO texture offset.");
                    }

                    return b.Str(names.Data + texOffset).Replace('\\', '/');
                }

                string texture = Slot(12);
                // The DF shader (23) keeps its diffuse in texture_2; texture_1 is empty or a tiny filler shared
                // across buildings (solid black in the Khaz Algar spires), which drew the whole building near-black.
                if (b.U(m + 4) == 23 && Slot(24) is { Length: > 0 } diffuse)
                {
                    texture = diffuse;
                }

                int[] indices = new int[count];
                for (int j = 0; j < count; j++)
                {
                    indices[j] = gb.H(ix.Data + (first + j) * 2);
                    if (indices[j] >= vertices)
                    {
                        throw new InvalidDataException("Invalid WMO vertex index.");
                    }
                }

                for (int j = 0; j < count; j += 3)
                {
                    (indices[j + 1], indices[j + 2]) = (indices[j + 2], indices[j + 1]);
                }

                int blend = b.I(m + 8);
                SurfaceLayers? layers = null;
                if (WmoCombiners.Resolve((int)b.U(m + 4)) is not { } info)
                {
                    warnings.Add($"WMO shader {b.U(m + 4)} is not implemented and uses its base texture.");
                }
                else
                {
                    if (info.Opaque)
                    {
                        blend = 0;
                    }

                    // Texture slots 2 and 3 are at +24 and +36; a slot the shader needs but the material leaves empty keeps the base texture.
                    string[] extra = [.. new[] { 24, 36 }.Take(info.Units - 1).Select(Slot)];
                    // A two-layer environment shader whose environment map is empty is the plain two-layer shader.
                    if (info.Combiner == LayerCombiner.WmoTwoLayerEnvMetal && extra[0].Length > 0 && extra[1].Length == 0 && WmoCombiners.Resolve(6) is { } twoLayer)
                    {
                        info = twoLayer;
                        extra = extra[..1];
                    }

                    if (info.Combiner != LayerCombiner.None && extra.All(t => t.Length > 0))
                    {
                        layers = new(info.Combiner, extra, info.Coords, uvs2, Alpha2: alpha2);
                    }
                    else if (info.Combiner is not (LayerCombiner.None or LayerCombiner.WmoEnv or LayerCombiner.WmoEnvMetal) && extra.Any(t => t.Length > 0))
                    {
                        // A reflection shader without its reflection map adds nothing, and a layered one with every extra slot empty has nothing to blend, so the base texture is exact; only a partly filled set warns.
                        warnings.Add($"WMO shader {b.U(m + 4)} has an empty texture slot and uses its base texture.");
                    }
                }

                surfaces.Add(new(positions, normals, uvs, indices, texture, blend, (b.U(m) & 4) != 0, Colors: tint, Unlit: (b.U(m) & 1) != 0 || colorMode == VertexColorMode.BakedInterior, ColorMode: colorMode, Layers: layers, Debug: $"WMO shader {b.U(m + 4)}, flags 0x{b.U(m):X}, blend {b.I(m + 8)}"));
            }
        }

        List<ModelDoodad> doodads = [];
        if (root.TryGetValue("MODD", out var dd) && root.TryGetValue("MODS", out var sets) && (root.ContainsKey("MODN") || doodadIds.Length > 0))
        {
            var included = new HashSet<int>();
            foreach (int set in new[]
            {
                0,
                (int)doodadSet
            }.Distinct())
            {
                if (set >= sets.Size / 32)
                {
                    continue;
                }

                int p = sets.Data + set * 32;
                int first = b.I(p + 20), count = b.I(p + 24);
                if ((long)first + count > dd.Size / 40)
                {
                    throw new InvalidDataException("Bad WMO doodad set.");
                }

                for (int i = first; i < first + count; i++)
                {
                    included.Add(i);
                }
            }

            foreach (int i in included)
            {
                int p = dd.Data + i * 40, name = (int)(b.U(p) & 0xffffff);
                string doodadPath;
                if (root.TryGetValue("MODN", out var dn))
                {
                    if (name >= dn.Size)
                    {
                        throw new InvalidDataException("Bad WMO doodad name.");
                    }

                    doodadPath = BinaryAsset.ModelPath(b.Str(dn.Data + name));
                }
                else
                {
                    // MODI lists a FileDataID per doodad; the name field indexes it.
                    if (name >= doodadIds.Length)
                    {
                        throw new InvalidDataException("Bad WMO doodad file index.");
                    }

                    if (doodadIds[name] == 0)
                    {
                        continue;
                    }

                    doodadPath = AssetRef.FromFileId(doodadIds[name]);
                }

                var q = new Quaternion(b.F(p + 16), b.F(p + 20), b.F(p + 24), b.F(p + 28));
                doodads.Add(new(doodadPath, BinaryAsset.View(b.V(p + 4)), BinaryAsset.View(q), b.F(p + 32)));
            }
        }

        // Compact render batches and merge by material. Exported group arrays otherwise repeat
        // thousands of unused vertices for every tiny batch (especially Stormwind).
        var merged = surfaces.Where(s => s.LiquidType == null).GroupBy(s => (s.Texture, s.Blend, s.TwoSided, s.Unlit, s.ColorMode, Layers: s.Layers?.Signature ?? "")).Select(group =>
        {
            List<Vector3> positions = [], normals = [];
            List<Vector2> uv = [], uv2 = [];
            List<Vector4> colors = [];
            List<float> alpha2 = [];
            var layers = group.First().Layers;
            List<int> indices = [];
            var remap = new Dictionary<(Vector3[] Source, int Index), int>();
            foreach (var surface in group)
            {
                foreach (int index in surface.Indices)
                {
                    var key = (surface.Positions, index);
                    if (!remap.TryGetValue(key, out int target))
                    {
                        target = positions.Count;
                        remap[key] = target;
                        positions.Add(surface.Positions[index]);
                        normals.Add(surface.Normals[index]);
                        uv.Add(surface.Uv[index]);
                        colors.Add(surface.Colors![index]);
                        if (layers != null)
                        {
                            uv2.Add(surface.Layers!.Uv2![index]);
                            alpha2.Add(surface.Layers.Alpha2![index]);
                        }
                    }

                    indices.Add(target);
                }
            }

            return new ModelSurface(positions.ToArray(), normals.ToArray(), uv.ToArray(), indices.ToArray(), group.Key.Texture, group.Key.Blend, group.Key.TwoSided, Colors: colors.ToArray(), Unlit: group.Key.Unlit, ColorMode: group.Key.ColorMode, Layers: layers == null ? null : layers with { Uv2 = uv2.ToArray(), Alpha2 = alpha2.ToArray() }, Debug: string.Join(" / ", group.Select(s => s.Debug).Where(d => d != null).Distinct()));
        }).ToArray();
        return new(merged.Concat(surfaces.Where(s => s.LiquidType != null)).ToArray(), [], 0, doodads.ToArray(), warnings.Distinct().ToArray(), Lights: [.. lights]);
    }

    /// <summary>
    /// Retail dynamic lights (MNLD, Shadowlands 9.1+): 184-byte entries with type, doodad set,
    /// BGRA color, local position/rotation, attenuation range, intensity and spot cone angles.
    /// Positions and spot directions convert to viewer space like the geometry. Entries with
    /// unknown types or non-finite numbers are skipped; a truncated chunk warns and yields nothing.
    /// </summary>
    static List<WmoLight> ReadLights(BinaryAsset b, Dictionary<string, ChunkHeader> root, List<string> warnings)
    {
        var result = new List<WmoLight>();
        if (!root.TryGetValue("MNLD", out var chunk))
        {
            return result;
        }

        if (chunk.Size % 184 != 0)
        {
            warnings.Add("WMO has a truncated MNLD light chunk and its point lights are skipped.");
            return result;
        }

        for (int i = 0; i < chunk.Size / 184; i++)
        {
            int p = chunk.Data + i * 184;
            int type = b.I(p);
            if (type is < 0 or > 3)
            {
                continue;
            }

            var color = new Vector3(b.Bytes[p + 18] / 255f, b.Bytes[p + 17] / 255f, b.Bytes[p + 16] / 255f);
            var position = BinaryAsset.View(b.V(p + 20));
            float attenStart = b.F(p + 44), attenEnd = b.F(p + 48), intensity = b.F(p + 52);
            if (!float.IsFinite(position.X + position.Y + position.Z) || !float.IsFinite(attenStart + attenEnd + intensity))
            {
                continue;
            }

            // Spot direction: the Euler rotation applied to -Z, the presumed forward axis
            // (the client's convention is not documented; wrong here only mis-aims the cone).
            var direction = type == 1
                ? BinaryAsset.View(Vector3.Transform(-Vector3.UnitZ, Quaternion.CreateFromYawPitchRoll(b.F(p + 36), b.F(p + 32), b.F(p + 40))))
                : Vector3.Zero;
            result.Add(new(type, position, color, attenStart, attenEnd, intensity, b.I(p + 12),
                direction == Vector3.Zero ? direction : Vector3.Normalize(direction),
                b.F(p + 128), b.F(p + 132), b.F(p + 124)));
        }

        return result;
    }

    /// <summary>
    /// Read a root chunk that is a plain list of FileDataIDs; absent means an empty list.
    /// </summary>
    static uint[] FileIds(BinaryAsset b, Dictionary<string, ChunkHeader> root, string tag) =>
        root.TryGetValue(tag, out var chunk) ? Enumerable.Range(0, chunk.Size / 4).Select(i => b.U(chunk.Data + i * 4)).ToArray() : [];
}
