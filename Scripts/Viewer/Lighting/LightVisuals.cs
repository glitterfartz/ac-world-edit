using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Godot views of the pure lighting model in <c>Assets/World/ClientLighting.cs</c>. The parsing and
/// every numeric decision live there, where the xUnit project compiles them; this file only exposes
/// them in the types the scene nodes use. They are extension methods because a partial type cannot
/// span assemblies.
/// </summary>
public static class LightVisuals
{
    /// <summary>The curve sampled at a tick, as a Godot colour.</summary>
    public static Color At(this LightBand band, double tick)
    {
        var (r, g, b) = band.Rgb(tick);
        return new Color(r, g, b);
    }

    /// <summary>The region center in view space, as the scene nodes expect it.</summary>
    public static Vector3 ViewPosition(this LightRegion region) => new(region.ViewX, region.ViewY, region.ViewZ);
}
