namespace ACWorldEdit.Assets;

/// <summary>
/// One batch of a decoded model as text, for the diagnostic dump: which textures and combiner it
/// resolved to and the shader it came from, without the vertex data.
/// </summary>
public sealed record SurfaceReport(string Texture, int Blend, bool TwoSided, bool Unlit, string ColorMode, int Vertices, int Triangles, string? Combiner, string[] LayerTextures, string[] LayerCoords, string? Debug);

/// <summary>
/// A model's batches and decode warnings. A published model drops its textures, layers and warnings
/// (<see cref="ModelData.ForProbes"/>), so the library keeps this summary taken just before.
/// </summary>
public sealed record ModelReport(SurfaceReport[] Surfaces, string[] Warnings)
{
    public static ModelReport From(ModelData data) => new(
        [.. data.Surfaces.Select(s => new SurfaceReport(
            s.Texture,
            s.Blend,
            s.TwoSided,
            s.Unlit,
            s.ColorMode.ToString(),
            s.Positions.Length,
            s.Indices.Length / 3,
            s.Layers?.Combiner.ToString(),
            s.Layers?.Textures ?? [],
            [.. (s.Layers?.Coords ?? []).Select(c => c.ToString())],
            s.Debug))],
        [.. data.Warnings.Distinct()]);
}
