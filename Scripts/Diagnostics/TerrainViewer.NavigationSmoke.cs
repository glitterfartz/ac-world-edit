using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>Opt-in navigation upload/picking/lifecycle checks, exercised with a real renderer.</summary>
public partial class NavigationOverlay
{
    public bool SmokeReady => tiles.Count > 0 && tiles.Values.All(t => t.Finished) && pending == null;
    public void SmokeDisplay(bool throughWorld)
    {
        xray = throughWorld; bounds = true; Inspect = true; RefreshDisplay();
        foreach (var check in GetTree().Root.FindChildren("*", "CheckBox", true, false).OfType<CheckBox>())
        {
            if (check.Text == "X-ray") check.SetPressedNoSignal(throughWorld);
            if (check.Text is "Tile boundaries" or "Inspect navmesh") check.SetPressedNoSignal(true);
        }
    }
    public void SmokeFrameInterior(Camera3D camera)
    {
        var triangle = tiles.Values.SelectMany(t => t.Data.Triangles).First();
        var at = (View(triangle.A) + View(triangle.B) + View(triangle.C)) / 3;
        camera.Position = at + new Vector3(0, 1.3f, .7f);
        camera.LookAt(at);
    }
    public bool SmokePick(Camera3D camera)
    {
        foreach (var tile in tiles.Values)
        {
            foreach (var tri in tile.Data.Triangles.Take(100))
            {
                var at = (View(tri.A) + View(tri.B) + View(tri.C)) / 3;
                if (camera.IsPositionBehind(at)) continue;
                Pick(camera, camera.UnprojectPosition(at));
                if (selectedTile != null) return true;
            }
        }
        return false;
    }
}

public partial class TerrainViewer
{
    bool navigationSmoke;
    int navigationStage, navigationFrames;
    bool navigationSmokeMoved, navigationSmokeFramed;
    double navigationTime;
    void NavigationSmoke(double delta)
    {
        navigationTime += delta;
        if (navigationTime > 180) { SmokeFail("NAVIGATION", "timed out"); return; }
        if (busy || world == null || currentMapId == null) return;
        if (navigationStage == 0)
        {
            if (!navigationSmokeMoved)
            {
                navigationSmokeMoved = true;
                var flags = OS.GetCmdlineUserArgs();
                if (flags.FirstOrDefault(a => a.StartsWith("--navigation-smoke-map=")) is string mapFlag)
                {
                    int target = int.Parse(mapFlag.Split('=')[1]); int index = mapList.FindIndex(m => m.Id == target);
                    if (index < 0) { SmokeFail("NAVIGATION", "map unavailable"); return; }
                    if (currentMapId != target) { maps.Select(index); _ = OpenMap(); return; }
                }
                if (flags.FirstOrDefault(a => a.StartsWith("--navigation-smoke-tile=")) is string tileFlag)
                {
                    var parts = tileFlag.Split('=')[1].Split(','); Teleport(new(int.Parse(parts[0]), int.Parse(parts[1]))); return;
                }
            }
            creatures.SetLayerEnabled(false); gameObjects.SetLayerEnabled(false); graveyards.SetLayerEnabled(false); waypoints.SetLayerEnabled(false);
            navigation.SetLayerEnabled(true); navigation.SmokeDisplay(false);
            sidebar.ShowNavigationTab(); navigationStage = 1; return;
        }
        if (!navigation.SmokeReady || models.Busy || pending != null || building != null || navigationFrames++ < 90) return;
        string label = (profile.Kind == ClientKind.Retail ? "tc" : "ac") + (currentMapId != 0 ? $"-{currentMapId}" : "");
        if (currentMapId != 0 && !navigationSmokeFramed)
        {
            navigationSmokeFramed = true;
            navigation.SmokeFrameInterior(camera);
            yaw = camera.Rotation.Y; pitch = camera.Rotation.X;
            navigationFrames = 0;
            return;
        }
        if (navigationStage == 1)
        {
            if (!navigation.SmokePick(camera)) { SmokeFail("NAVIGATION", "polygon picking failed"); return; }
            if (DisplayServer.GetName() != "headless") GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://artifacts/navigation-{label}.png"));
            navigation.SmokeDisplay(true); navigationStage = 2; navigationFrames = 0; return;
        }
        if (navigationStage == 2)
        {
            if (DisplayServer.GetName() != "headless") GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://artifacts/navigation-{label}-xray.png"));
            // Invalidate pending state twice before reloading the same source.
            navigation.SetMap(1); navigation.SetMap(currentMapId); navigationStage = 3; navigationFrames = 0; return;
        }
        navigation.SetLayerEnabled(false);
        if (navigation.TileCount != 0) { SmokeFail("NAVIGATION", "disable did not release tiles"); return; }
        GD.Print($"NAVIGATION_SMOKE_OK {label} detail geometry / picking / xray / generation reset / disable"); GetTree().Quit();
    }
}
