using System.Text.Json;
using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// F12: save a screenshot and a JSON report of what the camera sees to
/// <c>artifacts/diagnostics/&lt;timestamp&gt;/</c>, for debugging rendering problems after the fact.
/// </summary>
public partial class TerrainViewer
{
    const float DiagnosticsRadius = 250;
    const int DiagnosticsNearby = 150;
    const int DiagnosticsCandidates = 8;

    static float[] Round(Vector3 v) => [MathF.Round(v.X, 2), MathF.Round(v.Y, 2), MathF.Round(v.Z, 2)];

    object ServerPoint(Vector3 view)
    {
        var s = ViewSpace.ToServer(view);
        return new { x = Math.Round(s.X, 2), y = Math.Round(s.Y, 2), z = Math.Round(s.Z, 2) };
    }

    /// <summary>
    /// Nearest ray hit on loaded terrain, found by marching the ray over the height maps. Null when the
    /// ray leaves the loaded tiles first.
    /// </summary>
    Vector3? TerrainRayHit(PickRay ray)
    {
        for (float t = 0; t < 1500; t += 2)
        {
            var p = ray.Origin + ray.Direction * t;
            if (TerrainHeightAt(p.X, p.Z) is float h && p.Y <= h)
            {
                return new(p.X, h, p.Z);
            }
        }

        return null;
    }

    void CaptureDiagnostics()
    {
        try
        {
            string rel = $"artifacts/diagnostics/{DateTime.Now:yyyyMMdd-HHmmss}";
            string dir = ProjectSettings.GlobalizePath("res://" + rel);
            Directory.CreateDirectory(dir);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, "screenshot.png"));

            // The mouse is hidden while flying, so aim at the screen centre then.
            var size = GetViewport().GetVisibleRect().Size;
            bool centred = capture || Input.MouseMode != Input.MouseModeEnum.Visible;
            var screen = centred ? size / 2 : GetViewport().GetMousePosition();
            var ray = PickRay.From(camera, screen);

            var placed = objects.Placements().Select(p =>
            {
                var origin = p.Transform.Origin;
                var inverse = p.Transform.AffineInverse();
                float? hit = p.Ready ? ScenePicking.BoxHit(p.Bounds, inverse * ray.Origin, inverse.Basis * ray.Direction) : null;
                return (Placed: p, Distance: camera.Position.DistanceTo(origin), Hit: hit, InView: camera.IsPositionInFrustum(origin));
            }).ToList();

            // Bounding boxes overlap and ignore occlusion, so list several hits rather than one.
            var candidates = placed.Where(p => p.Hit is float h && h < ray.Far).OrderBy(p => p.Hit).Take(DiagnosticsCandidates).ToList();
            var nearby = placed.Where(p => p.Distance <= DiagnosticsRadius).OrderBy(p => p.Distance).Take(DiagnosticsNearby).ToList();

            object Entry((WorldObjectScene.Placed Placed, float Distance, float? Hit, bool InView) e) => new
            {
                path = e.Placed.Item.Path,
                id = e.Placed.Item.Id,
                wmo = e.Placed.Item.IsWmo,
                doodadSet = e.Placed.Item.DoodadSet,
                distance = MathF.Round(e.Distance, 1),
                inFrustum = e.InView,
                rayDistance = e.Hit is float h ? MathF.Round(h, 1) : (float?)null,
                scale = MathF.Round(e.Placed.Item.Scale, 3),
                rotationDegrees = new[] { e.Placed.Item.Rotation.X, e.Placed.Item.Rotation.Y, e.Placed.Item.Rotation.Z },
                view = Round(e.Placed.Transform.Origin),
                server = ServerPoint(e.Placed.Transform.Origin),
                modelReady = e.Placed.Ready,
                modelFailed = models.HasFailed(e.Placed.Model),
            };

            var models_ = new Dictionary<string, object?>();
            foreach (var e in candidates.Concat(nearby))
            {
                string path = e.Placed.Item.Path + (e.Placed.Item.IsWmo ? $" (set {e.Placed.Item.DoodadSet})" : "");
                if (!models_.ContainsKey(path))
                {
                    models_[path] = models.ReportFor(e.Placed.Model);
                }
            }

            var terrainHit = TerrainRayHit(ray);
            var report = new
            {
                captured = DateTime.Now.ToString("s"),
                client = new { kind = store?.Kind.ToString(), build = store?.Build },
                map = new { id = currentMapId, name = world?.Name },
                tile = new { x = CameraTile().X, y = CameraTile().Y },
                zone = CurrentLocation().Zone,
                camera = new
                {
                    view = Round(camera.Position),
                    server = ServerPoint(camera.Position),
                    yawDegrees = MathF.Round(Mathf.RadToDeg(yaw), 1),
                    pitchDegrees = MathF.Round(Mathf.RadToDeg(pitch), 1),
                },
                timeOfDay = Math.Round(sky.TimeOfDayHours, 2),
                fps = Engine.GetFramesPerSecond(),
                loadedTiles = loaded.Count,
                objectCount = objects.ObjectCount,
                modelsBusy = models.Busy,
                pick = new
                {
                    screen = new[] { screen.X, screen.Y },
                    usedScreenCentre = centred,
                    terrainHit = terrainHit is Vector3 t ? new { view = Round(t), server = ServerPoint(t) } : null,
                    candidates = candidates.Select(Entry).ToList(),
                },
                nearby = nearby.Select(Entry).ToList(),
                models = models_,
                note = "FileDataID paths ('fdid:N') resolve through the community listfile (id;path) in the user data folder. Pick candidates are bounding-box hits along the ray, nearest first: they ignore occlusion and box slack, so the object you aimed at may be one of several.",
            };
            File.WriteAllText(System.IO.Path.Combine(dir, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never }));

            // Model decode warnings go to the Godot log; keep the tail next to the report.
            string log = System.IO.Path.Combine(OS.GetUserDataDir(), "logs", "godot.log");
            if (File.Exists(log))
            {
                using var stream = new FileStream(log, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                var lines = new Queue<string>();
                while (reader.ReadLine() is { } line)
                {
                    lines.Enqueue(line);
                    if (lines.Count > 400)
                    {
                        lines.Dequeue();
                    }
                }

                File.WriteAllLines(System.IO.Path.Combine(dir, "log-tail.txt"), lines);
            }

            statusLog.Post($"Saved diagnostics to {rel}.");
        }
        catch (Exception e)
        {
            statusLog.Post("Diagnostics failed: " + e.Message);
        }
    }
}
