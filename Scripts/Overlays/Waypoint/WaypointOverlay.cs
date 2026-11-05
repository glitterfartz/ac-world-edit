using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Bounded projection of waypoint paths (waypoint_data, or waypoint_path_node in TrinityCore) used by the creatures near the streamed region: a
/// direction-shaded line per path (closed back to the first point, as the server repeats the path),
/// a dot per point and, for the emphasized path, numbered labels. Source rows stay detached and
/// read-only; WaypointDraft owns durable local moves. A path is emphasized while one of its points
/// is selected or one of its creatures is selected in the creature layer.
/// </summary>
public partial class WaypointOverlay : RegionOverlay
{
    public WaypointOverlay() : base(true, "Waiting for a world…")
    {
        paths = new WaypointPaths(this);
    }

    const float PickPixels = 12;

    /// <summary>The creature selected in the creature layer; its path is emphasized.</summary>
    public Func<ulong?> FocusedCreature { get; set; } = () => null;
    public IReadOnlyCollection<WaypointPath> Paths => lookup.Values;
    public bool IsLoading => query.IsLoading;
    public event Action? SelectionChanged;
    public event Action<WorldPose>? FocusRequested;

    /// <summary>
    /// Draw every path in the region (true) or only the emphasized one (false).
    /// </summary>
    public bool ShowAll
    {
        get => showAll;
        set => showAll = value;
    }

    bool showAll;
    readonly Dictionary<uint, WaypointPath> lookup = [];
    readonly WaypointPaths paths;
    readonly RegionQuery<WaypointRegionResult> query = new();
    string sourceKey = "";
    (uint Path, uint Point)? selected;
    uint? focusedPath, emphasized;
    ulong? focusedGuid;
    MeshInstance3D handle = null!, walker = null!;

    public override void _Ready()
    {
        handle = paths.Marker(WaypointPaths.SelectedColor, 0.9f);
        walker = paths.Marker(new Color("5bd8ff"), 0.7f);
        OpenDraft();
    }

    protected override void OnRegionChanged(bool mapChanged)
    {
        // Re-center nearest-first ordering on the camera at the next update.
        point = null;
        Restart(mapChanged);
    }

    protected override void Restart(bool hard)
    {
        query.Restart();
        if (hard)
        {
            Deselect();
            paths.Clear();
            lookup.Clear();
            emphasized = focusedPath = null;
            focusedGuid = null;
            StopPreview();
            list?.Clear();
        }

        queryStatus = LayerEnabled ? "Loading waypoint paths…" : "Waypoint layer hidden";
    }

    public void Update(Camera3D camera)
    {
        if (LayerEnabled && region != null)
        {
            point ??= ViewSpace.QueryPointAt(camera.Position, region.Map);
        }

        if (query.TakeSettledSearch())
        {
            PopulateList();
        }

        query.Poll(Publish, error => queryStatus = error);
        if (LayerEnabled && region != null && query.WantsStart)
        {
            var area = region;
            var center = point;
            var config = Connection();
            query.Start(token => DBSource.Open(config).ReadWaypointsAsync(area, center, token));
        }

        UpdateFocus();
        foreach (var (id, view) in paths.Views)
        {
            view.Root.Visible = LayerEnabled && (showAll || id == emphasized);
        }

        UpdateHandle();
        UpdatePreview();
        if (status != null)
        {
            status.Text = queryStatus;
            UpdateInspectorActions();
        }

        var editing = EditableNode();
        gizmo.Update(camera, editing?.GlobalPosition, 0);
    }

    /// <summary>
    /// Follow the creature layer's selection to its path and re-emphasize on any change.
    /// </summary>
    void UpdateFocus()
    {
        ulong? guid = FocusedCreature();
        if (guid != focusedGuid)
        {
            focusedGuid = guid;
            focusedPath = guid.HasValue ? lookup.Values.FirstOrDefault(p => p.Users.Any(u => u.Guid == guid.Value))?.Id : null;
        }

        uint? next = selected?.Path ?? focusedPath;
        if (next == emphasized)
        {
            return;
        }

        uint? previous = emphasized;
        emphasized = next;
        StopPreview();
        foreach (uint? id in new[] { previous, next })
        {
            if (id.HasValue && lookup.TryGetValue(id.Value, out var path))
            {
                BuildPath(path);
            }
        }
    }

    /// <summary>
    /// Replace the source snapshot and reconcile path nodes: unchanged paths keep their nodes.
    /// </summary>
    void Publish(WaypointRegionResult result)
    {
        sourceKey = result.SourceKey;
        var incoming = result.Paths.ToDictionary(p => p.Id);
        foreach (uint id in paths.Views.Keys.Where(id => !incoming.ContainsKey(id)).ToArray())
        {
            if (selected?.Path == id)
            {
                Deselect();
            }

            paths.Remove(id);
        }

        foreach (var path in result.Paths)
        {
            bool unchanged = lookup.TryGetValue(path.Id, out var old) && old.Nodes.SequenceEqual(path.Nodes) && paths.Contains(path.Id);
            lookup[path.Id] = path;
            if (!unchanged)
            {
                BuildPath(path);
            }
        }

        foreach (uint id in lookup.Keys.Where(id => !incoming.ContainsKey(id)).ToArray())
        {
            lookup.Remove(id);
        }

        focusedGuid = null;
        PopulateList();
        int points = result.Paths.Sum(p => p.Nodes.Length);
        queryStatus = result.Paths.Length == 0 ? "No waypoint paths in this region." : result.Truncated ? $"{result.Paths.Length} nearest paths · limit reached" : $"{result.Paths.Length} paths · {points} points in this region";
        if (selected.HasValue)
        {
            ShowSelection();
        }
    }

