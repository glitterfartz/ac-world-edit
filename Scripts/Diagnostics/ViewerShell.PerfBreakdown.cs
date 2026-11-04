using System.Diagnostics;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// <c>--perf-breakdown</c>: where a settled Stormwind frame goes. Parks the camera at fixed poses once
/// streaming is idle and, per pose, measures render CPU/GPU time and draw counts with every scene
/// category on, then with it hidden (and, for shadow casters, with shadow casting off), so each
/// category's cost is its delta from the all-on frame. Static poses keep streaming noise out of the
/// numbers; deltas are not additive (hiding a building reveals terrain behind it), which the "none"
/// floor shows. Run it windowed.
/// </summary>
public partial class ViewerShell
{
    bool perfBreakdown, perfBreakdownStarted;

    const int BreakdownWarmup = 20, BreakdownFrames = 90;

    /// <summary>A pose: server-space eye, look-at point and pitch.</summary>
    static readonly (string Name, (double X, double Y, double Z) Eye, (double X, double Y, double Z) Target, float Pitch)[] BreakdownPoses =
    [
        ("TradeDistrict", (-8830, 630, 160), (-8515, 860, 165), -0.35f),
        ("CathedralSquare", (-8515, 860, 165), (-8440, 330, 170), -0.35f),
        ("Keep", (-8440, 330, 170), (-9050, 450, 160), -0.35f),
        ("MainGate", (-9050, 450, 160), (-8830, 630, 160), -0.35f),
        ("Overview", (-8700, 700, 330), (-8515, 860, 165), -0.6f),
    ];

    sealed record Category(string Name, List<Node3D> Nodes);

    void PerfBreakdownTick()
    {
        if (perfBreakdownStarted || world == null || currentMapId != 0 || busy)
        {
            return;
        }

        perfBreakdownStarted = true;
        _ = PerfBreakdownRun();
    }

    async Task NextFrame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    /// <summary>Wait until streaming has been idle for a couple of seconds' worth of frames.</summary>
    async Task WaitSettled()
    {
        long started = Stopwatch.GetTimestamp();
        int idle = 0;
        while (idle < 90)
        {
            await NextFrame();
            idle = PerfSettled ? idle + 1 : 0;
            if (Stopwatch.GetElapsedTime(started).TotalSeconds > 300)
            {
                throw new TimeoutException($"streaming never settled: {message} loaded={loaded.Count}/{wanted.Count}");
            }
        }
    }

