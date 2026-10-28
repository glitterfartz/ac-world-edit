using System.Diagnostics;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// <c>--perf-smoke</c>: a reproducible Stormwind City streaming benchmark. Teleports into the Trade
/// District on Eastern Kingdoms (map 0), records frame times while the area streams in (arrival), then
/// flies a fixed path through the city at 70 yd/s (flight) and waits for streaming to settle again. Prints
/// <c>PERF_*</c> lines (frame percentiles, per-section main-thread time, tile and database query
/// latencies, model decode counts, GC collections) and quits. Run it windowed: headless uses a dummy
/// renderer, so uploads cost nothing. VSync is turned off so frame times show the real cost.
/// </summary>
public partial class TerrainViewer
{
    bool perfSmoke;
    int perfStage;
    double perfTime;
    long perfFrameStart, perfStageStart;
    // Per-frame section marks inside _Process; only written while perfSmoke is set (no allocation).
    readonly long[] perfMarks = new long[PerfSections + 1];
    const int PerfSections = 7;
    static readonly string[] PerfSectionNames = ["streaming", "ui", "creatures", "gameobjects", "otherOverlays", "models", "rest"];
    readonly List<double> perfFrames = [];
    readonly List<double>[] perfSectionTimes = [.. Enumerable.Range(0, PerfSections).Select(_ => new List<double>())];
    readonly List<double> perfTileMs = [];
    readonly List<double> perfRenderCpu = [], perfRenderGpu = [];
    readonly Dictionary<TileKey, long> perfTileStarted = [];
    int perfGc0, perfGc1, perfGc2;

    // Stormwind City in server coordinates (x, y, z): Trade District, Cathedral Square, the Keep,
    // then out to the main gate. Tiles 29-31, 47-48 (viewer TileKey = ADT column, row).
    static readonly (double X, double Y, double Z)[] PerfPath =
    [
        (-8830, 630, 160), (-8515, 860, 165), (-8440, 330, 170), (-9050, 450, 160)
    ];
    const float PerfSpeed = 70;

    /// <summary>Called at the start and between the sections of _Process while the perf smoke runs.</summary>
    void PerfMark(int index)
    {
        if (perfSmoke)
        {
            perfMarks[index] = Stopwatch.GetTimestamp();
        }
    }

    void PerfTileScheduled(TileKey key)
    {
        if (perfSmoke)
        {
            perfTileStarted[key] = Stopwatch.GetTimestamp();
        }
    }

