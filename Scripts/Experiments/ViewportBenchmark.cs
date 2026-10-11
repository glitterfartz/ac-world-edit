using System.Diagnostics;
using System.Text.Json;
using Godot;
namespace ACWorldEdit.Experiments;

/// <summary>Opt-in diagnostics. Short/headless runs never satisfy the roadmap GPU or 30-minute gates.</summary>
public sealed class ViewportBenchmark
{
    readonly List<double> stationary = [], streaming = [], uploads = [], teleports = [];
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly double duration;
    double nextTeleport = 20, teleportStarted;
    long peakRam;
    int minimumLoaded = int.MaxValue, maximumLoaded;
    bool waiting = true, started;
    long previousFrame;
    public ViewportBenchmark()
    {
        string? argument = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--benchmark-seconds="));
        duration = argument == null ? 1800 : double.Parse(argument.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture);
        if (!double.IsFinite(duration) || duration < 10 || duration > 3600) throw new ArgumentException("Benchmark duration must be 10..3600 seconds.");
    }
    public void AddProxies(Node3D parent, Vector3 center)
    {
        var mesh = new BoxMesh { Size = new(1, 2, 1) };
        var material = new StandardMaterial3D { AlbedoColor = new("ffb34d") }; mesh.Material = material;
        var group = new Node3D { Name = "Phase0SyntheticProxies" }; parent.AddChild(group);
        for (int i = 0; i < 2000; i++)
            group.AddChild(new MeshInstance3D { Mesh = mesh, Position = center + new Vector3((i % 50 - 25) * 3, 0, (i / 50 - 20) * 3) });
    }
    public void Sample(double delta, double uploadMs, bool settled, int loaded, int wanted, Action teleport, Node treeOwner)
    {
        long currentFrame = Stopwatch.GetTimestamp();
        // Godot delta may be smoothed; capture actual intervals between process callbacks.
        double frameMs = previousFrame == 0 ? 0 : Stopwatch.GetElapsedTime(previousFrame, currentFrame).TotalMilliseconds;
        previousFrame = currentFrame;
        uploads.Add(uploadMs);
        using (var process = Process.GetCurrentProcess()) peakRam = Math.Max(peakRam, process.WorkingSet64);
        maximumLoaded = Math.Max(maximumLoaded, loaded);
        if (settled)
        {
            started = true; minimumLoaded = Math.Min(minimumLoaded, loaded);
            if (waiting) { teleports.Add(clock.Elapsed.TotalSeconds - teleportStarted); waiting = false; }
            if (frameMs > 0) stationary.Add(frameMs);
        }
        else if (frameMs > 0) streaming.Add(frameMs);
        if (started && !waiting && clock.Elapsed.TotalSeconds >= nextTeleport)
        { teleportStarted = clock.Elapsed.TotalSeconds; waiting = true; nextTeleport += 20; teleport(); }
        if (clock.Elapsed.TotalSeconds < duration) return;
        static double? Percentile(List<double> values, double fraction)
        { if (values.Count == 0) return null; var sorted = values.Order().ToArray(); return sorted[(int)Math.Ceiling((sorted.Length - 1) * fraction)]; }
        var report = new
        {
            scope = "Phase 0 viewport diagnostics; synthetic boxes, not terrain-grounded production spawns",
            frameMeasurement = "Stopwatch intervals between process callbacks (not smoothed Godot delta or GPU timer)",
            display = DisplayServer.GetName(), godot = Engine.GetVersionInfo()["string"].AsString(),
            seconds = clock.Elapsed.TotalSeconds, proxies = 2000, radius = 1, wantedTiles = wanted,
            maximumLoadedTiles = maximumLoaded, minimumSettledTiles = minimumLoaded == int.MaxValue ? 0 : minimumLoaded,
            stationaryMedianFrameMs = Percentile(stationary, 0.5), streamingP95FrameMs = Percentile(streaming, 0.95),
            uploadP95Ms = Percentile(uploads.Where(v => v > 0).ToList(), 0.95), maxUploadMs = uploads.Count == 0 ? 0 : uploads.Max(),
            usableSceneSeconds = teleports, peakProcessWorkingSetBytes = peakRam,
            inputP95Ms = (double?)null, peakVramBytes = (long?)null,
            acceptance = "Pending: agreed hardware, input/VRAM measurements, controlled cold/warm runs, matched plugin viewport and 30-minute movement"
        };
        string path = ProjectSettings.GlobalizePath("res://artifacts/phase-0-viewport-benchmark.json");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("PHASE0_BENCHMARK_REPORT " + path); treeOwner.GetTree().Quit(started ? 0 : 1);
    }
}