    async Task PerfBreakdownRun()
    {
        try
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            var viewport = GetViewport().GetViewportRid();
            RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
            foreach (var pose in BreakdownPoses)
            {
                var eye = ViewSpace.ToView(pose.Eye.X, pose.Eye.Y, pose.Eye.Z);
                PerfPlaceCamera(eye, ViewSpace.ToView(pose.Target.X, pose.Target.Y, pose.Target.Z));
                pitch = pose.Pitch;
                camera.Rotation = new(pitch, yaw, 0);
                UpdateWanted();
                await WaitSettled();
                await PerfBreakdownPose(pose.Name, viewport);
            }

            GD.Print("PERF_BREAKDOWN_PASSED");
            GetTree().Quit();
        }
        catch (Exception e)
        {
            SmokeFail("PERF_BREAKDOWN", e.Message);
        }
    }

    static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    static DirectionalLight3D? FindSun(Node root) => Descendants(root).OfType<DirectionalLight3D>().FirstOrDefault();

    /// <summary>Scene nodes grouped by what they draw. Each node is toggled with <c>Visible</c>.</summary>
    (List<Category> Categories, List<ModelInstance> Wmos) CollectCategories()
    {
        var terrain = new List<Node3D>();
        var liquid = new List<Node3D>();
        foreach (var tile in loaded.Values)
        {
            foreach (var child in tile.GetChildren().OfType<Node3D>())
            {
                (child.Name == "Liquids" ? liquid : terrain).Add(child);
            }
        }

        var wmos = new List<ModelInstance>();
        var shell = new List<Node3D>();
        var embedded = new List<Node3D>();
        var adt = new List<Node3D>();
        foreach (var model in GetChildren().OfType<ModelInstance>())
        {
            if (AssetRef.IsWmo(model.Key.Path))
            {
                wmos.Add(model);
                shell.AddRange(model.GetChildren().OfType<GeometryInstance3D>());
                embedded.AddRange(model.GetChildren().OfType<ModelInstance>());
            }
            else
            {
                adt.Add(model);
            }
        }

        var markers = new List<Node3D>();
        var creatureModels = creatures.GetChildren().OfType<ModelInstance>().Cast<Node3D>().ToList();
        var objectModels = gameObjects.GetChildren().OfType<ModelInstance>().Cast<Node3D>().ToList();
        foreach (var overlay in new Node[] { creatures, gameObjects })
        {
            markers.AddRange(overlay.GetChildren().OfType<Node3D>().Where(n => n is not ModelInstance));
        }

        var skyModels = Descendants(sky).OfType<ModelInstance>().Cast<Node3D>().ToList();
        return ([
            new("terrain", terrain), new("liquid", liquid), new("wmoShell", shell), new("wmoDoodads", embedded),
            new("adtDoodads", adt), new("creatureModels", creatureModels), new("gameObjectModels", objectModels),
            new("markersLabels", markers), new("skybox", skyModels),
        ], wmos);
    }

    static List<GeometryInstance3D> Geometry(IEnumerable<Node3D> nodes) => [.. nodes.SelectMany(n => n is GeometryInstance3D g ? [g] : Descendants(n).OfType<GeometryInstance3D>())];

    static void SetVisible(IEnumerable<Node3D> nodes, bool visible)
    {
        foreach (var n in nodes)
        {
            n.Visible = visible;
        }
    }

    static void SetShadows(IEnumerable<GeometryInstance3D> nodes, bool on)
    {
        foreach (var g in nodes)
        {
            g.CastShadow = on ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }

    static double Median(List<double> values) => values.Count == 0 ? 0 : values.Order().ToArray()[values.Count / 2];

    async Task PerfBreakdownPose(string pose, Rid viewport)
    {
        var (categories, wmos) = CollectCategories();
        var sun = FindSun(sky);
        var byName = categories.ToDictionary(c => c.Name);
        string Counts(Category c)
        {
            var geometry = Geometry(c.Nodes);
            int surfaces = 0;
            long copies = 0;
            foreach (var g in geometry)
            {
                if (g is MeshInstance3D m && m.Mesh != null)
                {
                    surfaces += m.Mesh.GetSurfaceCount();
                    copies++;
                }
                else if (g is MultiMeshInstance3D mm && mm.Multimesh?.Mesh != null)
                {
                    surfaces += mm.Multimesh.Mesh.GetSurfaceCount();
                    copies += mm.Multimesh.InstanceCount;
                }
            }

            return $"nodes={c.Nodes.Count} geometry={geometry.Count} surfaces={surfaces} drawnCopies={copies}";
        }

        GD.Print($"PERFB_POSE {pose} tiles={loaded.Count} models={models.ReadyModels}");
        foreach (var c in categories)
        {
            GD.Print($"PERFB_COUNT {pose} {c.Name} {Counts(c)}");
        }

        // The heaviest buildings, by distinct draws (merged surfaces x placements in the batch).
        var top = wmos.Select(w =>
        {
            int surfaces = w.GetChildren().OfType<GeometryInstance3D>().Sum(g => g is MultiMeshInstance3D { Multimesh.Mesh: { } mesh } ? mesh.GetSurfaceCount() : 0);
            long doodads = Descendants(w).OfType<MultiMeshInstance3D>().Sum(mm => (long)(mm.Multimesh?.InstanceCount ?? 0));
            return (w.Key.Path, Placements: w.Transforms?.Length ?? 1, Surfaces: surfaces, Doodads: doodads);
        }).OrderByDescending(t => (long)t.Surfaces * t.Placements + t.Doodads).Take(8);
        foreach (var t in top)
        {
            GD.Print($"PERFB_WMO {pose} {t.Path} placements={t.Placements} shellSurfaces={t.Surfaces} embeddedDoodadCopies={t.Doodads}");
        }

        var configs = new List<(string Name, Action Apply, Action Revert)>
        {
            ("all", () => { }, () => { }),
            ("none", () => SetVisible(categories.SelectMany(c => c.Nodes), false), () => SetVisible(categories.SelectMany(c => c.Nodes), true)),
        };
        foreach (var c in categories)
        {
            configs.Add(($"hide:{c.Name}", () => SetVisible(c.Nodes, false), () => SetVisible(c.Nodes, true)));
        }

        foreach (string name in new[] { "terrain", "wmoShell", "wmoDoodads", "adtDoodads" })
        {
            var geometry = Geometry(byName[name].Nodes);
            configs.Add(($"noShadow:{name}", () => SetShadows(geometry, false), () => SetShadows(geometry, true)));
        }

        if (sun != null)
        {
            configs.Add(("sunShadowsOff", () => sun.ShadowEnabled = false, () => sun.ShadowEnabled = true));
        }

        configs.Add(("all(again)", () => { }, () => { }));

        foreach (var (name, apply, revert) in configs)
        {
            apply();
            for (int i = 0; i < BreakdownWarmup; i++)
            {
                await NextFrame();
            }

            List<double> frame = [], cpu = [], gpu = [];
            List<double> visDraws = [], shadowDraws = [], visPrims = [], shadowPrims = [], visObjects = [], shadowObjects = [];
            long last = Stopwatch.GetTimestamp();
            for (int i = 0; i < BreakdownFrames; i++)
            {
                await NextFrame();
                long now = Stopwatch.GetTimestamp();
                frame.Add(Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
                last = now;
                cpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport) + RenderingServer.GetFrameSetupTimeCpu());
                gpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport));
                visDraws.Add(RenderingServer.ViewportGetRenderInfo(viewport, RenderingServer.ViewportRenderInfoType.Visible, RenderingServer.ViewportRenderInfo.DrawCallsInFrame));
                shadowDraws.Add(RenderingServer.ViewportGetRenderInfo(viewport, RenderingServer.ViewportRenderInfoType.Shadow, RenderingServer.ViewportRenderInfo.DrawCallsInFrame));
                visPrims.Add(RenderingServer.ViewportGetRenderInfo(viewport, RenderingServer.ViewportRenderInfoType.Visible, RenderingServer.ViewportRenderInfo.PrimitivesInFrame));
                shadowPrims.Add(RenderingServer.ViewportGetRenderInfo(viewport, RenderingServer.ViewportRenderInfoType.Shadow, RenderingServer.ViewportRenderInfo.PrimitivesInFrame));
                visObjects.Add(RenderingServer.ViewportGetRenderInfo(viewport, RenderingServer.ViewportRenderInfoType.Visible, RenderingServer.ViewportRenderInfo.ObjectsInFrame));
                shadowObjects.Add(RenderingServer.ViewportGetRenderInfo(viewport, RenderingServer.ViewportRenderInfoType.Shadow, RenderingServer.ViewportRenderInfo.ObjectsInFrame));
            }

            revert();
            GD.Print($"PERFB {pose} {name} frameMs={Median(frame):F2} cpuMs={Median(cpu):F2} gpuMs={Median(gpu):F2} " +
                $"drawsVis={Median(visDraws):F0} drawsShadow={Median(shadowDraws):F0} primsVis={Median(visPrims):F0} primsShadow={Median(shadowPrims):F0} " +
                $"objectsVis={Median(visObjects):F0} objectsShadow={Median(shadowObjects):F0}");
        }
    }
}
