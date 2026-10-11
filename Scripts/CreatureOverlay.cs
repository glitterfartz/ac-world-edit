using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
namespace ACWorldEdit;

/// <summary>Read-only, transient projection of nearby database spawns.</summary>
public partial class CreatureOverlay : Node3D
{
    readonly Dictionary<uint, (CreatureSpawn Spawn, Node3D Node, MeshInstance3D Body, Label3D Label)> markers = [];
    readonly StandardMaterial3D normal = new() { AlbedoColor = new("5bd8c2") };
    readonly StandardMaterial3D selected = new() { AlbedoColor = new("ffcf69"), EmissionEnabled = true, Emission = new("805510") };
    readonly CapsuleMesh bodyMesh = new() { Radius = 0.55f, Height = 2 };
    readonly BoxMesh headingMesh = new() { Size = new(0.2f, 0.2f, 1.4f) };
    ItemList list = null!;
    Label details = null!, status = null!;
    LineEdit config = null!;
    LineEdit filter = null!;
    CancellationTokenSource cancellation = new();
    CreatureRegion? region;
    Task<CreatureRegionResult>? pending;
    int generation, pendingGeneration;
    IReadOnlyList<CreatureSpawn> records = [];
    uint? selectedGuid;
    int nextMarker;
    bool truncated;
    string queryStatus = "Waiting for an outdoor map.";
    public uint? SelectedGuid => selectedGuid;
    public IReadOnlyList<CreatureSpawn> Records => records;
    public bool IsLoading => pending != null || nextMarker < records.Count;
    public uint? SelectedListGuid
    {
        get { var indices = list.GetSelectedItems(); return indices.Length == 0 ? null : uint.Parse(list.GetItemMetadata(indices[0]).AsString()); }
    }
    public string QueryStatus => queryStatus;
    public event Action<CreatureSpawn>? FocusRequested;

