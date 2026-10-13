using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Assets;

namespace ACWorldEdit;
/// <summary>
/// Transient scene/list projection of bounded database records. CreatureDraft owns durable moves
/// and ModelLibrary shared resources; unloading markers never changes rows.
/// </summary>
public partial class CreatureOverlay : Node3D
{
    public ModelLibrary? Models { get; set; }

    readonly Dictionary<uint, CreatureAppearance?> appearances = [];
    readonly Dictionary<ModelKey, (ModelInstance Node, uint[] Guids)> modelBatches = [];
    long modelUpdate;
    // Source records are immutable baselines. Markers/native transforms show effective
    // draft poses; neither a streaming refresh nor a failed save replaces source rows.
    CreatureDraft? draft;
    string draftPath = "";
    string sourceKey = "";
    string draftMessage = "Local draft only; source database is unchanged.";
    TranslationGizmo gizmo = null!;
    OptionButton animation = null!;
    ushort previewAnimation;
    uint? animationListGuid;
    public bool PreviewAnimationReady => selectedGuid.HasValue && markers.TryGetValue(selectedGuid.Value, out var marker) && !marker.Body.Visible && modelBatches.Any(b => b.Key.Animation == previewAnimation && b.Value.Node.ModelReady && b.Value.Guids.Contains(selectedGuid.Value));
    public bool IsDragging => gizmo.Dragging;
    public int ModelCreatureCount => modelBatches.Values.Where(b => b.Node.ModelReady).Sum(b => b.Guids.Length);
    public int AdmittedCreatureCount => admitted.Count;

    readonly Dictionary<uint, (CreatureSpawn Spawn, Node3D Node, MeshInstance3D Body, Label3D Label)> markers = [];
    readonly StandardMaterial3D normal = new()
    {
        AlbedoColor = new("5bd8c2")
    };
    readonly StandardMaterial3D selected = new()
    {
        AlbedoColor = new("ffcf69"),
        EmissionEnabled = true,
        Emission = new("805510")
    };
    readonly CapsuleMesh bodyMesh = new()
    {
        Radius = 0.55f,
        Height = 2
    };
    ItemList list = null!;
    Label details = null!, status = null!;
    LineEdit config = null!;
    LineEdit filter = null!;
    Label selectionName = null!, selectionMeta = null!;
    public LineEdit ConnectionControl => config;
    public string ConnectionPath => config.Text;
    public SpawnPreview Preview { get; private set; } = new();
    public bool LayerEnabled { get; private set; } = true;
    public WorldEventInfo[] EventCatalog { get; private set; } = [];

    public event Action? SelectionChanged;
    SpawnQueryPoint? queryPoint;
    readonly HashSet<uint> admitted = [];
    bool searchPending;
    long searchChanged;
    public void SetPreview(SpawnPreview preview)
    {
        preview.Validate();
        Preview = preview;
        Restart(false);
    }

    public void SetLayerEnabled(bool value)
    {
        if (LayerEnabled == value)
        {
            return;
        }

        LayerEnabled = value;
        Restart(false);
    }

    public void Deselect()
    {
        // Cancel while the selected identity still exists so an unfinished move restores its pose.
        gizmo.Finish(false);
        if (selectedGuid.HasValue && markers.TryGetValue(selectedGuid.Value, out var old))
        {
            old.Body.MaterialOverride = normal;
        }

        selectedGuid = null;
        list?.DeselectAll();
        animation.Disabled = true;
        selectionName.Text = "Select a creature";
        selectionMeta.Text = "Click a model or choose one above";
        details.Text = "Selection details appear here.";
    }

    CancellationTokenSource cancellation = new();
    CreatureRegion? region;
    Task<CreatureRegionResult>? pending;
    // The query may finish after cancellation. Only its captured generation can publish
    // records; the replacement waits so at most one database query is active.
    int generation, pendingGeneration;
    IReadOnlyList<CreatureSpawn> records = [];
    uint? selectedGuid;
    List<CreatureSpawn> pendingMarkers = [];
    bool truncated;
    string queryStatus = "Waiting for an outdoor map.";
    public uint? SelectedGuid => selectedGuid;
    public IReadOnlyList<CreatureSpawn> Records => records;
    public bool IsLoading => pending != null || pendingMarkers.Count > 0;

