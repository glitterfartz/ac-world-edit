using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Opt-in regression workflows compiled with the viewer. Flags gate execution; movement checks use isolated drafts and database access remains read-only.
/// </summary>
public partial class TerrainViewer
{
    int waterStage, waterSurfaceCount;
    double waterTime, waterReady;
    readonly List<double> waterFrames = [];
    bool reloadSmoke;
    int reloadStage;
    double reloadTime;

    /// <summary>
    /// Configure opt-in checks from CLI flags. Normal viewer startup never enables them.
    /// </summary>
    void ConfigureSmokeChecks()
    {
        waterInterior = OS.GetCmdlineUserArgs().Contains("--water-interior-smoke");
        waterSmoke = OS.GetCmdlineUserArgs().Contains("--water-smoke") || waterInterior;
        smoke = OS.GetCmdlineUserArgs().Contains("--terrain-smoke");
        creatureSmoke = OS.GetCmdlineUserArgs().Contains("--creature-smoke");
        visualSmoke = OS.GetCmdlineUserArgs().Contains("--visual-smoke");
        creatureSmoke |= visualSmoke;
        gizmoSmoke = OS.GetCmdlineUserArgs().Contains("--gizmo-smoke");
        creatureSmoke |= gizmoSmoke;
        reloadSmoke = OS.GetCmdlineUserArgs().Contains("--reload-smoke");
        visibilitySmoke = OS.GetCmdlineUserArgs().Contains("--visibility-smoke");
        skyboxSmoke = OS.GetCmdlineUserArgs().Contains("--skybox-smoke");
    }

    /// <summary>
    /// Reload real archives while a native model is partially uploaded, then verify native scenery
    /// finishes loading again. Exercises the same lifecycle as File → Load archives. A diagnostic
    /// upload hold guarantees the partial state on any machine; it is released once the reload has
    /// discarded the held entry, so the replacement loads at normal speed.
    /// </summary>
    void ReloadSmoke(double delta)
    {
        reloadTime += delta;
        if (reloadTime > 120)
        {
            GD.PushError("RELOAD_SMOKE_TIMEOUT " + message);
            GetTree().Quit(1);
            return;
        }

        if (reloadStage == 0)
        {
            models.HoldPartialUploads = true;
        }
        else if (models.HoldPartialUploads && !models.HasPartialUpload)
        {
            // The reload (or its instance reset) disposed the held entry; let the new store load.
            models.HoldPartialUploads = false;
            GD.Print("RELOAD_SMOKE_PARTIAL_UPLOAD_DISCARDED");
        }

        if (busy || world == null)
        {
            return;
        }

        if (reloadStage == 0)
        {
            // Textures and surfaces exist before publication: this interrupts the GPU upload slot
            // rather than only reloading an empty store or a fully ready model cache.
            if (!models.HasPartialUpload || models.ReadyModels != 0 || models.TextureCount == 0)
            {
                return;
            }

            reloadStage = 1;
            GD.Print($"RELOAD_SMOKE_INTERRUPTED_PARTIAL_UPLOAD textures={models.TextureCount}");
            _ = LoadArchives();
            return;
        }

        if (models.HoldPartialUploads || loaded.Count == 0 || loaded.Count != wanted.Count || pending != null || building != null || models.Busy)
        {
            return;
        }

        if (currentMapId != 0 || models.ReadyModels == 0 || models.TextureCount == 0 || models.ActiveFailedModels != 0)
        {
            GD.PushError("RELOAD_SMOKE_FAILED");
            GetTree().Quit(1);
            return;
        }

        GD.Print($"RELOAD_SMOKE_PASSED tiles={loaded.Count} models={models.ReadyModels} textures={models.TextureCount}");
        GetTree().Quit();
    }

