using Godot;

namespace ACWorldEdit;

/// <summary>
/// Camera bookmarks: the toolbar window that saves where the camera is and teleports back to it.
/// </summary>
public partial class ViewerShell
{
    BookmarkWindow bookmarkWindow = null!;

    CameraBookmark CaptureBookmark()
    {
        var server = CameraServer();
        var (zone, _) = CurrentLocation();
        int map = currentMapId ?? 0;
        string mapName = mapList.FirstOrDefault(m => m.Id == map)?.Name ?? $"Map {map}";
        return new((uint)map, server.X, server.Y, server.Z, yaw, pitch, mapName, zone);
    }

    async Task<string> OpenBookmark(CameraBookmark bookmark)
    {
        if (bookmark.Map != currentMapId)
        {
            if (busy)
            {
                return "The viewer is busy loading; try again in a moment.";
            }

            OpenMapAtServerPose(bookmark.Map, bookmark.X, bookmark.Y, bookmark.Z);
            await OpenMap();
            if (currentMapId != bookmark.Map)
            {
                return $"Could not open map {bookmark.Map}.";
            }
        }

        camera.Position = ViewSpace.ToView(bookmark.X, bookmark.Y, bookmark.Z);
        camera.Rotation = new Vector3(bookmark.Pitch, bookmark.Yaw, 0);
        yaw = bookmark.Yaw;
        pitch = bookmark.Pitch;
        cameraFocused = true;
        streamer.UpdateWanted();
        return string.IsNullOrEmpty(bookmark.Zone) ? $"Opened {bookmark.MapName}." : $"Opened {bookmark.MapName} · {bookmark.Zone}.";
    }
}