    static WaypointState SourceState(WaypointNode node) => new(new(node.X, node.Y, node.Z), new(node.Orientation, node.Velocity, node.Delay, node.Smooth, node.MoveType, node.Action, node.ActionChance));

    /// <summary>
    /// The point as the draft leaves it: every editable column, source values where untouched.
    /// </summary>
    WaypointState StateOf(WaypointPath path, WaypointNode node) => draft?.Effective(sourceKey, path.Id, node.Point, SourceState(node)) ?? SourceState(node).ToColumn();

    WaypointPosition PositionOf(WaypointPath path, WaypointNode node) => StateOf(path, node).Position;

    static Vector3 ViewPosition(WaypointPosition p) => ViewSpace.ToView(p.X, p.Y, p.Z);

    /// <summary>
    /// (Re)create a path's nodes from its effective positions. The optional override substitutes a
    /// point's view position while it is being dragged.
    /// </summary>
    void BuildPath(WaypointPath path, (uint Point, Vector3 View)? preview = null) =>
        paths.Rebuild(path, node => StateOf(path, node), path.Id == emphasized, preview);

    /// <summary>
    /// Show the gold handle on the selected point (its node is where the gizmo attaches).
    /// </summary>
    void UpdateHandle()
    {
        var view = SelectedView();
        handle.Visible = LayerEnabled && view.HasValue;
        if (handle.Visible && !gizmo.Dragging)
        {
            handle.Position = view!.Value;
        }
    }

    /// <summary>The handle node when a point is selected and editing is available.</summary>
    Node3D? EditableNode() => draft != null && selected.HasValue && handle.Visible ? handle : null;

    Vector3? SelectedView()
    {
        if (!selected.HasValue || !paths.TryView(selected.Value.Path, out var view) || view == null || !lookup.TryGetValue(selected.Value.Path, out var path))
        {
            return null;
        }

        int index = Array.FindIndex(path.Nodes, n => n.Point == selected.Value.Point);
        return index >= 0 && index < view.Positions.Length ? view.Positions[index] : null;
    }

    public void Select(uint pathId, uint point)
    {
        if (!lookup.TryGetValue(pathId, out var path) || path.Nodes.All(n => n.Point != point))
        {
            return;
        }

        var previous = selected;
        selected = (pathId, point);
        if (previous != selected)
        {
            SelectionChanged?.Invoke();
        }

        handle.Visible = true;
        handle.Position = SelectedView() ?? handle.Position;
        ShowSelection();
    }

    /// <summary>
    /// Select a path as a whole by its first point.
    /// </summary>
    public void SelectPath(uint pathId)
    {
        if (lookup.TryGetValue(pathId, out var path) && path.Nodes.Length > 0)
        {
            Select(pathId, path.Nodes[0].Point);
        }
    }

    public void Deselect()
    {
        gizmo?.Finish(false);
        var previous = selected;
        selected = null;
        if (handle != null)
        {
            handle.Visible = false;
        }

        if (previous.HasValue && lookup.TryGetValue(previous.Value.Path, out var path) && paths.Contains(path.Id))
        {
            BuildPath(path);
        }

        list?.DeselectAll();
        if (title != null)
        {
            ShowSelection();
        }
    }

    /// <summary>
    /// Nearest waypoint dot to the cursor among the drawn paths. The distance is along the pick ray,
    /// so it compares with creature, GameObject and graveyard hits.
    /// </summary>
    public (uint Path, uint Point, float Distance)? FindHit(Camera3D camera, Vector2 screen)
    {
        if (!LayerEnabled)
        {
            return null;
        }

        var ray = PickRay.From(camera, screen);
        (uint, uint, float)? best = null;
        float bestPixels = PickPixels * PickPixels;
        foreach (var (id, view) in paths.Views)
        {
            if (!view.Root.Visible || !lookup.TryGetValue(id, out var path))
            {
                continue;
            }

            for (int i = 0; i < view.Positions.Length && i < path.Nodes.Length; i++)
            {
                var position = view.Positions[i];
                if (camera.IsPositionBehind(position))
                {
                    continue;
                }

                float along = (position - ray.Origin).Dot(ray.Direction);
                if (along < 0 || along > camera.Far)
                {
                    continue;
                }

                float pixels = camera.UnprojectPosition(position).DistanceSquaredTo(screen);
                if (pixels <= bestPixels)
                {
                    bestPixels = pixels;
                    best = (id, path.Nodes[i].Point, along);
                }
            }
        }

        return best is { } hit ? (hit.Item1, hit.Item2, hit.Item3) : null;
    }

    public override void _ExitTree()
    {
        query.Dispose();
        draft?.Dispose();
    }
}
