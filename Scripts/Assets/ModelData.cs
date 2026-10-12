using System.Numerics;
using System.Text;

namespace ACWorldEdit.Assets;
// Parsed data contains no Godot resources; all archive/decode work can run on a worker.
public sealed record ModelSurface(Vector3[] Positions, Vector3[] Normals, Vector2[] Uv, int[] Indices, string Texture, int Blend, bool TwoSided, int[]? Bones = null, float[]? Weights = null, Vector4[]? Colors = null, bool Unlit = false, uint? LiquidType = null, uint LiquidKind = 0);
public sealed record ModelDoodad(string Path, Vector3 Position, Quaternion Rotation, float Scale);
public sealed record ModelData(ModelSurface[] Surfaces, ModelBone[] Bones, uint Duration, ModelDoodad[] Doodads, string[] Warnings, ushort[]? AnimationIds = null)
{
    public long EstimatedBytes => Surfaces.Sum(s => (long)s.Positions.Length * 80 + s.Indices.Length * 4L);
}

public sealed record ModelBone(int Parent, Vector3 Pivot, ModelTrack<Vector3> Translation, ModelTrack<Quaternion> Rotation, ModelTrack<Vector3> Scale);
public sealed record ModelTrack<T>(uint[] Times, T[] Values, ushort Interpolation, uint GlobalDuration);