    void PerfTilePublished(TileKey key)
    {
        if (perfSmoke && perfTileStarted.Remove(key, out long started))
        {
            perfTileMs.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    bool PerfSettled => wanted.All(loaded.ContainsKey) && pending == null && building == null && !models.Busy && !creatures.IsLoading && !gameObjects.IsLoading && !graveyards.IsLoading && !waypoints.IsLoading;

    void PerfSmoke(double delta)
    {
        long now = Stopwatch.GetTimestamp();
        double frameMs = perfFrameStart == 0 ? delta * 1000 : Stopwatch.GetElapsedTime(perfFrameStart, now).TotalMilliseconds;
        perfFrameStart = now;
        perfTime += delta;
        if (perfTime > 400)
        {
            SmokeFail("PERF", $"timeout stage={perfStage} {message} loaded={loaded.Count}/{wanted.Count} modelsBusy={models.Busy}");
            return;
        }

        if (perfStage == 0)
        {
            if (world == null || currentMapId != 0 || busy)
            {
                return;
            }

            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
            var start = PerfPath[0];
            PerfPlaceCamera(ViewSpace.ToView(start.X, start.Y, start.Z), ViewSpace.ToView(PerfPath[1].X, PerfPath[1].Y, PerfPath[1].Z));
            UpdateWanted();
            PerfBegin(1);
            return;
        }

        if (perfStage is 1 or 3)
        {
            PerfRecord(frameMs);
            if (!PerfSettled || Stopwatch.GetElapsedTime(perfStageStart).TotalSeconds < 1)
            {
                return;
            }

            PerfReport(perfStage == 1 ? "ARRIVAL" : "SETTLE");
            if (perfStage == 1)
            {
                PerfBegin(2);
            }
            else
            {
                GD.Print($"PERF_SMOKE_PASSED models={models.ReadyModels} textures={models.TextureCount} cacheMiB={models.CachedBytes / 1048576} objects={objects.ObjectCount} creatures={creatures.Records.Count} gameobjects={gameObjects.Records.Count}");
                GetTree().Quit();
            }

            return;
        }

        // Flight: the camera position follows wall time along the fixed path.
        PerfRecord(frameMs);
        double travelled = Stopwatch.GetElapsedTime(perfStageStart).TotalSeconds * PerfSpeed;
        for (int i = 0; i + 1 < PerfPath.Length; i++)
        {
            var a = ViewSpace.ToView(PerfPath[i].X, PerfPath[i].Y, PerfPath[i].Z);
            var b = ViewSpace.ToView(PerfPath[i + 1].X, PerfPath[i + 1].Y, PerfPath[i + 1].Z);
            float length = a.DistanceTo(b);
            if (travelled <= length)
            {
                PerfPlaceCamera(a.Lerp(b, (float)(travelled / length)), b);
                return;
            }

            travelled -= length;
        }

        PerfReport("FLIGHT");
        PerfBegin(3);
    }

    void PerfPlaceCamera(Vector3 position, Vector3 target)
    {
        cameraFocused = true;
        camera.Position = position;
        var d = target - position;
        yaw = Mathf.Atan2(-d.X, -d.Z);
        pitch = -0.35f;
        camera.Rotation = new(pitch, yaw, 0);
    }

    void PerfBegin(int stage)
    {
        perfStage = stage;
        perfStageStart = Stopwatch.GetTimestamp();
        perfFrames.Clear();
        perfTileMs.Clear();
        perfRenderCpu.Clear();
        perfRenderGpu.Clear();
        foreach (var list in perfSectionTimes)
        {
            list.Clear();
        }

        PerfDiagnostics.Queries.Clear();
        models.ResetStats();
        perfGc0 = GC.CollectionCount(0);
        perfGc1 = GC.CollectionCount(1);
        perfGc2 = GC.CollectionCount(2);
    }

    void PerfRecord(double frameMs)
    {
        perfFrames.Add(frameMs);
        var viewport = GetViewport().GetViewportRid();
        perfRenderCpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport) + RenderingServer.GetFrameSetupTimeCpu());
        perfRenderGpu.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport));
        for (int i = 0; i < PerfSections; i++)
        {
            perfSectionTimes[i].Add(Stopwatch.GetElapsedTime(perfMarks[i], perfMarks[i + 1]).TotalMilliseconds);
        }
    }

    static string Percentiles(List<double> values)
    {
        if (values.Count == 0)
        {
            return "n=0";
        }

        var s = values.Order().ToArray();
        double P(double q) => s[Math.Min(s.Length - 1, (int)(s.Length * q))];
        return $"n={s.Length} mean={values.Average():F2} p50={P(0.5):F2} p95={P(0.95):F2} p99={P(0.99):F2} max={s[^1]:F2}";
    }

    void PerfReport(string phase)
    {
        double seconds = Stopwatch.GetElapsedTime(perfStageStart).TotalSeconds;
        GD.Print($"PERF_{phase} seconds={seconds:F2} frames: {Percentiles(perfFrames)} over33ms={perfFrames.Count(f => f > 33.3)} over50ms={perfFrames.Count(f => f > 50)}");
        for (int i = 0; i < PerfSections; i++)
        {
            GD.Print($"PERF_{phase}_SECTION {PerfSectionNames[i]}: {Percentiles(perfSectionTimes[i])}");
        }

        GD.Print($"PERF_{phase}_RENDER cpu: {Percentiles(perfRenderCpu)}");
        GD.Print($"PERF_{phase}_RENDER gpu: {Percentiles(perfRenderGpu)}");
        GD.Print($"PERF_{phase}_TILES {Percentiles(perfTileMs)}");
        foreach (var group in PerfDiagnostics.Queries.ToArray().GroupBy(q => q.Kind))
        {
            var done = group.Where(q => !q.Cancelled).Select(q => q.Milliseconds).ToList();
            GD.Print($"PERF_{phase}_DB {group.Key} started={group.Count()} cancelled={group.Count(q => q.Cancelled)} completed: {Percentiles(done)}");
        }

        var m = models.Stats;
        GD.Print($"PERF_{phase}_MODELS decodes={m.Decodes} decodeMs={m.DecodeMilliseconds:F0} uploads={m.Uploads} binds={m.Binds} evictions={m.Evictions} ready={models.ReadyModels} cacheMiB={models.CachedBytes / 1048576}");
        GD.Print($"PERF_{phase}_GC gen0={GC.CollectionCount(0) - perfGc0} gen1={GC.CollectionCount(1) - perfGc1} gen2={GC.CollectionCount(2) - perfGc2} objects={Performance.GetMonitor(Performance.Monitor.ObjectCount)} nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)} drawCalls={Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)} videoMiB={Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576:F0}");
    }
}

/// <summary>
/// Opt-in timing sink for <c>--perf-smoke</c>. Null-free and empty unless the smoke enabled it; the
/// region queries add one record per finished task while <see cref="Enabled"/> is set.
/// </summary>
public static class PerfDiagnostics
{
    public static bool Enabled;
    public static readonly System.Collections.Concurrent.ConcurrentQueue<(string Kind, double Milliseconds, bool Cancelled)> Queries = new();
}
