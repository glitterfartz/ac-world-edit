using System.Numerics;
using System.Text;

namespace ACWorldEdit.Assets;
// Parsed data contains no Godot resources; all archive/decode work can run on a worker.
/// <summary>
/// How a WMO batch uses its baked MOCV colors. Interior groups (flag 0x2000) store their lighting
/// there: it replaces scene lighting and is doubled like the client's modulate-2x. Exterior groups
/// keep sun lighting and add the colors as an emissive term, since many are near black.
/// </summary>
public enum VertexColorMode
{
    None,
    BakedInterior,
    ExteriorEmissive
}

/// <summary>
/// Detached batch with viewer-space vertices, material metadata and optional four-influence
/// skinning, color or liquid channels. Format readers validate array/index relationships.
/// </summary>
public sealed record ModelSurface(Vector3[] Positions, Vector3[] Normals, Vector2[] Uv, int[] Indices, string Texture, int Blend, bool TwoSided, int[]? Bones = null, float[]? Weights = null, Vector4[]? Colors = null, bool Unlit = false, uint? LiquidType = null, uint LiquidKind = 0, SurfaceAnimation? Material = null, VertexColorMode ColorMode = VertexColorMode.None, SurfaceLayers? Layers = null);
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
    /// <summary>
    /// Bytes held by the surfaces' arrays. Batches of one WMO group share a <see cref="ModelSurface.Positions"/>
    /// array (and the channels that go with it), which is counted once.
    /// </summary>
    public long EstimatedBytes
    {
        get
        {
            HashSet<Vector3[]> seen = new(ReferenceEqualityComparer.Instance);
            long bytes = 0;
            foreach (var s in Surfaces)
            {
                bytes += s.Indices.Length * 4L;
                if (seen.Add(s.Positions))
                {
                    bytes += s.Positions.Length * 12L + s.Normals.Length * 12L + s.Uv.Length * 8L + (s.Colors?.Length ?? 0) * 16L + (s.Bones?.Length ?? 0) * 4L + (s.Weights?.Length ?? 0) * 4L;
                }
            }

            return bytes;
        }
    }

    /// <summary>
    /// What a viewer keeps once the GPU has the model: the embedded doodads and animation IDs, plus,
    /// for a WMO, each batch's positions, indices and liquid type for ground probes and liquid
    /// checks. Everything only upload reads (normals, UVs, colors, skinning, textures, bone tracks,
    /// warnings) is dropped. Positions arrays stay shared between batches.
    /// </summary>
    public ModelData ForProbes(bool keepSurfaces) => this with
    {
        Surfaces = keepSurfaces ? [.. Surfaces.Select(s => new ModelSurface(s.Positions, [], [], s.Indices, "", 0, false, LiquidType: s.LiquidType))] : [],
        Bones = [],
        Warnings = [],
    };
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
public sealed record ModelTrack<T>(uint[] Times, T[] Values, ushort Interpolation, uint GlobalDuration, bool Global = false);
/// <summary>
/// Native M2 batch appearance tracks: color (RGB/alpha), texture weight and the first texture
/// unit's UV transform. The client multiplies color alpha by weight; a fully transparent batch
/// is not drawn. Untextured-UV batches leave the transform tracks empty (identity).
/// </summary>
public sealed record SurfaceAnimation(ModelTrack<Vector3> Color, ModelTrack<float> Alpha, ModelTrack<float> Weight, ModelTrack<Vector3> UvTranslation, ModelTrack<Quaternion> UvRotation, ModelTrack<Vector3> UvScale);
