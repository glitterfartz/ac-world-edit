using System.Numerics;
using System.Text;

namespace ACWorldEdit.Assets;
// Parsed data contains no Godot resources; all archive/decode work can run on a worker.
/// <summary>
/// Detached batch with viewer-space vertices, material metadata and optional four-influence
/// skinning, color or liquid channels. Format readers validate array/index relationships.
/// </summary>
public sealed record ModelSurface(Vector3[] Positions, Vector3[] Normals, Vector2[] Uv, int[] Indices, string Texture, int Blend, bool TwoSided, int[]? Bones = null, float[]? Weights = null, Vector4[]? Colors = null, bool Unlit = false, uint? LiquidType = null, uint LiquidKind = 0);
/// <summary>
/// Embedded WMO M2 placement in viewer-space local coordinates. Its parent WMO transform
/// composes with this pose when binding instances.
/// </summary>
public sealed record ModelDoodad(string Path, Vector3 Position, Quaternion Rotation, float Scale);
/// <summary>
/// Worker-produced geometry, requested skeleton/clip and embedded doodads. Warnings preserve
/// fidelity limits; fields never own Godot objects or database rows.
/// </summary>
public sealed record ModelData(ModelSurface[] Surfaces, ModelBone[] Bones, uint Duration, ModelDoodad[] Doodads, string[] Warnings, ushort[]? AnimationIds = null)
{
    public long EstimatedBytes => Surfaces.Sum(s => (long)s.Positions.Length * 80 + s.Indices.Length * 4L);
}

/// <summary>
/// Native parent/pivot and translation/rotation/scale tracks for one requested clip. Sampling
/// applies parent transforms after pivot-relative local animation.
/// </summary>
public sealed record ModelBone(int Parent, Vector3 Pivot, ModelTrack<Vector3> Translation, ModelTrack<Quaternion> Rotation, ModelTrack<Vector3> Scale);
/// <summary>
/// Native timestamps/keys, interpolation mode and optional global loop duration. Empty sequences
/// use the channel&apos;s identity/default value.
/// </summary>
public sealed record ModelTrack<T>(uint[] Times, T[] Values, ushort Interpolation, uint GlobalDuration);
