using Godot;

namespace ACWorldEdit;

/// <summary>
/// The walk preview: a marker that walks the emphasized path the way the server would.
/// </summary>
public partial class WaypointOverlay
{
    // ---- walk preview ----------------------------------------------------------------------

    bool previewing;
    int segment;
    float traveled;
    double waitLeft;
    ulong lastTicks;

    public bool Previewing => previewing;

    /// <summary>
    /// A path can be previewed when the emphasized path has at least two points.
    /// </summary>
    public bool CanPreview => emphasized.HasValue && lookup.TryGetValue(emphasized.Value, out var previewPath) && DisplayNodes(previewPath).Length > 1;

    /// <summary>
    /// Start or stop a marker that walks the emphasized path the way the server would: leg by leg at
    /// the destination point's walk or run speed, pausing for its delay, and back to the first point
    /// after the last. Default creature speeds; terrain is ignored, as it is on the server.
    /// </summary>
    public void SetPreview(bool on)
    {
        previewing = on && CanPreview;
        segment = 0;
        traveled = 0;
        waitLeft = 0;
        lastTicks = Time.GetTicksMsec();
        walker.Visible = false;
    }

    void StopPreview() => SetPreview(false);

    void UpdatePreview()
    {
        if (!previewing)
        {
            walker.Visible = false;
            return;
        }

        if (!CanPreview || !paths.TryView(emphasized!.Value, out var view) || view == null || !lookup.TryGetValue(emphasized.Value, out var path))
        {
            StopPreview();
            return;
        }

        var display = DisplayNodes(path);
        if (view.Positions.Length != display.Length)
        {
            StopPreview();
            return;
        }

        ulong now = Time.GetTicksMsec();
        double dt = Math.Min((now - lastTicks) / 1000.0, 0.25);
        lastTicks = now;
        int count = display.Length;
        segment %= count;
        int to = (segment + 1) % count;
        walker.Visible = LayerEnabled;
        if (waitLeft > 0)
        {
            waitLeft -= dt;
            walker.Position = view.Positions[segment];
            return;
        }

        float length = view.Positions[segment].DistanceTo(view.Positions[to]);
        var props = StateOf(path, display[to]).Props;
        traveled += (props.Velocity > 0 ? (float)props.Velocity : props.MoveType == 1 ? RunSpeed : WalkSpeed) * (float)dt;
        if (traveled >= length)
        {
            segment = to;
            traveled = 0;
            waitLeft = props.Delay / 1000.0;
            walker.Position = view.Positions[to];
            return;
        }

        walker.Position = view.Positions[segment].Lerp(view.Positions[to], length > 0 ? traveled / length : 1);
    }
}