    public uint? SelectedListGuid
    {
        get
        {
            var indices = list.GetSelectedItems();
            return indices.Length == 0 ? null : uint.Parse(list.GetItemMetadata(indices[0]).AsString());
        }
    }

    public string QueryStatus => queryStatus;

    public event Action<CreatureSpawn>? FocusRequested;
    /// <summary>
    /// Open a durable local draft, isolated for movement smoke checks, and attach the gizmo.
    /// Recovery failures preserve the file and disable movement.
    /// </summary>
    public override void _Ready()
    {
        draftPath = ProjectSettings.GlobalizePath(OS.GetCmdlineUserArgs().Contains("--visual-smoke") || OS.GetCmdlineUserArgs().Contains("--gizmo-smoke") ? $"res://artifacts/gizmo-drafts/{Guid.NewGuid():N}/positions.json" : "user://drafts/creature-positions.json");
        try
        {
            draft = new CreatureDraft(draftPath);
        }
        catch (Exception)
        {
            draftMessage = "Cannot open local draft. Movement disabled; existing file preserved.";
        }

        gizmo = new TranslationGizmo();
        AddChild(gizmo);
        gizmo.Changed += MoveSelection;
    }

    /// <summary>
    /// Restart on region changes while retaining same-map selection. Map changes clear
    /// source-specific selection.
    /// </summary>
    public void SetRegion(CreatureRegion? next)
    {
        if (region == next)
        {
            return;
        }

        bool preserveSelection = next != null && next.Map == region?.Map;
        region = next;
        if (!preserveSelection)
        {
            queryPoint = null;
        }

        Restart(preserveSelection);
    }

    /// <summary>
    /// Invalidate the snapshot and request fresh data through normal cancellation.
    /// </summary>
    public void Refresh() => Restart();
    /// <summary>
    /// Advance generation and cancel the query, awaiting its completion before starting another.
    /// Bound concurrency and reject stale results.
    /// </summary>
    void Restart(bool preserveSelection = true)
    {
        generation++;
        cancellation.Cancel();
        cancellation.Dispose();
        cancellation = new();
        // Keep at most one active query. Its cancellation completes before starting the replacement.
        if (preserveSelection)
        {
            // Same region (routine camera movement, search/filter tweaks): cancel and reissue the
            // query but leave every current marker/model exactly as it is. ReconcileSpawns() diffs
            // the arriving result by GUID instead of wiping the scene first, which is what made
            // flying trigger a visible "clear and reload" for nearly the same nearby creatures every
            // time the quantized query center shifted.
            pendingMarkers.Clear();
            queryStatus = region == null ? "Waiting for an outdoor map." : records.Count > 0 ? "Refreshing nearby creatures…" : "Loading nearby creatures…";
        }
        else
        {
            Clear(false);
            queryStatus = region == null ? "Waiting for an outdoor map." : "Loading nearby creatures…";
        }
    }

    /// <summary>
    /// Remove transient markers/batches and cancel the gizmo. Retain durable history and optionally
    /// keep selection for same-map reloads.
    /// </summary>
    void Clear(bool preserveSelection)
    {
        if (gizmo != null)
        {
            gizmo.Finish(false);
            gizmo.Visible = false;
        }

        foreach (var batch in modelBatches.Values)
        {
            batch.Node.Visible = false;
            batch.Node.QueueFree();
        }

        modelBatches.Clear();
        appearances.Clear();
        modelUpdate = 0;
        animationListGuid = null;
        if (animation != null)
        {
            animation.Disabled = true;
        }

        foreach (var marker in markers.Values)
        {
            marker.Node.Visible = false;
            marker.Node.QueueFree();
        }

        markers.Clear();
        admitted.Clear();
        records = [];
        pendingMarkers.Clear();
        if (!preserveSelection)
        {
            if (selectionName != null)
            {
                Deselect();
            }
            else
            {
                selectedGuid = null;
            }
        }

        list?.Clear();
        if (!selectedGuid.HasValue && details != null)
        {
            details.Text = "Click a creature marker or choose one above.";
        }
    }