    public Control BuildPanel()
    {
        var panel = new PanelContainer { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -380, OffsetRight = -20, OffsetTop = 20 };
        var box = new VBoxContainer { CustomMinimumSize = new(340, 0) }; panel.AddChild(box);
        box.AddChild(new Label { Text = "NEARBY CREATURES · READ ONLY" });
        config = new LineEdit { Text = System.Environment.GetEnvironmentVariable("ACWORLD_SERVER_CONFIG") ?? CreatureSource.DefaultConfigPath,
            PlaceholderText = "worldserver.conf path", TooltipText = "Read WorldDatabaseInfo from this external file; credentials stay out of the project." };
        box.AddChild(config);
        var refresh = new Button { Text = "Refresh creatures" }; refresh.Pressed += Refresh; box.AddChild(refresh);
        var focus = new Button { Text = "Fly to selected creature" }; focus.Pressed += () =>
        { if (selectedGuid.HasValue && markers.TryGetValue(selectedGuid.Value, out var marker)) FocusRequested?.Invoke(marker.Spawn); };
        box.AddChild(focus);
        filter = new LineEdit { PlaceholderText = "Filter loaded creatures by name / GUID / template" }; box.AddChild(filter);
        filter.TextChanged += _ => PopulateList();
        list = new ItemList { CustomMinimumSize = new(340, 190) }; box.AddChild(list);
        list.ItemSelected += index => Select(uint.Parse(list.GetItemMetadata((int)index).AsString()));
        status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(340, 40) }; box.AddChild(status);
        details = new Label { Text = "Click a creature marker or choose one above.", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new(340, 90) }; box.AddChild(details);
        return panel;
    }
    public void SetRegion(CreatureRegion? next)
    {
        if (region == next) return;
        bool preserveSelection = next != null && next.Map == region?.Map;
        region = next; Restart(preserveSelection);
    }
    public void Refresh() => Restart();
    void Restart(bool preserveSelection = true)
    {
        generation++; cancellation.Cancel(); cancellation.Dispose(); cancellation = new();
        Clear(preserveSelection);
        queryStatus = region == null ? "Waiting for an outdoor map." : "Loading nearby creatures…";
        // Keep at most one active query. Its cancellation completes before starting the replacement.
    }
    void Clear(bool preserveSelection)
    {
        foreach (var marker in markers.Values) { marker.Node.Visible = false; marker.Node.QueueFree(); }
        markers.Clear(); records = []; nextMarker = 0;
        if (!preserveSelection) selectedGuid = null;
        list?.Clear(); if (!selectedGuid.HasValue && details != null) details.Text = "Click a creature marker or choose one above.";
    }
    int requestedGeneration = -1;
    public void Update(Camera3D camera)
    {
        if (pending is { IsCompleted: true })
        {
            try
            {
                var result = pending.GetAwaiter().GetResult();
                if (pendingGeneration == generation)
                {
                    records = result.Spawns;
                    truncated = result.Truncated; nextMarker = 0;
                    PopulateList();
                    queryStatus = records.Count == 0 ? "No creatures in this region." : "Preparing creature markers…";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (pendingGeneration == generation) queryStatus = CreatureSource.SafeError(error); }
            pending = null;
        }
        if (requestedGeneration != generation && pending == null && region != null)
        {
            requestedGeneration = generation; pendingGeneration = generation;
            var next = region; string path = config.Text; var token = cancellation.Token;
            pending = Task.Run(async () => await new CreatureSource(path).ReadAsync(next, token), token);
        }
        if (nextMarker < records.Count)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < 64 && nextMarker < records.Count &&
                (i == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds < 2); i++, nextMarker++)
                AddMarker(records[nextMarker]);
            if (nextMarker == records.Count)
            {
                if (selectedGuid.HasValue)
                {
                    if (markers.ContainsKey(selectedGuid.Value)) Select(selectedGuid.Value);
                    else { selectedGuid = null; details.Text = "Selected creature is outside the current region."; }
                }
                queryStatus = truncated ? $"Showing first {records.Count} creatures. Reduce nearby tile radius for dense areas." : $"{records.Count} creatures loaded. Click a marker to select.";
            }
            else queryStatus = $"Preparing markers {nextMarker}/{records.Count}…";
        }
        if (status != null) status.Text = queryStatus;
        foreach (var marker in markers.Values)
            marker.Label.Visible = selectedGuid == marker.Spawn.Guid || camera.GlobalPosition.DistanceSquaredTo(marker.Node.GlobalPosition) < 120 * 120;
    }
    void AddMarker(CreatureSpawn spawn)
    {
        var position = WorldCoordinates.ToView(new(spawn.Pose.X, spawn.Pose.Y, spawn.Pose.Z));
        var node = new Node3D { Name = "Creature_" + spawn.Guid, Position = new((float)position.X, (float)position.Y, (float)position.Z), Rotation = new(0, (float)spawn.Pose.Heading, 0) };
        var body = new MeshInstance3D { Mesh = bodyMesh, MaterialOverride = normal, Position = new(0, 1, 0) }; node.AddChild(body);
        node.AddChild(new MeshInstance3D { Mesh = headingMesh, MaterialOverride = normal, Position = new(0, 1, -0.7f) });
        var label = new Label3D { Text = $"{spawn.Name}\n#{spawn.Guid}", Position = new(0, 3, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 32, PixelSize = 0.018f };
        node.AddChild(label); AddChild(node); markers.Add(spawn.Guid, (spawn, node, body, label));
    }
    void PopulateList()
    {
        list.Clear(); string term = filter.Text.Trim();
        foreach (var spawn in records.Where(s => $"{s.Name} {s.Guid} {s.TemplateId}".Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            int index = list.AddItem($"{spawn.Name} · #{spawn.Guid} · template {spawn.TemplateId}");
            list.SetItemMetadata(index, spawn.Guid.ToString()); if (selectedGuid == spawn.Guid) list.Select(index);
        }
    }
    public void Select(uint guid)
    {
        if (!markers.TryGetValue(guid, out var marker)) return;
        if (selectedGuid.HasValue && markers.TryGetValue(selectedGuid.Value, out var previous)) previous.Body.MaterialOverride = normal;
        selectedGuid = guid; marker.Body.MaterialOverride = selected;
        var spawn = marker.Spawn;
        details.Text = FormattableString.Invariant($"{spawn.Name}\nGUID {spawn.Guid} · template {spawn.TemplateId} · map {spawn.Pose.Map}\nX {spawn.Pose.X:F3}  Y {spawn.Pose.Y:F3}  Z {spawn.Pose.Z:F3}\nHeading {spawn.Pose.Heading:F4} rad\nSpawn mask {spawn.SpawnMask} · phase mask {spawn.PhaseMask}");
        list.DeselectAll();
        for (int i = 0; i < list.ItemCount; i++)
            if (list.GetItemMetadata(i).AsString() == guid.ToString())
            { list.Select(i); list.CallDeferred(ItemList.MethodName.EnsureCurrentIsVisible); break; }
    }
    public bool Pick(Camera3D camera, Vector2 screen)
    {
        Vector3 origin = camera.ProjectRayOrigin(screen), direction = camera.ProjectRayNormal(screen);
        uint? nearest = null; float distance = float.MaxValue;
        foreach (var marker in markers.Values)
        {
            Vector3 center = marker.Node.GlobalPosition + Vector3.Up;
            if (camera.IsPositionBehind(center)) continue;
            var offset = center - origin; float along = offset.Dot(direction);
            if (along < 0 || along > camera.Far || along > distance) continue;
            // Small screen-space target remains selectable at a distance; only markers near the click qualify.
            if (camera.UnprojectPosition(center).DistanceSquaredTo(screen) > 12 * 12) continue;
            nearest = marker.Spawn.Guid; distance = along;
        }
        if (!nearest.HasValue) return false; Select(nearest.Value); return true;
    }
    public Vector3 PositionOf(uint guid) => markers[guid].Node.GlobalPosition;
    public override void _ExitTree() { cancellation.Cancel(); cancellation.Dispose(); }
}