    bool smoke;
    bool waterSmoke, waterInterior;
    bool creatureSmoke;
    bool visualSmoke;
    bool gizmoSmoke, gizmoVerified;
    int shortcutStage;
    double? shortcutHeading;
    double creatureSmokeTime;
    int creatureSmokeStage;
    double creatureSelectedAt;
    double interiorReadyAt;
    readonly List<double> visualFrameTimes = [];
    int animationSmokeStage;
    double animationReadyAt;
    int smokeStage;
    double smokeTime;
    TileKey smokeOriginal;
    int smokeSettle;
    /// <summary>
    /// Opt-in streaming regression: settle, move far enough to unload, return and check stale
    /// ownership. Headless checks lifecycle; graphical mode saves a preview.
    /// </summary>
    void Smoke(double delta)
    {
        smokeTime += delta;
        if (smokeTime > 120)
        {
            GD.PushError("TERRAIN_SMOKE_TIMEOUT " + message);
            GetTree().Quit(1);
            return;
        }

        if (world == null || busy)
        {
            return;
        }

        if (loaded.Count != wanted.Count || loaded.Count == 0 || pending != null || building != null)
        {
            smokeSettle = 0;
            return;
        }

        if (++smokeSettle < 3)
        {
            return;
        }

        if (smokeStage == 0)
        {
            System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://artifacts"));
            if (DisplayServer.GetName() != "headless")
            {
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/terrain-preview.png"));
            }

            GD.Print($"TERRAIN_SMOKE_INITIAL tiles={loaded.Count} textures={renderer.TextureCount}");
            var destination = world.Tiles.Where(k => Math.Abs(k.X - smokeOriginal.X) >= 4).OrderBy(k => Math.Abs(k.X - smokeOriginal.X) + Math.Abs(k.Y - smokeOriginal.Y)).First();
            Teleport(destination);
            smokeStage = 1;
            smokeSettle = 0;
        }
        else if (smokeStage == 1 && loaded.Count == wanted.Count && loaded.Count > 0 && pending == null && building == null)
        {
            if (loaded.Keys.Any(k => !wanted.Contains(k)) || loaded.ContainsKey(smokeOriginal))
            {
                GD.PushError("Stale streamed tiles");
                GetTree().Quit(1);
                return;
            }

            GD.Print($"TERRAIN_SMOKE_MOVED position={CameraTile()} tiles={loaded.Count} textures={renderer.TextureCount}");
            Teleport(smokeOriginal);
            smokeStage = 2;
            smokeSettle = 0;
        }
        else if (smokeStage == 2)
        {
            GD.Print($"TERRAIN_SMOKE_PASSED returned={CameraTile()} tiles={loaded.Count} textures={renderer.TextureCount}");
            GetTree().Quit();
            smokeStage = 3;
        }
    }

    /// <summary>
    /// Opt-in native liquid check: choose actual heights/placements, await loading and capture
    /// animated views with short scene-specific timings.
    /// </summary>
    void WaterSmoke(double delta)
    {
        waterTime += delta;
        if (waterTime > 180)
        {
            GD.PushError("WATER_SMOKE_TIMEOUT " + message);
            GetTree().Quit(1);
            return;
        }

        if (world == null || store == null || busy || pending != null || building != null || loaded.Count != wanted.Count)
        {
            return;
        }

        if (waterStage == 0)
        {
            Vector3 target;
            if (waterInterior)
            {
                var obj = world.GlobalObjects.First();
                var surfaces = models.LiquidSurfaces(new ModelKey(obj.Path, DoodadSet: obj.DoodadSet));
                if (surfaces.Length == 0)
                {
                    return;
                }

                var native = surfaces.OrderByDescending(s => s.Indices.Length).First();
                var center = native.Positions.Aggregate(System.Numerics.Vector3.Zero, (a, b) => a + b) / native.Positions.Length;
                waterSurfaceCount = surfaces.Length;
                target = ModelLibrary.Placement(obj) * new Vector3(center.X, center.Y, center.Z);
                camera.Position = target + new Vector3(6, 3, 6);
                camera.LookAt(target);
                UpdateWanted();
                waterStage = 1;
                GD.Print($"WATER_INTERIOR_NATIVE map={currentMapId} surfaces={surfaces.Length} type={native.LiquidType} center={target}");
                return;
            }

            var key = CameraTile();
            var liquid = LiquidData.Terrain(store.Read(world.TilePath(key))).OrderByDescending(s => s.Indices.Length * s.Colors!.Max(c => c.W)).First();
            var lakeCenter = liquid.Positions.Aggregate(System.Numerics.Vector3.Zero, (a, b) => a + b) / liquid.Positions.Length;
            target = new((key.X - 32) * TerrainTile.Size + lakeCenter.X, lakeCenter.Y, (key.Y - 32) * TerrainTile.Size + lakeCenter.Z);
            camera.Position = target + new Vector3(0, 30, 25);
            camera.LookAt(target);
            UpdateWanted();
            waterStage = 1;
            GD.Print($"WATER_SMOKE_NATIVE tile={key} type={liquid.LiquidType} height={lakeCenter.Y} cells={liquid.Indices.Length / 6} alpha={liquid.Colors!.Min(c => c.W)}/{liquid.Colors!.Average(c => c.W)}/{liquid.Colors!.Max(c => c.W)} center={lakeCenter}");
            return;
        }

        if (models.Busy)
        {
            return;
        }

        if (waterReady == 0)
        {
            waterReady = waterTime;
            return;
        }

        if (waterTime - waterReady < 1)
        {
            return;
        }

        if (waterStage == 1)
        {
            if (DisplayServer.GetName() != "headless")
            {
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath(waterInterior ? "res://artifacts/water-interior.png" : "res://artifacts/water-preview.png"));
            }

            waterStage = 2;
            waterReady = waterTime;
            return;
        }

