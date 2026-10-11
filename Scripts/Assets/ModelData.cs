using System.Numerics;
using System.Text;
namespace ACWorldEdit.Assets;

// Parsed data contains no Godot resources; all archive/decode work can run on a worker.
public sealed record ModelSurface(Vector3[] Positions, Vector3[] Normals, Vector2[] Uv, int[] Indices,
    string Texture, int Blend, bool TwoSided, int[]? Bones = null, float[]? Weights = null, Vector4[]? Colors = null, bool Unlit = false, uint? LiquidType = null, uint LiquidKind = 0);
public sealed record ModelDoodad(string Path, Vector3 Position, Quaternion Rotation, float Scale);
public sealed record ModelData(ModelSurface[] Surfaces, ModelBone[] Bones, uint Duration, ModelDoodad[] Doodads, string[] Warnings, ushort[]? AnimationIds = null)
{
    public long EstimatedBytes => Surfaces.Sum(s => (long)s.Positions.Length * 80 + s.Indices.Length * 4L);
}
public sealed record ModelBone(int Parent, Vector3 Pivot, ModelTrack<Vector3> Translation, ModelTrack<Quaternion> Rotation, ModelTrack<Vector3> Scale);
public sealed record ModelTrack<T>(uint[] Times, T[] Values, ushort Interpolation, uint GlobalDuration);

public sealed class BinaryAsset(byte[] bytes)
{
    public byte[] Bytes => bytes;
    public int Length => bytes.Length;
    public void Range(int p, int count, int stride = 1)
    { if (p < 0 || count < 0 || stride < 0 || (long)p + (long)count * stride > bytes.Length) throw new InvalidDataException("Model array outside asset bounds."); }
    public uint U(int p) { Range(p, 4); return BitConverter.ToUInt32(bytes, p); }
    public int I(int p) => checked((int)U(p));
    public ushort H(int p) { Range(p, 2); return BitConverter.ToUInt16(bytes, p); }
    public short S(int p) => unchecked((short)H(p));
    public float F(int p) { Range(p, 4); float v = BitConverter.ToSingle(bytes, p); if (!float.IsFinite(v)) throw new InvalidDataException("Non-finite model value."); return v; }
    public Vector3 V(int p) => new(F(p), F(p + 4), F(p + 8));
    public string Str(int p) { Range(p, 1); int end = System.Array.IndexOf(bytes, (byte)0, p); if (end < 0) throw new InvalidDataException("Unterminated asset string."); return Encoding.UTF8.GetString(bytes, p, end - p); }
    public (int Count, int Offset) Array(int p, int stride)
    { int count = I(p), offset = I(p + 4); Range(offset, count, stride); return (count, offset); }
    public static Vector3 View(Vector3 v) => new(-v.Y, v.Z, -v.X);
    public static Quaternion View(Quaternion q) => Quaternion.Normalize(new(-q.Y, q.Z, -q.X, q.W));
    public static string ModelPath(string path) => System.IO.Path.ChangeExtension(path.Replace('\\', '/'), ".m2");
}

public sealed class DbcTable
{
    readonly BinaryAsset data;
    readonly int count, fields, stride, strings;
    readonly Dictionary<uint, int> rows = [];
    public DbcTable(byte[] bytes)
    {
        data = new(bytes); data.Range(0, 20);
        if (Encoding.ASCII.GetString(bytes, 0, 4) != "WDBC") throw new InvalidDataException("Expected WDBC.");
        count = data.I(4); fields = data.I(8); stride = data.I(12);
        if (fields < 1 || stride != fields * 4) throw new InvalidDataException("Invalid DBC stride.");
        data.Range(20, count, stride); strings = checked(20 + count * stride); data.Range(strings, data.I(16));
        for (int i = 0; i < count; i++) rows[data.U(20 + i * stride)] = 20 + i * stride;
    }
    int Field(uint id, int field) { if (field < 0 || field >= fields || !rows.TryGetValue(id, out int p)) throw new InvalidDataException("Missing DBC record/field."); return p + field * 4; }
    public bool Contains(uint id) => rows.ContainsKey(id);
    public IEnumerable<uint> Ids => rows.Keys;
    public uint U(uint id, int field) => data.U(Field(id, field));
    public float F(uint id, int field) => data.F(Field(id, field));
    public string Str(uint id, int field) => data.Str(checked(strings + (int)U(id, field)));
}

public sealed record CreatureAppearance(string Path, string[] Textures, float Scale, string? BakedTexture);
public sealed class CreatureCatalog
{
    readonly DbcTable displays, models, extras;
    public IReadOnlyDictionary<ushort,string> AnimationNames { get; }
    public CreatureCatalog(AssetStore store)
    {
        displays = new(store.Read("DBFilesClient/CreatureDisplayInfo.dbc"));
        models = new(store.Read("DBFilesClient/CreatureModelData.dbc"));
        extras = new(store.Read("DBFilesClient/CreatureDisplayInfoExtra.dbc"));
        var animations = new DbcTable(store.Read("DBFilesClient/AnimationData.dbc"));
        AnimationNames = animations.Ids.Where(id => id<=ushort.MaxValue).ToDictionary(id => (ushort)id,id => animations.Str(id,1));
    }
    public CreatureAppearance Resolve(uint id)
    {
        uint model = displays.U(id, 1), extra = displays.U(id, 3);
        string path = BinaryAsset.ModelPath(models.Str(model, 2));
        string dir = path[..(path.LastIndexOf('/') + 1)];
        string[] textures = Enumerable.Range(6, 3).Select(f => displays.Str(id, f)).Select(s => s.Length == 0 ? "" : dir + s + ".blp").ToArray();
        string? baked = extra != 0 && extras.Contains(extra) ? extras.Str(extra, 20) : null;
        return new(path, textures, displays.F(id, 4), string.IsNullOrEmpty(baked) ? null : "Textures/BakedNpcTextures/" + baked);
    }
}
