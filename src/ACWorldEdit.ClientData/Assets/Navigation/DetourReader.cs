using System.Numerics;
namespace ACWorldEdit.Assets;

/// <summary>Little-endian Detour 7, six vertices per polygon, 64-bit references; no native code.</summary>
public static class DetourReader
{
    public const uint Magic = 0x4d4d4150;
    public static void Require(bool valid, string message) { if (!valid) throw new InvalidDataException(message); }
    public static float Float(BinaryReader r) { float f = r.ReadSingle(); Require(float.IsFinite(f), "Non-finite navigation coordinate or setting."); return f; }
    public static Vector3 Vector(BinaryReader r) => new(Float(r), Float(r), Float(r));
    public static NavigationParameters Parameters(BinaryReader r, NavigationConnection[] connections)
    {
        var origin = Vector(r); float w = Float(r), h = Float(r); int tiles = r.ReadInt32(); uint polys = r.ReadUInt32();
        Require(w > 0 && h > 0 && tiles > 0 && polys > 0 && polys <= 0x80000000u, "Invalid navigation map parameters.");
        return new(origin, w, h, tiles, polys, connections);
    }
    public static NavigationTile Read(byte[] bytes, int headerSize, int wrapperVersion, int requested, int source, string file, int fx, int fy, NavigationSettings? settings)
    {
        Require(bytes.Length <= 64 * 1024 * 1024 && bytes.Length >= headerSize + 100, "Truncated navigation tile header.");
        using var r = new BinaryReader(new MemoryStream(bytes, false));
        Require(r.ReadUInt32() == Magic && r.ReadUInt32() == 7 && r.ReadUInt32() == wrapperVersion, $"Unsupported MMap format; expected MMap {wrapperVersion}, Detour 7 (64-bit references).");
        Require(r.ReadUInt32() == bytes.Length - headerSize, "Navigation tile payload length mismatch.");
        byte liquids = r.ReadByte(); Require(liquids <= 1, "Invalid liquid marker."); r.BaseStream.Position = headerSize;
        Require(r.ReadUInt32() == 0x444e4156 && r.ReadUInt32() == 7, "Unsupported Detour payload.");
        int gx = r.ReadInt32(), gy = r.ReadInt32(), layer = r.ReadInt32(); uint user = r.ReadUInt32();
        int[] counts = new int[9];
        for (int i = 0; i < counts.Length; i++) { counts[i] = r.ReadInt32(); Require(counts[i] >= 0 && counts[i] <= 2000000, "Invalid Detour count."); }
        int pc = counts[0], vc = counts[1], lc = counts[2], dc = counts[3], dv = counts[4], dt = counts[5], bv = counts[6], oc = counts[7], ob = counts[8];
        Require(ob <= pc && dc == ob && pc - ob == oc && vc <= 65535, "Inconsistent Detour polygon counts.");
        float height = Float(r), radius = Float(r), climb = Float(r); var min = Vector(r); var max = Vector(r); float quant = Float(r);
        Require(height >= 0 && radius >= 0 && climb >= 0 && quant >= 0 && min.X <= max.X && min.Y <= max.Y && min.Z <= max.Z, "Invalid Detour bounds or agent dimensions.");
        long expected = headerSize + 100L + vc * 12L + pc * 32L + lc * 16L + dc * 12L + dv * 12L + dt * 4L + bv * 16L + oc * 36L;
        Require(expected == bytes.Length, "Invalid Detour array lengths or reference width.");
        var vertices = new Vector3[vc]; for (int i = 0; i < vc; i++) vertices[i] = Vector(r);
        var polygons = new NavigationPolygon[pc];
        for (int i = 0; i < pc; i++)
        {
            uint first = r.ReadUInt32(); var vi = new ushort[6]; var nei = new ushort[6];
            for (int j = 0; j < 6; j++) vi[j] = r.ReadUInt16();
            for (int j = 0; j < 6; j++) nei[j] = r.ReadUInt16();
            ushort flags = r.ReadUInt16(); byte n = r.ReadByte(), areaType = r.ReadByte(); int type = areaType >> 6;
            Require(type == (i < ob ? 0 : 1) && n >= (type == 0 ? 3 : 2) && n <= 6, "Invalid polygon type or vertex count.");
            for (int j = 0; j < n; j++) { Require(vi[j] < vc, "Polygon vertex outside array."); Require((nei[j] & 0x8000) != 0 || nei[j] <= pc, "Polygon neighbor outside array."); }
            polygons[i] = new(vi[..n], nei[..n], flags, (byte)(areaType & 63), (byte)type, first);
        }
        r.BaseStream.Position += lc * 16L; // Runtime link slots are not static connectivity.
        var details = new (uint V, uint T, byte N, byte C)[dc];
        for (int i = 0; i < dc; i++) { details[i] = (r.ReadUInt32(), r.ReadUInt32(), r.ReadByte(), r.ReadByte()); r.ReadUInt16(); }
        var detailVertices = new Vector3[dv]; for (int i = 0; i < dv; i++) detailVertices[i] = Vector(r);
        byte[] tris = r.ReadBytes(dt * 4); var triangles = new List<NavigationTriangle>(dt);
        for (int i = 0; i < dc; i++)
        {
            var d = details[i]; var poly = polygons[i]; Require((long)d.V + d.N <= dv && (long)d.T + d.C <= dt, "Detail mesh outside arrays.");
            Vector3 Vertex(byte index) { Require(index < poly.Vertices.Length + d.N, "Detail triangle index outside mesh."); return index < poly.Vertices.Length ? vertices[poly.Vertices[index]] : detailVertices[d.V + index - poly.Vertices.Length]; }
            for (int j = 0; j < d.C; j++) { int t = checked((int)(d.T + j) * 4); triangles.Add(new(i, Vertex(tris[t]), Vertex(tris[t+1]), Vertex(tris[t+2]), tris[t+3])); }
        }
        for (int i=0;i<bv;i++)
        {
            ushort lx=r.ReadUInt16(), ly=r.ReadUInt16(), lz=r.ReadUInt16();
            ushort hx=r.ReadUInt16(), hy=r.ReadUInt16(), hz=r.ReadUInt16();
            int index=r.ReadInt32();
            Require(lx<=hx && ly<=hy && lz<=hz && (index>=0 ? index<ob : -(long)index<=bv-i), "Invalid Detour bounding-volume node.");
        }
        var connections = new NavigationConnection[oc];
        for (int i = 0; i < oc; i++)
        {
            var a = Vector(r); var b = Vector(r); float rad = Float(r); ushort poly = r.ReadUInt16(); byte flags = r.ReadByte(), side = r.ReadByte(); uint id = r.ReadUInt32();
            Require(rad >= 0 && poly >= ob && poly < pc, "Invalid off-mesh connection."); connections[i] = new(a, b, rad, poly, flags, side, id);
        }
        return new(requested, source, file, fx, fy, gx, gy, layer, user, liquids != 0, min, max, height, radius, climb, settings, vertices, polygons, triangles.ToArray(), connections);
    }
}
