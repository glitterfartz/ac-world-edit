using Godot;

namespace ACWorldEdit.Assets;

/// <summary>
/// Godot view of the pure lighting model in <c>Assets/World/ClientLighting.cs</c>. The parsing and
/// every numeric decision live there, where the xUnit project compiles them; this half only exposes
/// the types the viewer's renderer needs.
/// </summary>
public sealed partial class LightBand
{
    /// <summary>The curve sampled at a tick, as a Godot colour.</summary>
    public Color At(double tick)
    {
        var (r, g, b) = Rgb(tick);
        return new Color(r, g, b);
    }
}

public sealed partial record LightRegion
{
    /// <summary>The region center in view space, as the scene nodes expect it.</summary>
    public Vector3 ViewPosition => new(ViewX, ViewY, ViewZ);
}
