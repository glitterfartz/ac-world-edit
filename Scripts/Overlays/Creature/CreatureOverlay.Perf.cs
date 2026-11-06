using Godot;

namespace ACWorldEdit;

/// <summary>
/// Opt-in frame-time probe for crowded scenes. Run with <c>--creature-perf</c>, park the camera
/// where it hurts, and every ten seconds the overlay prints medians for each of its own steps
/// plus marker/batch counters and the ground-probe cost. Zero cost without the flag.
/// </summary>
public partial class CreatureOverlay
{
    /// <summary>Set from --creature-perf; enables per-section timing and periodic reports.</summary>
    public bool PerfProbe { get; set; }

    readonly List<double> perfFrame = [], perfQuery = [], perfPrepare = [], perfStatus = [],
        perfBatches = [], perfAppearance = [], perfModels = [], perfBatchLoop = [],
        perfVisibility = [], perfGizmo = [];
    long perfWindowStart, perfFramePrev;
    long perfAppearanceRuns, perfGroundCalls;
    double perfGroundMs;

    static long PerfMark() => System.Diagnostics.Stopwatch.GetTimestamp();

    static void PerfAdd(List<double> list, long start) =>
        list.Add(System.Diagnostics.Stopwatch.GetElapsedTime(start, System.Diagnostics.Stopwatch.GetTimestamp()).TotalMilliseconds);

    static string PerfSummary(List<double> values)
    {
        if (values.Count == 0)
        {
            return "n=0";
        }

        var sorted = values.Order().ToArray();
        double at(double q) => sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * q))];
        return $"n={values.Count} p50={at(0.5):F2} p95={at(0.95):F2} max={sorted[^1]:F2}";
    }

    /// <summary>A timed version of <see cref="Update"/>; the plain path above stays untouched.</summary>
    void UpdateTimed(Camera3D camera)
    {
        long mark = PerfMark();
        UpdateQuery(camera);
        PerfAdd(perfQuery, mark);
        mark = PerfMark();
        PrepareMarkers();
        PerfAdd(perfPrepare, mark);
        mark = PerfMark();
        UpdateStatus();
        PerfAdd(perfStatus, mark);
        mark = PerfMark();
        UpdateModelBatches(camera);
        PerfAdd(perfBatches, mark);
        mark = PerfMark();
        UpdateVisibility(camera);
        PerfAdd(perfVisibility, mark);
        mark = PerfMark();
        UpdateGizmo(camera);
        PerfAdd(perfGizmo, mark);
        long now = PerfMark();
        if (perfFramePrev != 0)
        {
            perfFrame.Add(System.Diagnostics.Stopwatch.GetElapsedTime(perfFramePrev, now).TotalMilliseconds);
        }

        perfFramePrev = now;
        PerfMaybeReport();
    }

    void PerfMaybeReport()
    {
        if (perfWindowStart == 0)
        {
            perfWindowStart = System.Diagnostics.Stopwatch.GetTimestamp();
            return;
        }

        if (System.Diagnostics.Stopwatch.GetElapsedTime(perfWindowStart).TotalSeconds < 10)
        {
            return;
        }

        perfWindowStart = System.Diagnostics.Stopwatch.GetTimestamp();
        int draws = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        int objects = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        GD.Print($"CREATURE_PERF summary fps={Engine.GetFramesPerSecond()} markers={markers.Count} records={records.Count} pending={pendingMarkers.Count} batches={modelView.Batches.Count} admitted={modelView.AdmittedCount} draws={draws} objects={objects} appearanceRuns={perfAppearanceRuns} groundCalls={perfGroundCalls} groundMs={perfGroundMs:F1}");
        GD.Print($"CREATURE_PERF frame: {PerfSummary(perfFrame)}");
        GD.Print($"CREATURE_PERF query: {PerfSummary(perfQuery)} prepare: {PerfSummary(perfPrepare)} status: {PerfSummary(perfStatus)} batches: {PerfSummary(perfBatches)} visibility: {PerfSummary(perfVisibility)} gizmo: {PerfSummary(perfGizmo)}");
        GD.Print($"CREATURE_PERF appearance: {PerfSummary(perfAppearance)} models: {PerfSummary(perfModels)} batchLoop: {PerfSummary(perfBatchLoop)}");
        foreach (var list in new[] { perfFrame, perfQuery, perfPrepare, perfStatus, perfBatches, perfAppearance, perfModels, perfBatchLoop, perfVisibility, perfGizmo })
        {
            list.Clear();
        }

        perfAppearanceRuns = 0;
        perfGroundCalls = 0;
        perfGroundMs = 0;
    }
}
