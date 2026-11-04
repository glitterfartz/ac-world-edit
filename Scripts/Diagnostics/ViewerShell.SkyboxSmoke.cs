using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Opt-in graphical check that a LightSkybox model stays behind world geometry. For each Light.dbc
/// region that resolves a skybox (up to a few per map), it stands at the region center and renders
/// the same view three times: skybox hidden over two different solid backgrounds, then skybox
/// shown. Pixels identical across the two backgrounds are opaque world; the skybox must change
/// almost none of them. Open sky and translucent world surfaces (glass, ice, water), where the sky
/// correctly shows through, are excluded by construction. Needs a rendering (non-headless) display;
/// screenshots go to ignored artifacts/.
/// </summary>
public partial class ViewerShell
{
    bool skyboxSmoke;
    int skyboxMap = -1, skyboxTarget, skyboxView, skyboxStage, skyboxWait, skyboxViews;
    double skyboxTime;
    bool skyboxFailed;
    Image? skyboxHidden, skyboxOtherBackground;
    static readonly Color SmokeBackgroundA = new(1, 0, 1), SmokeBackgroundB = new(0, 1, 0);
    List<(Vector3 Position, string Skybox)> skyboxTargets = [];
    // Outland and Icecrown Citadel were reported; Northrend adds more outdoor skybox regions.
    static readonly string[] SkyboxMaps = ["Expansion01", "IcecrownCitadel", "Northrend"];
    static readonly float[] SkyboxYaws = [0, Mathf.Pi * 0.5f, Mathf.Pi, Mathf.Pi * 1.5f];
    const int SkyboxTargetsPerMap = 3;

    void SkyboxSmoke(double delta)
    {
        skyboxTime += delta;
        if (skyboxTime > 900)
        {
            SmokeFail("SKYBOX_SMOKE_TIMEOUT " + message);
            return;
        }

        if (DisplayServer.GetName() == "headless")
        {
            SmokeFail("SKYBOX_SMOKE_NEEDS_RENDERING run without --headless");
            return;
        }

        if (busy || store == null || mapList.Count == 0)
        {
            return;
        }

        if (skyboxWait > 0)
        {
            skyboxWait--;
            return;
        }

        bool settled = world != null && TerrainSettled && !models.Busy;
        switch (skyboxStage)
        {
            case 0:
                NextSkyboxMap();
                break;
            case 1:
                if (!settled)
                {
                    return;
                }

                MoveToSkyboxTarget();
                skyboxStage = 2;
                skyboxWait = 10;
                break;
            case 2:
                if (!settled)
                {
                    return;
                }

                AimSkyboxView();
                skyboxStage = 3;
                skyboxWait = 20;
                break;
            case 3:
                if (!settled)
                {
                    return;
                }

                // Panels never change between captures; hiding them keeps the fraction about the 3D view.
                SetInterfaceVisible(false);
                // Animated materials (ice, glow, water) would drift between the three captures and
                // read as the sky changing world pixels, so freeze engine time until they are taken.
                Engine.TimeScale = 0;
                sky.SkyboxVisible = false;
                sky.DiagnosticBackground = SmokeBackgroundA;
                skyboxStage = 4;
                skyboxWait = 3;
                break;
            case 4:
                skyboxHidden = GetViewport().GetTexture().GetImage();
                sky.DiagnosticBackground = SmokeBackgroundB;
                skyboxStage = 5;
                skyboxWait = 3;
                break;
            case 5:
                skyboxOtherBackground = GetViewport().GetTexture().GetImage();
                sky.DiagnosticBackground = SmokeBackgroundA;
                sky.SkyboxVisible = true;
                skyboxStage = 6;
                skyboxWait = 3;
                break;
            case 6:
                CompareSkyboxFrames();
                Engine.TimeScale = 1;
                sky.DiagnosticBackground = null;
                SetInterfaceVisible(true);
                skyboxView++;
                if (skyboxView < SkyboxYaws.Length)
                {
                    skyboxStage = 2;
                }
                else
                {
                    skyboxView = 0;
                    skyboxTarget++;
                    skyboxStage = skyboxTarget < skyboxTargets.Count ? 1 : 0;
                }

                break;
        }
    }

