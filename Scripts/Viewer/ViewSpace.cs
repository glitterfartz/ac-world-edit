using ACWorldEdit.Foundation;
using Godot;

namespace ACWorldEdit;

/// <summary>
/// Server-to-view axis mapping in Godot types: view X = -server Y, view Y = server Z,
/// view Z = -server X. The mapping itself lives in <see cref="WorldCoordinates"/>; this is the
/// Vector3 cast layer the viewer and overlays share.
/// </summary>
public static class ViewSpace
{
    public static Vector3 ToView(double serverX, double serverY, double serverZ)
    {
        var p = WorldCoordinates.ToView(new(serverX, serverY, serverZ));
        return new((float)p.X, (float)p.Y, (float)p.Z);
    }

    public static Vector3 ToView(WorldPose pose) => ToView(pose.X, pose.Y, pose.Z);

    public static Point3 ToServer(Vector3 view) => WorldCoordinates.ToServer(new(view.X, view.Y, view.Z));

    /// <summary>The quantized spawn-query center for a view-space position (the camera) on a map.</summary>
    public static SpawnQueryPoint QueryPointAt(Vector3 view, ushort map)
    {
        var server = ToServer(view);
        return SpawnQueryPoint.FromPose(new(map, server.X, server.Y, server.Z, 0));
    }
}