        if (!waterInterior)
        {
            waterSurfaceCount = renderer.LiquidSurfaceCount;
        }

        if (waterSurfaceCount == 0)
        {
            GD.PushError("WATER_SMOKE_NO_SURFACES");
            GetTree().Quit(1);
            return;
        }

        waterFrames.Add(delta * 1000);
        if (waterTime - waterReady < 4)
        {
            return;
        }

        if (DisplayServer.GetName() != "headless")
        {
            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath(waterInterior ? "res://artifacts/water-interior-animated.png" : "res://artifacts/water-animated.png"));
        }

        var times = waterFrames.Order().ToArray();
        GD.Print($"WATER_SMOKE_PASSED interior={waterInterior} surfaces={waterSurfaceCount} frames={times.Length} p50={times[times.Length / 2]:F2}ms p95={times[(int)(times.Length * .95)]:F2}ms");
        GetTree().Quit();
    }

    /// <summary>
    /// Opt-in read-only selection/map-cancellation check, extended in graphical mode to animation
    /// menus, isolated draft gizmos and an interior.
    /// </summary>
    void CreatureSmoke(double delta)
    {
        creatureSmokeTime += delta;
        if (creatureSmokeTime > (visualSmoke ? 180 : 60))
        {
            GD.PushError("CREATURE_SMOKE_TIMEOUT " + creatures.QueryStatus);
            GetTree().Quit(1);
            return;
        }

        if (busy || world == null || (creatureSmokeStage < 3 && (creatures.IsLoading || creatures.Records.Count == 0)))
        {
            return;
        }

        if (creatures.Records.Any(s => s.Pose.Map != currentMapId))
        {
            GD.PushError("CREATURE_SMOKE_STALE_MAP");
            GetTree().Quit(1);
            return;
        }

        if (creatureSmokeStage == 0)
        {
            if (loaded.Count != wanted.Count || pending != null || building != null)
            {
                return;
            }

            var centerTile = CameraTile();
            var spawn = creatures.Records.Where(s => ACWorldEdit.Foundation.WorldCoordinates.Tile(new(s.Pose.X, s.Pose.Y, s.Pose.Z)) == (centerTile.X, centerTile.Y)).OrderBy(s => creatures.PositionOf(s.Guid).DistanceSquaredTo(camera.Position)).First();
            var position = creatures.PositionOf(spawn.Guid);
            camera.Position = position + new Vector3(0, 8, 15);
            camera.LookAt(position + Vector3.Up);
            if (!creatures.Pick(camera, camera.UnprojectPosition(position + Vector3.Up)) || creatures.SelectedGuid != spawn.Guid || creatures.SelectedListGuid != spawn.Guid)
            {
                GD.PushError("CREATURE_SMOKE_PICK_FAILED");
                GetTree().Quit(1);
                return;
            }

            creatures.Select(spawn.Guid);
            GD.Print($"CREATURE_SMOKE_SELECTED count={creatures.Records.Count} guid={spawn.Guid} map={spawn.Pose.Map}");
            creatureSelectedAt = creatureSmokeTime;
            creatureSmokeStage = 1;
        }
        else if (creatureSmokeStage == 1)
        {
            if (creatureSmokeTime - creatureSelectedAt < 1)
            {
                return;
            }

            if (visualSmoke && (models.Busy || creatures.ModelCreatureCount == 0))
            {
                return;
            }

            if ((visualSmoke || gizmoSmoke) && !gizmoVerified)
            {
                if (!creatures.SmokeGizmo(camera))
                {
                    GD.PushError("GIZMO_SMOKE_FAILED");
                    GetTree().Quit(1);
                    return;
                }

                GD.Print("GIZMO_SMOKE_PASSED drag/rotate/ground/model-position/reopen/undo/redo");
                if (creatures.SmokeLabels() is not { } labels || labels.Checked == 0)
                {
                    GD.PushError("LABEL_SMOKE_FAILED");
                    GetTree().Quit(1);
                    return;
                }

                GD.Print(FormattableString.Invariant($"LABEL_SMOKE_PASSED creatures={labels.Checked} tallestModel={labels.Tallest:F2}yd"));
                if (!WmoGroundSmoke())
                {
                    GetTree().Quit(1);
                    return;
                }

                gizmoVerified = true;
                creatureSelectedAt = creatureSmokeTime;
                return;
            }

            if ((visualSmoke || gizmoSmoke) && shortcutStage < 4)
            {
                if (!ShortcutSmoke())
                {
                    GetTree().Quit(1);
                }

                return;
            }

            if (visualSmoke)
            {
                if (animationSmokeStage == 0)
                {
                    if (!creatures.ChooseAnimationForSmoke(4))
                    {
                        GD.PushError("ANIMATION_SMOKE_NO_WALK");
                        GetTree().Quit(1);
                        return;
                    }

                    animationSmokeStage = 1;
                    return;
                }

                if (animationSmokeStage == 1)
                {
                    if (!creatures.PreviewAnimationReady)
                    {
                        return;
                    }

                    if (animationReadyAt == 0)
                    {
                        animationReadyAt = creatureSmokeTime;
                        return;
                    }

                    if (creatureSmokeTime - animationReadyAt < 0.5)
                    {
                        return;
                    }

                    if (DisplayServer.GetName() != "headless")
                    {
                        GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/walk-preview.png"));
                    }

                    if (!creatures.ChooseAnimationForSmoke(0))
                    {
                        GetTree().Quit(1);
                        return;
                    }

                    animationSmokeStage = 2;
                    return;
                }

                if (animationSmokeStage == 2)
                {
                    if (!creatures.PreviewAnimationReady)
                    {
                        return;
                    }

                    GD.Print("NATIVE_ANIMATION_SMOKE_PASSED selected-model Walk/Stand UI and native clip loading");
                    animationSmokeStage = 3;
                    creatureSelectedAt = creatureSmokeTime;
                    return;
                }

                visualFrameTimes.Add(delta * 1000);
                if (creatureSmokeTime - creatureSelectedAt < 4)
                {
                    return;
                }

                var samples = visualFrameTimes.Order().ToArray();
                GD.Print($"VISUAL_FRAME_TIMES frames={samples.Length} p50Ms={samples[samples.Length / 2]:F3} p95Ms={samples[(int)(samples.Length * 0.95)]:F3}");
            }

            if (DisplayServer.GetName() != "headless")
            {
                System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://artifacts"));
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath(visualSmoke ? "res://artifacts/native-preview.png" : "res://artifacts/creature-preview.png"));
            }

            if (visualSmoke)
            {
                GD.Print($"VISUAL_SMOKE models={models.ReadyModels} creatures={creatures.ModelCreatureCount} objects={objects.ObjectCount} textures={models.TextureCount} cacheMiB={models.CachedBytes / 1048576} uploadMs={models.UploadMilliseconds:F3} fps={Engine.GetFramesPerSecond()}");
            }

            creatures.Refresh();
            creatures.Update(camera); // Start a query, then replace its map immediately.
            int nextMap = mapList.FindIndex(m => m.Directory == "Kalimdor");
            if (nextMap < 0)
            {
                GetTree().Quit(1);
                return;
            }

            maps.Select(nextMap);
            tileX.Value = 32;
            tileY.Value = 32;
            creatureSmokeStage = 2;
            _ = OpenMap();
        }
        else if (creatureSmokeStage == 2)
        {
            if (currentMapId != 1 || creatures.Records.Any(s => s.Pose.Map != 1))
            {
                GD.PushError("CREATURE_SMOKE_MAP_SWITCH_FAILED");
                GetTree().Quit(1);
                return;
            }

            GD.Print($"CREATURE_SMOKE_PASSED map-switch/cancel count={creatures.Records.Count}");
            if (!visualSmoke)
            {
                GetTree().Quit();
                return;
            }

            int interior = mapList.FindIndex(m => m.Directory == "StormwindJail");
            if (interior < 0)
            {
                GetTree().Quit(1);
                return;
            }

            maps.Select(interior);
            creatureSmokeStage = 3;
            _ = OpenMap();
        }
        else
        {
            if (models.Busy)
            {
                return;
            }

            if (world.Tiles.Count != 0 || world.GlobalObjects.Count == 0 || models.ActiveFailedModels > 0)
            {
                GD.PushError("INTERIOR_SMOKE_FAILED");
                GetTree().Quit(1);
                return;
            }

            if (interiorReadyAt == 0)
            {
                interiorReadyAt = creatureSmokeTime;
                return;
            }

            if (creatureSmokeTime - interiorReadyAt < 1)
            {
                return;
            }

            if (DisplayServer.GetName() != "headless")
            {
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/interior-preview.png"));
            }

            GD.Print($"INTERIOR_SMOKE_PASSED map={currentMapId} objects={objects.ObjectCount} models={models.ReadyModels} fps={Engine.GetFramesPerSecond()}");
            GetTree().Quit();
        }
    }

    /// <summary>
    /// Probe from above the nearest ready building with bounds: the chosen surface must come from
    /// the WMO (above the terrain beneath it), and the probe cost is reported. Skips, rather than
    /// fails, when no building near the smoke location has finished loading.
    /// </summary>
    bool WmoGroundSmoke()
    {
        // Bounds centers can fall in courtyards, so try a few of the nearest ready buildings.
        var buildings = objects.ReadyWmoPlacements()
            .Where(item => item.BoundsMinimum.HasValue && item.BoundsMaximum.HasValue)
            .OrderBy(item => new Vector3(item.Position.X, item.Position.Y, item.Position.Z).DistanceSquaredTo(camera.Position))
            .Take(5)
            .ToArray();
        if (buildings.Length == 0)
        {
            GD.Print("WMO_GROUND_SMOKE_SKIPPED no ready building with bounds nearby");
            return true;
        }

        foreach (var building in buildings)
        {
            var min = building.BoundsMinimum!.Value;
            var max = building.BoundsMaximum!.Value;
            var top = new Vector3((min.X + max.X) / 2, Math.Max(min.Y, max.Y) + 50, (min.Z + max.Z) / 2);
            var hit = GroundAt(top);
            var key = new TileKey((int)Math.Floor(top.X / TerrainTile.Size + 32), (int)Math.Floor(top.Z / TerrainTile.Size + 32));
            float? terrain = terrainHeights.TryGetValue(key, out var tile) ? tile.Probe(top.X - (key.X - 32) * TerrainTile.Size, top.Z - (key.Y - 32) * TerrainTile.Size) : null;
            var (floors, _) = objects.FloorHeights(top);
            GD.Print(FormattableString.Invariant($"WMO_GROUND_SMOKE path={building.Path} wmoHits={floors.Count} chosen={hit.Height:F3} terrain={terrain:F3} ms={hit.Milliseconds:F3} via={hit.Message}"));
            if (floors.Count > 0 && hit.Height is float chosen && (terrain is not float t || chosen > t + 0.5f))
            {
                GD.Print("WMO_GROUND_SMOKE_PASSED building surface chosen over terrain");
                return true;
            }
        }

        GD.PushError("WMO_GROUND_SMOKE_FAILED no building surface chosen");
        return false;
    }

    /// <summary>
    /// Send real key events through Godot's input pipeline, one stage per frame: Ctrl+Z must reach
    /// the Edit menu accelerator and undo a heading change, and E/Q/W must reach the toolbar mode
    /// buttons. Proves menu and toolbar shortcuts, not just direct method calls.
    /// </summary>
    bool ShortcutSmoke()
    {
        static void Press(Key key, bool ctrl = false)
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, CtrlPressed = ctrl, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, CtrlPressed = ctrl, Pressed = false });
        }

        switch (shortcutStage)
        {
            case 0:
                shortcutHeading = creatures.SmokeNudgeHeading();
                if (shortcutHeading == null)
                {
                    GD.PushError("SHORTCUT_SMOKE_FAILED could not record a heading change");
                    return false;
                }

                Press(Key.Z, ctrl: true);
                Press(Key.E);
                break;
            case 1:
                if (creatures.SmokeSelectedHeading() != shortcutHeading || creatures.Mode != GizmoMode.Rotate)
                {
                    GD.PushError($"SHORTCUT_SMOKE_FAILED ctrl+z/E heading={creatures.SmokeSelectedHeading()} expected={shortcutHeading} mode={creatures.Mode}");
                    return false;
                }

                Press(Key.Q);
                break;
            case 2:
                if (creatures.Mode != GizmoMode.Select)
                {
                    GD.PushError("SHORTCUT_SMOKE_FAILED Q mode=" + creatures.Mode);
                    return false;
                }

                Press(Key.W);
                break;
            case 3:
                if (creatures.Mode != GizmoMode.Move)
                {
                    GD.PushError("SHORTCUT_SMOKE_FAILED W mode=" + creatures.Mode);
                    return false;
                }

                GD.Print("SHORTCUT_SMOKE_PASSED ctrl+z menu undo, E/Q/W toolbar modes");
                break;
        }

        shortcutStage++;
        return true;
    }
}