    void NextSkyboxMap()
    {
        skyboxMap++;
        if (skyboxMap >= SkyboxMaps.Length)
        {
            bool covered = skyboxViews > 0;
            GD.Print(skyboxFailed || !covered ? $"SKYBOX_SMOKE_FAILED views={skyboxViews}" : $"SKYBOX_SMOKE_PASSED views={skyboxViews}");
            GetTree().Quit(skyboxFailed || !covered ? 1 : 0);
            return;
        }

        int index = mapList.FindIndex(m => m.Directory == SkyboxMaps[skyboxMap]);
        if (index < 0 || lighting == null)
        {
            GD.Print("SKYBOX_SMOKE_SKIPPED missing map or lighting " + SkyboxMaps[skyboxMap]);
            return;
        }

        // Region centers whose Default-slot profile names a skybox, one per distinct skybox model.
        var table = lighting;
        skyboxTargets = table.ForMap((uint)mapList[index].Id)
            .Select(region => (Region: region, Skybox: table.SkyboxPath(table.Profile(region.ParamsId)?.SkyboxId ?? 0)))
            .Where(pair => pair.Skybox != null)
            .GroupBy(pair => pair.Skybox!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(SkyboxTargetsPerMap)
            .Select(pair => (new Vector3(pair.Region.ViewPosition.X, pair.Region.ViewPosition.Y, pair.Region.ViewPosition.Z), pair.Skybox!))
            .ToList();
        GD.Print($"SKYBOX_SMOKE_MAP map={SkyboxMaps[skyboxMap]} skyboxTargets={skyboxTargets.Count}");
        if (skyboxTargets.Count == 0)
        {
            return;
        }

        sky.Playing = false;
        sky.SetTimeOfDay(12);
        skyboxTarget = 0;
        skyboxView = 0;
        var first = TileOf(skyboxTargets[0].Position);
        maps.Select(index);
        tileX.Value = first.X;
        tileY.Value = first.Y;
        skyboxStage = 1;
        _ = OpenMap();
    }

    static TileKey TileOf(Vector3 view) => new((int)Math.Floor(view.X / TerrainTile.Size + 32), (int)Math.Floor(view.Z / TerrainTile.Size + 32));

    /// <summary>
    /// Stand at the region center (default regions have no meaningful center, so the opened tile is
    /// kept) after streaming its tile.
    /// </summary>
    void MoveToSkyboxTarget()
    {
        var target = skyboxTargets[skyboxTarget];
        var key = TileOf(target.Position);
        if (world!.Tiles.Contains(key))
        {
            Teleport(key);
            camera.Position = new Vector3(target.Position.X, camera.Position.Y, target.Position.Z);
            UpdateWanted();
        }
    }

    /// <summary>
    /// Hover above the ground under the camera and look slightly down, so the lower 40% of the frame
    /// is world geometry and the upper part is sky.
    /// </summary>
    void AimSkyboxView()
    {
        var ground = GroundAt(new Vector3(camera.Position.X, 5000, camera.Position.Z));
        float height = ground.Height ?? camera.Position.Y;
        camera.Position = new Vector3(camera.Position.X, height + 25, camera.Position.Z);
        yaw = SkyboxYaws[skyboxView];
        pitch = -0.2f;
        camera.Rotation = new Vector3(pitch, yaw, 0);
    }

    void SetInterfaceVisible(bool visible)
    {
        foreach (var layer in GetChildren().OfType<CanvasLayer>())
        {
            layer.Visible = visible;
        }
    }

    static float Difference(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    // Opaque means the magenta/green background swap moves the pixel by under 2% (glass and cloud
    // layers that let the background through are excluded); a skybox change must exceed 8%.
    const float OpaqueTolerance = 0.02f, ChangeThreshold = 0.08f;

    void CompareSkyboxFrames()
    {
        var shown = GetViewport().GetTexture().GetImage();
        if (skyboxHidden == null || skyboxOtherBackground == null || shown.GetSize() != skyboxHidden.GetSize() || shown.GetSize() != skyboxOtherBackground.GetSize())
        {
            return;
        }

        int width = shown.GetWidth(), height = shown.GetHeight(), changed = 0, total = 0;
        // Failing views get a marked copy: red where the sky changed an opaque world pixel, blue
        // over the rest of the opaque world, to show what leaked.
        var marked = (Image)shown.Duplicate();
        for (int y = 0; y < height; y += 2)
        {
            for (int x = 0; x < width; x += 2)
            {
                // Opaque world: the same color over both backgrounds.
                Color world = skyboxHidden.GetPixel(x, y);
                if (Difference(world, skyboxOtherBackground.GetPixel(x, y)) >= OpaqueTolerance)
                {
                    continue;
                }

                total++;
                if (Difference(world, shown.GetPixel(x, y)) > ChangeThreshold)
                {
                    changed++;
                    marked.SetPixel(x, y, new Color(1, 0, 0));
                }
                else
                {
                    marked.SetPixel(x, y, marked.GetPixel(x, y).Lerp(new Color(0, 0.3f, 1), 0.35f));
                }
            }
        }

        float fraction = total == 0 ? 0 : (float)changed / total;
        string name = SkyboxMaps[skyboxMap];
        // Without an active skybox, or with almost no world on screen, there is nothing to
        // depth-test (region center off the terrain, a nested region winning); report it but do not
        // count it either way. Over world pixels the tolerance only absorbs edge antialiasing.
        bool ready = sky.SkyboxReady && total >= 1000;
        bool pass = fraction < 0.01f;
        if (ready)
        {
            skyboxViews++;
            skyboxFailed |= !pass;
        }

        string verdict = !sky.SkyboxReady ? "skipped-no-skybox" : !ready ? "skipped-no-world" : pass ? "ok" : "FAIL";
        GD.Print(FormattableString.Invariant($"SKYBOX_SMOKE_VIEW map={name} target={skyboxTarget} view={skyboxView} expected={skyboxTargets[skyboxTarget].Skybox} active={sky.SkyboxPath ?? "none"} size={sky.SkyboxBounds?.Size} tile={CameraTile()} worldPixels={total} skyOverWorld={fraction:F4} {verdict}"));
        shown.SavePng(ProjectSettings.GlobalizePath($"res://artifacts/skybox-{name}-{skyboxTarget}-{skyboxView}.png"));
        if (ready && !pass)
        {
            marked.SavePng(ProjectSettings.GlobalizePath($"res://artifacts/skybox-{name}-{skyboxTarget}-{skyboxView}-leak.png"));
            skyboxHidden.SavePng(ProjectSettings.GlobalizePath($"res://artifacts/skybox-{name}-{skyboxTarget}-{skyboxView}-nosky.png"));
        }
    }
}