    int requestedGeneration = -1;
    /// <summary>
    /// Publish only current queries, create bounded marker batches and reconcile models/list/gizmo
    /// state.
    /// </summary>
    public void Update(Camera3D camera)
    {
        if (LayerEnabled && region != null)
        {
            var server = WorldCoordinates.ToServer(new(camera.Position.X, camera.Position.Y, camera.Position.Z));
            var point = SpawnQueryPoint.FromPose(new(region.Map, server.X, server.Y, server.Z, 0));
            if (point != queryPoint)
            {
                queryPoint = point;
                Restart(true);
            }
        }

        if (searchPending && System.Diagnostics.Stopwatch.GetElapsedTime(searchChanged).TotalSeconds >= 0.3)
        {
            searchPending = false;
            Restart(false);
        }

        if (pending is { IsCompleted: true })
        {
            try
            {
                var result = pending.GetAwaiter().GetResult();
                if (pendingGeneration == generation)
                {
                    sourceKey = result.SourceKey;
                    EventCatalog = result.Events ?? [];
                    truncated = result.Truncated;
                    ReconcileSpawns(result.Spawns);
                    if (selectedGuid.HasValue && !records.Any(s => s.Guid == selectedGuid.Value))
                    {
                        Deselect();
                    }

                    PopulateList();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                if (pendingGeneration == generation)
                {
                    queryStatus = CreatureSource.SafeError(error);
                }
            }

            pending = null;
        }

        if (requestedGeneration != generation && pending == null && region != null && LayerEnabled)
        {
            requestedGeneration = generation;
            pendingGeneration = generation;
            var next = region;
            string path = ConnectionPath;
            var preview = Preview;
            var point = queryPoint;
            string search = filter.Text;
            var token = cancellation.Token;
            pending = Task.Run(async () => await new CreatureSource(path).ReadAsync(next, preview, point, search, token), token);
        }

        if (pendingMarkers.Count > 0)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            int created = 0;
            while (pendingMarkers.Count > 0 && created < 64 && (created == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds < 2))
            {
                var spawn = pendingMarkers[^1];
                pendingMarkers.RemoveAt(pendingMarkers.Count - 1);
                AddMarker(spawn);
                created++;
            }

            if (pendingMarkers.Count == 0)
            {
                if (selectedGuid.HasValue)
                {
                    if (markers.ContainsKey(selectedGuid.Value))
                    {
                        Select(selectedGuid.Value);
                    }
                    else
                    {
                        Deselect();
                        details.Text = "Selected creature is outside the current region.";
                    }
                }

                queryStatus = CreatureStatusText();
            }
            else
            {
                queryStatus = $"Preparing markers {records.Count - pendingMarkers.Count}/{records.Count}…";
            }
        }

        if (status != null)
        {
            status.Text = LayerEnabled ? queryStatus : "Creature layer hidden";
        }

        if (Models?.Catalog != null && System.Diagnostics.Stopwatch.GetElapsedTime(modelUpdate).TotalSeconds > 0.2)
        {
            UpdateModels(camera);
            modelUpdate = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        foreach (var batch in modelBatches.Values)
        {
            if (batch.Node.ModelReady)
            {
                foreach (uint guid in batch.Guids)
                {
                    if (markers.TryGetValue(guid, out var marker))
                    {
                        marker.Body.Visible = false;
                    }
                }

                if (selectedGuid.HasValue && batch.Guids.Contains(selectedGuid.Value) && animationListGuid != selectedGuid)
                {
                    animation.Clear();
                    int selectedIndex = 0;
                    foreach (ushort id in batch.Node.AvailableAnimations)
                    {
                        animation.AddItem($"{Models?.Catalog?.AnimationNames.GetValueOrDefault(id) ?? "Animation"} ({id})", id);
                        if (id == previewAnimation)
                        {
                            selectedIndex = animation.ItemCount - 1;
                        }
                    }

                    if (animation.ItemCount == 0)
                    {
                        animation.AddItem("Static model", 0);
                    }

                    animation.Select(selectedIndex);
                    animation.Disabled = batch.Node.AvailableAnimations.Length <= 1;
                    animationListGuid = selectedGuid;
                }
            }
        }

        int visibleNames = 0;
        foreach (var marker in markers.Values)
        {
            bool nearby = LayerEnabled && admitted.Contains(marker.Spawn.Guid);
            marker.Node.Visible = nearby;
            bool showName = nearby && Preview.ShowNames && visibleNames < 64 && camera.GlobalPosition.DistanceSquaredTo(marker.Node.GlobalPosition) < 80 * 80;
            marker.Label.Visible = selectedGuid == marker.Spawn.Guid || showName;
            if (showName)
            {
                visibleNames++;
            }
        }

        UpdateInspectorActions();
        gizmo.Update(camera, draft != null && selectedGuid.HasValue && markers.TryGetValue(selectedGuid.Value, out var selectedMarker) ? selectedMarker.Node.GlobalPosition : null);
    }

    /// <summary>
    /// Cache appearances and group nearby creatures by variant/clip. Selection can preview a
    /// separate animation without changing other creatures.
    /// </summary>
    void UpdateModels(Camera3D camera)
    {
        if (Models?.Catalog == null)
        {
            return;
        }

        var groups = new Dictionary<ModelKey, List<uint>>();
        admitted.Clear();
        var candidates = markers.Values.Where(m => selectedGuid == m.Spawn.Guid || camera.GlobalPosition.DistanceSquaredTo(m.Node.GlobalPosition) <= Preview.ViewDistance * Preview.ViewDistance).OrderBy(m => selectedGuid == m.Spawn.Guid ? 0 : 1).ThenBy(m => camera.GlobalPosition.DistanceSquaredTo(m.Node.GlobalPosition)).Take(Preview.ModelLimit);
        foreach (var marker in candidates)
        {
            admitted.Add(marker.Spawn.Guid);
            if (!LayerEnabled)
            {
                continue;
            }

            if (!appearances.TryGetValue(marker.Spawn.DisplayId, out var appearance))
            {
                try
                {
                    appearance = Models.Catalog.Resolve(marker.Spawn.DisplayId);
                }
                catch (Exception)
                {
                    appearance = null;
                    GD.PushWarning($"No client appearance for creature display {marker.Spawn.DisplayId}; using labeled marker.");
                }

                appearances[marker.Spawn.DisplayId] = appearance;
            }

            if (appearance == null)
            {
                continue;
            }

            var key = new ModelKey(appearance.Path, appearance.Textures[0], appearance.Textures[1], appearance.Textures[2], appearance.BakedTexture ?? "", Animation: selectedGuid == marker.Spawn.Guid ? previewAnimation : (ushort)0);
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
            }

            list.Add(marker.Spawn.Guid);
        }

        foreach (var batch in modelBatches.ToArray())
        {
            // Compare membership, not order: distance-sorted candidates reorder every tick while
            // flying even when the visible set is unchanged, and rebuilding the batch on pure
            // reordering is what flickers the fallback marker back in for no reason.
            if (groups.TryGetValue(batch.Key, out var guids) && guids.Count == batch.Value.Guids.Length && guids.ToHashSet().SetEquals(batch.Value.Guids))
            {
                continue;
            }

            foreach (uint guid in batch.Value.Guids)
            {
                if (markers.TryGetValue(guid, out var marker))
                {
                    marker.Body.Visible = true;
                }
            }

            batch.Value.Node.Visible = false;
            batch.Value.Node.QueueFree();
            modelBatches.Remove(batch.Key);
        }

        foreach (var group in groups)
        {
            if (!modelBatches.ContainsKey(group.Key))
            {
                modelBatches[group.Key] = (Models.Attach(this, group.Key, group.Value.Select(ModelTransform).ToArray()), group.Value.ToArray());
            }
        }
    }

