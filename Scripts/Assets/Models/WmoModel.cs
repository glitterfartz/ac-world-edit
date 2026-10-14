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
    public static ModelData Read(AssetStore store, string path, ushort doodadSet = 0, CancellationToken token = default)
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
        if (groups > 4096)
        {
            throw new InvalidDataException("Unbounded WMO group count.");
        }

        var materials = root["MOMT"];
        var names = root["MOTX"];
        List<ModelSurface> surfaces = [];
        List<string> warnings = [];
        for (int group = 0; group < groups; group++)
        {
            token.ThrowIfCancellationRequested();
            string file = path[..^4] + $"_{group:D3}.wmo";
            var gb = new BinaryAsset(store.Read(file));
            var top = Chunks.Read(gb.Bytes).ToList();
            var gp = top.First(c => c.Tag == "MOGP");
            if (gp.Size < 68)
            {
                throw new InvalidDataException("Short WMO group header.");
            }

            var sub = Chunks.Read(gb.Bytes, gp.Data + 68, gp.Data + gp.Size).GroupBy(c => c.Tag).ToDictionary(g => g.Key, g => g.First());
            // Group liquid IDs can use legacy numbering. The root flag determines
            // interpretation; a legacy zero derives its category from visible tile flags.
            if (sub.TryGetValue("MLIQ", out var lq))
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

                surfaces.Add(LiquidData.Wmo(gb.Bytes.AsSpan(lq.Data, lq.Size).ToArray(), type, LiquidTextures.For(store).Kind(type)));
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
            bool interior = (gb.U(gp.Data + 8) & 0x2000) != 0;
            var colorMode = colors.Size == 0 ? VertexColorMode.None : interior ? VertexColorMode.BakedInterior : VertexColorMode.ExteriorEmissive;
            Vector3[] positions = new Vector3[vertices], normals = new Vector3[vertices];
            Vector2[] uvs = new Vector2[vertices];
            Vector4[] tint = new Vector4[vertices];
            for (int i = 0; i < vertices; i++)
            {
                positions[i] = BinaryAsset.View(gb.V(vt.Data + i * 12));
                normals[i] = nr.Size >= vertices * 12 ? BinaryAsset.View(gb.V(nr.Data + i * 12)) : Vector3.UnitY;
                if (uv.Size >= vertices * 8)
                {
                    // Some stock groups (Horde barracks) contain NaN UVs. Keep their valid
                    // geometry visible and isolate those undefined texels instead of losing the WMO.
                    float u = BitConverter.ToSingle(gb.Bytes, uv.Data + i * 8), v = BitConverter.ToSingle(gb.Bytes, uv.Data + i * 8 + 4);
                    if (!float.IsFinite(u) || !float.IsFinite(v))
                    {
                        u = v = 0;
                        warnings.Add($"Group {group} contains undefined UVs; those texels use (0,0).");
                    }

                    uvs[i] = new(u, v);
                }

                int p = colors.Data + i * 4;
                tint[i] = colors.Size >= vertices * 4 ? new(gb.Bytes[p + 2] / 255f, gb.Bytes[p + 1] / 255f, gb.Bytes[p] / 255f, 1) : Vector4.One;
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
                int texOffset = b.I(m + 12);
                if (texOffset >= names.Size)
                {
                    throw new InvalidDataException("Invalid WMO texture offset.");
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

                surfaces.Add(new(positions, normals, uvs, indices, b.Str(names.Data + texOffset).Replace('\\', '/'), b.I(m + 8), (b.U(m) & 4) != 0, Colors: tint, Unlit: (b.U(m) & 1) != 0 || colorMode == VertexColorMode.BakedInterior, ColorMode: colorMode));
                if (b.U(m + 4) != 0)
                {
                    warnings.Add($"WMO shader {b.U(m + 4)} uses base-texture preview.");
                }
            }
        }

        List<ModelDoodad> doodads = [];
        if (root.TryGetValue("MODD", out var dd) && root.TryGetValue("MODN", out var dn) && root.TryGetValue("MODS", out var sets))
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
                if (name >= dn.Size)
                {
                    throw new InvalidDataException("Bad WMO doodad name.");
                }

                var q = new Quaternion(b.F(p + 16), b.F(p + 20), b.F(p + 24), b.F(p + 28));
                doodads.Add(new(BinaryAsset.ModelPath(b.Str(dn.Data + name)), BinaryAsset.View(b.V(p + 4)), BinaryAsset.View(q), b.F(p + 32)));
            }
        }

        // Compact render batches and merge by material. Exported group arrays otherwise repeat
        // thousands of unused vertices for every tiny batch (especially Stormwind).
        var merged = surfaces.Where(s => s.LiquidType == null).GroupBy(s => (s.Texture, s.Blend, s.TwoSided, s.Unlit, s.ColorMode)).Select(group =>
        {
            List<Vector3> positions = [], normals = [];
            List<Vector2> uv = [];
            List<Vector4> colors = [];
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
                    }

                    indices.Add(target);
                }
            }

            return new ModelSurface(positions.ToArray(), normals.ToArray(), uv.ToArray(), indices.ToArray(), group.Key.Texture, group.Key.Blend, group.Key.TwoSided, Colors: colors.ToArray(), Unlit: group.Key.Unlit, ColorMode: group.Key.ColorMode);
        }).ToArray();
        return new(merged.Concat(surfaces.Where(s => s.LiquidType != null)).ToArray(), [], 0, doodads.ToArray(), warnings.Distinct().ToArray());
    }
}
