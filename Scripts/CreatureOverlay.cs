using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Assets;
namespace ACWorldEdit;

/// <summary>Read-only, transient projection of nearby database spawns.</summary>
public partial class CreatureOverlay : Node3D
{
    public ModelLibrary? Models { get; set; }
    readonly Dictionary<uint, CreatureAppearance?> appearances = [];
    readonly Dictionary<ModelKey,(ModelInstance Node,uint[] Guids)> modelBatches = [];
    long modelUpdate;
    CreatureDraft? draft;
    string draftPath = "";
    string sourceKey = "";
    string draftMessage = "Local draft only; source database is unchanged.";
    TranslationGizmo gizmo = null!;
    public bool IsDragging => gizmo.Dragging;
    public int ModelCreatureCount => modelBatches.Values.Where(b => b.Node.ModelReady).Sum(b => b.Guids.Length);
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
    public override void _Ready()
    {
        draftPath = ProjectSettings.GlobalizePath(OS.GetCmdlineUserArgs().Contains("--visual-smoke") || OS.GetCmdlineUserArgs().Contains("--gizmo-smoke")
            ? $"res://artifacts/gizmo-drafts/{Guid.NewGuid():N}/positions.json" : "user://drafts/creature-positions.json");
        try { draft = new CreatureDraft(draftPath); }
        catch (Exception) { draftMessage = "Cannot open local draft. Movement disabled; existing file preserved."; }
        gizmo = new TranslationGizmo(); AddChild(gizmo); gizmo.Changed += MoveSelection;
    }