    /// <summary>
    /// Compose creature heading, effective draft position and template/display scale. Static scenery
    /// facing corrections do not belong here.
    /// </summary>
    Transform3D ModelTransform(uint guid)
    {
        var marker = markers[guid];
        float scale = marker.Spawn.DisplayScale * (appearances.GetValueOrDefault(marker.Spawn.DisplayId)?.Scale ?? 1);
        return new(marker.Node.Basis.Scaled(Vector3.One * scale), marker.Node.Position);
    }

    /// <summary>
    /// Diff a freshly queried snapshot against the markers already on screen instead of wiping and
    /// rebuilding: creatures no longer present are removed, creatures already shown keep their
    /// existing node/native model untouched (only their cached record is refreshed), and only
    /// genuinely new creatures queue for incremental marker creation.
    /// </summary>
    void ReconcileSpawns(IReadOnlyList<CreatureSpawn> spawns)
    {
        var incoming = spawns.ToDictionary(s => s.Guid);
        foreach (uint guid in markers.Keys.Where(g => !incoming.ContainsKey(g)).ToArray())
        {
            var marker = markers[guid];
            marker.Node.Visible = false;
            marker.Node.QueueFree();
            markers.Remove(guid);
            admitted.Remove(guid);
        }

        foreach (var spawn in spawns)
        {
            if (markers.TryGetValue(spawn.Guid, out var existing))
            {
                markers[spawn.Guid] = (spawn, existing.Node, existing.Body, existing.Label);
            }
        }

        records = spawns;
        pendingMarkers = spawns.Where(s => !markers.ContainsKey(s.Guid)).ToList();
        queryStatus = pendingMarkers.Count > 0 ? $"Preparing markers {records.Count - pendingMarkers.Count}/{records.Count}…" : CreatureStatusText();
    }

    string CreatureStatusText() => records.Count == 0 ? "No creatures in this region." : truncated ? $"{records.Count} nearest results · limit reached\nNarrow the distance or search." : $"{records.Count} matches in this neighborhood";

    /// <summary>
    /// Create a labeled fallback at the effective pose; model readiness controls its body
    /// visibility. GUID remains the stable selection identity.
    /// </summary>
    void AddMarker(CreatureSpawn spawn)
    {
        var pose = PoseOf(spawn);
        var position = WorldCoordinates.ToView(new(pose.X, pose.Y, pose.Z));
        var node = new Node3D
        {
            Name = "Creature_" + spawn.Guid,
            Position = new((float)position.X, (float)position.Y, (float)position.Z),
            Rotation = new(0, (float)pose.Heading, 0)
        };
        var body = new MeshInstance3D
        {
            Mesh = bodyMesh,
            MaterialOverride = normal,
            Position = new(0, 1, 0)
        };
        node.Visible = false;
        node.AddChild(body);
        body.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        var label = new Label3D
        {
            Text = $"{spawn.Name}\n#{spawn.Guid}",
            Position = new(0, 3, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 32,
            PixelSize = 0.018f
        };
        node.AddChild(label);
        AddChild(node);
        markers.Add(spawn.Guid, (spawn, node, body, label));
    }

    /// <summary>
    /// Filter loaded records and preserve GUID metadata so duplicate names/templates cannot confuse
    /// selection.
    /// </summary>
    void PopulateList()
    {
        list.Clear();
        string term = filter.Text.Trim();
        foreach (var spawn in records.Where(s => $"{s.Name} {s.Guid} {s.TemplateId}".Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            int index = list.AddItem($"{spawn.Name}   ·   {spawn.Guid}");
            list.SetItemTooltip(index, $"GUID {spawn.Guid} · Template {spawn.TemplateId}\n{spawn.Visibility?.Description}");
            list.SetItemMetadata(index, spawn.Guid.ToString());
            if (selectedGuid == spawn.Guid)
            {
                list.Select(index);
            }
        }
    }

    /// <summary>
    /// Synchronize viewport/list/details and reset the clip menu on selection changes. Highlight
    /// fallback geometry when needed.
    /// </summary>
    public void Select(uint guid)
    {
        if (!markers.TryGetValue(guid, out var marker))
        {
            return;
        }

        if (selectedGuid.HasValue && markers.TryGetValue(selectedGuid.Value, out var previous))
        {
            previous.Body.MaterialOverride = normal;
        }

        if (selectedGuid != guid)
        {
            previewAnimation = 0;
            animationListGuid = null;
            modelUpdate = 0;
            animation.Disabled = true;
        }

        selectedGuid = guid;
        marker.Body.MaterialOverride = selected;
        SelectionChanged?.Invoke();
        var spawn = marker.Spawn;
        var pose = PoseOf(spawn);
        selectionName.Text = spawn.Name;
        selectionMeta.Text = $"GUID {spawn.Guid}   /   Template {spawn.TemplateId}";
        details.Text = FormattableString.Invariant($"Map {pose.Map} · Display {spawn.DisplayId}\nX {pose.X:F2}    Y {pose.Y:F2}    Z {pose.Z:F2}\nHeading {pose.Heading:F3} rad\nPhase 0x{spawn.PhaseMask:X} · Difficulty 0x{spawn.SpawnMask:X}\n{spawn.Visibility?.Description}\n{(draft?.Conflict(sourceKey, guid, spawn.Pose) == true ? "Source position changed; draft requires review." : draftMessage)}");
        list.DeselectAll();
        for (int i = 0; i < list.ItemCount; i++)
        {
            if (list.GetItemMetadata(i).AsString() == guid.ToString())
            {
                list.Select(i);
                list.CallDeferred(ItemList.MethodName.EnsureCurrentIsVisible);
                break;
            }
        }
    }

    /// <summary>
    /// Resolve the effective local pose while retaining the immutable source baseline for conflict
    /// detection.
    /// </summary>
    WorldPose PoseOf(CreatureSpawn spawn) => draft?.Effective(sourceKey, spawn.Guid, spawn.Pose) ?? spawn.Pose;
    /// <summary>
    /// Require a writable local draft, selection and a hit on a gizmo handle before moving.
    /// </summary>
    public bool BeginDrag(Camera3D camera, Vector2 screen) => draft != null && selectedGuid.HasValue && gizmo.Begin(camera, screen);
    /// <summary>
    /// Update visual movement; persistence waits for completion of the gesture.
    /// </summary>
    public void Drag(Camera3D camera, Vector2 screen) => gizmo.Drag(camera, screen);
    /// <summary>
    /// Finish/cancel through the gizmo callback, committing at most one history command.
    /// </summary>
    public void EndDrag(bool commit) => gizmo.Finish(commit);
    /// <summary>
    /// Convert committed view coordinates into a local server-coordinate draft, then update
    /// marker/model transforms together. Failed saves restore the persisted pose.
    /// </summary>
    void MoveSelection(Vector3 position, bool commit)
    {
        if (!selectedGuid.HasValue || !markers.TryGetValue(selectedGuid.Value, out var marker))
        {
            return;
        }

        if (commit && draft != null)
        {
            var p = WorldCoordinates.ToServer(new(position.X, position.Y, position.Z));
            try
            {
                draft.Move(sourceKey, marker.Spawn.Guid, marker.Spawn.Pose, PoseOf(marker.Spawn) with { X = p.X, Y = p.Y, Z = p.Z });
                draftMessage = "Saved local draft. Drag colored axes to move; source unchanged.";
            }
            catch (Exception)
            {
                draftMessage = "Could not save move or source changed. Previous draft preserved.";
                position = ViewPosition(PoseOf(marker.Spawn));
            }
        }

        marker.Node.Position = position;
        foreach (var batch in modelBatches.Values)
        {
            int index = Array.IndexOf(batch.Guids, marker.Spawn.Guid);
            if (index >= 0)
            {
                batch.Node.SetPlacement(index, ModelTransform(marker.Spawn.Guid));
            }
        }

        if (commit)
        {
            Select(marker.Spawn.Guid);
        }
    }

    static Vector3 ViewPosition(WorldPose pose)
    {
        var p = WorldCoordinates.ToView(new(pose.X, pose.Y, pose.Z));
        return new((float)p.X, (float)p.Y, (float)p.Z);
    }

    /// <summary>
    /// Cancel the active preview, persist undo/redo and reconcile marker/native positions from
    /// effective history.
    /// </summary>
    void History(bool redo)
    {
        gizmo.Finish(false);
        try
        {
            if (redo)
            {
                draft?.Redo();
            }
            else
            {
                draft?.Undo();
            }
        }
        catch (Exception)
        {
            draftMessage = "Could not save draft history. Previous draft preserved.";
            return;
        }

        foreach (var marker in markers.Values)
        {
            marker.Node.Position = ViewPosition(PoseOf(marker.Spawn));
        }

        foreach (var batch in modelBatches.Values)
        {
            for (int i = 0; i < batch.Guids.Length; i++)
            {
                batch.Node.SetPlacement(i, ModelTransform(batch.Guids[i]));
            }
        }

        if (selectedGuid.HasValue)
        {
            Select(selectedGuid.Value);
        }
    }

    public (uint Guid, float Distance)? FindHit(Camera3D camera, Vector2 screen)
    {
        if (!LayerEnabled)
        {
            return null;
        }

        Vector3 origin = camera.ProjectRayOrigin(screen), direction = camera.ProjectRayNormal(screen);
        uint? nearest = null;
        float distance = float.MaxValue;
        var modelGuids = new HashSet<uint>();
        foreach (var batch in modelBatches.Values)
        {
            if (!batch.Node.ModelReady)
            {
                continue;
            }

            for (int i = 0; i < batch.Guids.Length; i++)
            {
                uint guid = batch.Guids[i];
                modelGuids.Add(guid);
                var inverse = batch.Node.Transforms![i].AffineInverse();
                float? hit = ScenePicking.BoxHit(batch.Node.Bounds, inverse * origin, inverse.Basis * direction);
                if (hit.HasValue && hit.Value < distance && hit.Value < camera.Far)
                {
                    nearest = guid;
                    distance = hit.Value;
                }
            }
        }

        foreach (var marker in markers.Values)
        {
            if (modelGuids.Contains(marker.Spawn.Guid) || !marker.Node.Visible)
            {
                continue;
            }

            Vector3 center = marker.Node.GlobalPosition + Vector3.Up;
            if (camera.IsPositionBehind(center))
            {
                continue;
            }

            var offset = center - origin;
            float along = offset.Dot(direction);
            if (along < 0 || along > camera.Far || along > distance)
            {
                continue;
            }

            // Small screen-space target remains selectable at a distance; only markers near the click qualify.
            if (camera.UnprojectPosition(center).DistanceSquaredTo(screen) > 12 * 12)
            {
                continue;
            }

            nearest = marker.Spawn.Guid;
            distance = along;
        }

        return nearest.HasValue ? (nearest.Value, distance) : null;
    }

    public bool Pick(Camera3D camera, Vector2 screen)
    {
        var hit = FindHit(camera, screen);
        if (!hit.HasValue)
        {
            return false;
        }

        Select(hit.Value.Guid);
        return true;
    }

    public Vector3 PositionOf(uint guid) => markers[guid].Node.GlobalPosition;
    /// <summary>
    /// Cancel read-only queries and release the draft writer lock on scene exit.
    /// </summary>
    public override void _ExitTree()
    {
        cancellation.Cancel();
        cancellation.Dispose();
        draft?.Dispose();
    }
}