    public Control BuildPanel()
    {
        var panel = new PanelContainer { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -380, OffsetRight = -20, OffsetTop = 20 };
        var box = new VBoxContainer { CustomMinimumSize = new(340, 0) }; panel.AddChild(box);
        box.AddChild(new Label { Text = "CREATURES · LOCAL DRAFT" });
        config = new LineEdit { Text = System.Environment.GetEnvironmentVariable("ACWORLD_SERVER_CONFIG") ?? CreatureSource.DefaultConfigPath,
            PlaceholderText = "worldserver.conf path", TooltipText = "Read WorldDatabaseInfo from this external file; credentials stay out of the project." };
        box.AddChild(config);
        var refresh = new Button { Text = "Refresh creatures" }; refresh.Pressed += Refresh; box.AddChild(refresh);
        var focus = new Button { Text = "Fly to selected creature" }; focus.Pressed += () =>
        { if (selectedGuid.HasValue && markers.TryGetValue(selectedGuid.Value, out var marker)) FocusRequested?.Invoke(marker.Spawn with { Pose = PoseOf(marker.Spawn) }); };
        box.AddChild(focus);
        var history = new HBoxContainer(); box.AddChild(history);
        var undo = new Button { Text = "Undo move",SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; history.AddChild(undo); undo.Pressed += () => History(false);
        var redo = new Button { Text = "Redo move",SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; history.AddChild(redo); redo.Pressed += () => History(true);
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
        if (gizmo != null) { gizmo.Finish(false); gizmo.Visible = false; }
        foreach (var batch in modelBatches.Values) { batch.Node.Visible = false; batch.Node.QueueFree(); }
        modelBatches.Clear(); appearances.Clear(); modelUpdate = 0;
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
                    sourceKey = result.SourceKey;
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
        if (Models?.Catalog != null && System.Diagnostics.Stopwatch.GetElapsedTime(modelUpdate).TotalSeconds > 0.2)
        { UpdateModels(camera); modelUpdate = System.Diagnostics.Stopwatch.GetTimestamp(); }
        foreach (var batch in modelBatches.Values)
            if (batch.Node.ModelReady) foreach (uint guid in batch.Guids) if (markers.TryGetValue(guid,out var marker)) marker.Body.Visible = false;
        foreach (var marker in markers.Values)
            marker.Label.Visible = selectedGuid == marker.Spawn.Guid || camera.GlobalPosition.DistanceSquaredTo(marker.Node.GlobalPosition) < 120 * 120;
        gizmo.Update(camera,draft != null && selectedGuid.HasValue && markers.TryGetValue(selectedGuid.Value,out var selectedMarker) ? selectedMarker.Node.GlobalPosition : null);
    }
    void UpdateModels(Camera3D camera)
    {
        if (Models?.Catalog == null) return;
        var groups = new Dictionary<ModelKey,List<uint>>();
        foreach (var marker in markers.Values)
        {
            if (selectedGuid != marker.Spawn.Guid && camera.GlobalPosition.DistanceSquaredTo(marker.Node.GlobalPosition) > 350 * 350) continue;
            if (!appearances.TryGetValue(marker.Spawn.DisplayId,out var appearance))
            {
                try { appearance = Models.Catalog.Resolve(marker.Spawn.DisplayId); }
                catch (Exception) { appearance = null; GD.PushWarning($"No client appearance for creature display {marker.Spawn.DisplayId}; using labeled marker."); }
                appearances[marker.Spawn.DisplayId] = appearance;
            }
            if (appearance == null) continue;
            var key = new ModelKey(appearance.Path,appearance.Textures[0],appearance.Textures[1],appearance.Textures[2],appearance.BakedTexture ?? "");
            if (!groups.TryGetValue(key,out var list)) groups[key] = list = []; list.Add(marker.Spawn.Guid);
        }
        foreach (var batch in modelBatches.ToArray())
        {
            if (groups.TryGetValue(batch.Key,out var guids) && guids.SequenceEqual(batch.Value.Guids)) continue;
            foreach (uint guid in batch.Value.Guids) if (markers.TryGetValue(guid,out var marker)) marker.Body.Visible = true;
            batch.Value.Node.Visible = false; batch.Value.Node.QueueFree(); modelBatches.Remove(batch.Key);
        }
        foreach (var group in groups)
            if (!modelBatches.ContainsKey(group.Key))
                modelBatches[group.Key] = (Models.Attach(this,group.Key,group.Value.Select(ModelTransform).ToArray()),group.Value.ToArray());
    }
    Transform3D ModelTransform(uint guid)
    {
        var marker = markers[guid]; float scale = marker.Spawn.DisplayScale * (appearances.GetValueOrDefault(marker.Spawn.DisplayId)?.Scale ?? 1);
        return new(marker.Node.Basis.Scaled(Vector3.One * scale),marker.Node.Position);
    }
    void AddMarker(CreatureSpawn spawn)
    {
        var pose = PoseOf(spawn); var position = WorldCoordinates.ToView(new(pose.X, pose.Y, pose.Z));
        var node = new Node3D { Name = "Creature_" + spawn.Guid, Position = new((float)position.X, (float)position.Y, (float)position.Z), Rotation = new(0, (float)pose.Heading, 0) };
        var body = new MeshInstance3D { Mesh = bodyMesh, MaterialOverride = normal, Position = new(0, 1, 0) }; node.AddChild(body);
        body.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
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
        var spawn = marker.Spawn; var pose = PoseOf(spawn);
        details.Text = FormattableString.Invariant($"{spawn.Name}\nGUID {spawn.Guid} · template {spawn.TemplateId} · display {spawn.DisplayId} · map {pose.Map}\nX {pose.X:F3}  Y {pose.Y:F3}  Z {pose.Z:F3}\nHeading {pose.Heading:F4} rad\nSpawn mask {spawn.SpawnMask} · phase mask {spawn.PhaseMask}\n{(draft?.Conflict(sourceKey,guid,spawn.Pose) == true ? "Source position changed; draft requires review." : draftMessage)}");
        list.DeselectAll();
        for (int i = 0; i < list.ItemCount; i++)
            if (list.GetItemMetadata(i).AsString() == guid.ToString())
            { list.Select(i); list.CallDeferred(ItemList.MethodName.EnsureCurrentIsVisible); break; }
    }
    WorldPose PoseOf(CreatureSpawn spawn) => draft?.Effective(sourceKey,spawn.Guid,spawn.Pose) ?? spawn.Pose;
    public bool BeginDrag(Camera3D camera,Vector2 screen) => draft != null && selectedGuid.HasValue && gizmo.Begin(camera,screen);
    public void Drag(Camera3D camera,Vector2 screen) => gizmo.Drag(camera,screen);
    public void EndDrag(bool commit) => gizmo.Finish(commit);
    public bool SmokeGizmo(Camera3D camera)
    {
        if (!selectedGuid.HasValue || draft == null) return false;
        uint guid = selectedGuid.Value; var original = markers[guid].Node.Position;
        gizmo.Update(camera,original);
        var handle = original+Vector3.Right*gizmo.Scale.X*0.7f;
        if (!BeginDrag(camera,camera.UnprojectPosition(handle))) return false;
        Drag(camera,camera.UnprojectPosition(handle+Vector3.Right*2)); EndDrag(true);
        var moved = markers[guid].Node.Position;
        if (moved.DistanceTo(original+Vector3.Right*2)>0.05f) return false;
        foreach (var batch in modelBatches.Values)
        { int i=Array.IndexOf(batch.Guids,guid); if (i>=0 && batch.Node.Transforms![i].Origin.DistanceTo(moved)>0.01f) return false; }
        draft.Dispose(); draft = new CreatureDraft(draftPath);
        if (ViewPosition(PoseOf(markers[guid].Spawn)).DistanceTo(moved)>0.01f) return false;
        History(false); if (markers[guid].Node.Position.DistanceTo(original)>0.01f) return false;
        History(true); if (markers[guid].Node.Position.DistanceTo(moved)>0.01f) return false;
        History(false); return markers[guid].Node.Position.DistanceTo(original)<0.01f;
    }
    void MoveSelection(Vector3 position,bool commit)
    {
        if (!selectedGuid.HasValue || !markers.TryGetValue(selectedGuid.Value,out var marker)) return;
        if (commit && draft != null)
        {
            var p = WorldCoordinates.ToServer(new(position.X,position.Y,position.Z));
            try { draft.Move(sourceKey,marker.Spawn.Guid,marker.Spawn.Pose,PoseOf(marker.Spawn) with { X=p.X,Y=p.Y,Z=p.Z }); draftMessage = "Saved local draft. Drag colored axes to move; source unchanged."; }
            catch (Exception) { draftMessage = "Could not save move or source changed. Previous draft preserved."; position = ViewPosition(PoseOf(marker.Spawn)); }
        }
        marker.Node.Position = position;
        foreach (var batch in modelBatches.Values)
        { int index = Array.IndexOf(batch.Guids,marker.Spawn.Guid); if (index >= 0) batch.Node.SetPlacement(index,ModelTransform(marker.Spawn.Guid)); }
        if (commit) Select(marker.Spawn.Guid);
    }
    static Vector3 ViewPosition(WorldPose pose) { var p = WorldCoordinates.ToView(new(pose.X,pose.Y,pose.Z)); return new((float)p.X,(float)p.Y,(float)p.Z); }
    void History(bool redo)
    {
        gizmo.Finish(false);
        try { if (redo) draft?.Redo(); else draft?.Undo(); }
        catch (Exception) { draftMessage = "Could not save draft history. Previous draft preserved."; return; }
        foreach (var marker in markers.Values) marker.Node.Position = ViewPosition(PoseOf(marker.Spawn));
        foreach (var batch in modelBatches.Values) for (int i=0;i<batch.Guids.Length;i++) batch.Node.SetPlacement(i,ModelTransform(batch.Guids[i]));
        if (selectedGuid.HasValue) Select(selectedGuid.Value);
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
    public override void _ExitTree() { cancellation.Cancel(); cancellation.Dispose(); draft?.Dispose(); }
}
