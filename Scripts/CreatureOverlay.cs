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
    CancellationTokenSource cancellation = new();
    CreatureRegion? region;
    Task<CreatureRegionResult>? pending;
    // The query may finish after cancellation. Only its captured generation can publish
    // records; the replacement waits so at most one database query is active.
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
    /// Build read-only details, config-path input, filtered list, animation menu and local history
    /// controls. No control writes SQL or client assets.
    /// </summary>
    public Control BuildPanel()
    {
        var panel = new PanelContainer
        {
            AnchorLeft = 1,
            AnchorRight = 1,
            OffsetLeft = -380,
            OffsetRight = -20,
            OffsetTop = 20
        };
        var box = new VBoxContainer
        {
            CustomMinimumSize = new(340, 0)
        };
        panel.AddChild(box);
        box.AddChild(new Label { Text = "CREATURES · LOCAL DRAFT" });
        config = new LineEdit
        {
            Text = System.Environment.GetEnvironmentVariable("ACWORLD_SERVER_CONFIG") ?? CreatureSource.DefaultConfigPath,
            PlaceholderText = "worldserver.conf path",
            TooltipText = "Read WorldDatabaseInfo from this external file; credentials stay out of the project."
        };
        box.AddChild(config);
        var refresh = new Button
        {
            Text = "Refresh creatures"
        };
        refresh.Pressed += Refresh;
        box.AddChild(refresh);
        var focus = new Button
        {
            Text = "Fly to selected creature"
        };
        focus.Pressed += () =>
        {
            if (selectedGuid.HasValue && markers.TryGetValue(selectedGuid.Value, out var marker))
            {
                FocusRequested?.Invoke(marker.Spawn with { Pose = PoseOf(marker.Spawn) });
            }
        };
        box.AddChild(focus);
        box.AddChild(new Label { Text = "Preview animation (selected creature)" });
        animation = new OptionButton
        {
            FitToLongestItem = false,
            Disabled = true
        };
        animation.AddItem("Stand / idle", 0);
        box.AddChild(animation);
        animation.ItemSelected += index =>
        {
            previewAnimation = (ushort)animation.GetItemId((int)index);
            modelUpdate = 0;
        };
        var history = new HBoxContainer();
        box.AddChild(history);
        var undo = new Button
        {
            Text = "Undo move",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        history.AddChild(undo);
        undo.Pressed += () => History(false);
        var redo = new Button
        {
            Text = "Redo move",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        history.AddChild(redo);
        redo.Pressed += () => History(true);
        filter = new LineEdit
        {
            PlaceholderText = "Filter loaded creatures by name / GUID / template"
        };
        box.AddChild(filter);
        filter.TextChanged += _ => PopulateList();
        list = new ItemList
        {
            CustomMinimumSize = new(340, 190)
        };
        box.AddChild(list);
        list.ItemSelected += index => Select(uint.Parse(list.GetItemMetadata((int)index).AsString()));
        status = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new(340, 40)
        };
        box.AddChild(status);
        details = new Label
        {
            Text = "Click a creature marker or choose one above.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new(340, 90)
        };
        box.AddChild(details);
        return panel;
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
        Clear(preserveSelection);
        queryStatus = region == null ? "Waiting for an outdoor map." : "Loading nearby creatures…";
        // Keep at most one active query. Its cancellation completes before starting the replacement.
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
        records = [];
        nextMarker = 0;
        if (!preserveSelection)
        {
            selectedGuid = null;
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
        if (pending is { IsCompleted: true })
        {
            try
            {
                var result = pending.GetAwaiter().GetResult();
                if (pendingGeneration == generation)
                {
                    records = result.Spawns;
                    sourceKey = result.SourceKey;
                    truncated = result.Truncated;
                    nextMarker = 0;
                    PopulateList();
                    queryStatus = records.Count == 0 ? "No creatures in this region." : "Preparing creature markers…";
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

        if (requestedGeneration != generation && pending == null && region != null)
        {
            requestedGeneration = generation;
            pendingGeneration = generation;
            var next = region;
            string path = config.Text;
            var token = cancellation.Token;
            pending = Task.Run(async () => await new CreatureSource(path).ReadAsync(next, token), token);
        }

        if (nextMarker < records.Count)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < 64 && nextMarker < records.Count && (i == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds < 2); i++, nextMarker++)
            {
                AddMarker(records[nextMarker]);
            }

            if (nextMarker == records.Count)
            {
                if (selectedGuid.HasValue)
                {
                    if (markers.ContainsKey(selectedGuid.Value))
                    {
                        Select(selectedGuid.Value);
                    }
                    else
                    {
                        selectedGuid = null;
                        details.Text = "Selected creature is outside the current region.";
                    }
                }

                queryStatus = truncated ? $"Showing first {records.Count} creatures. Reduce nearby tile radius for dense areas." : $"{records.Count} creatures loaded. Click a marker to select.";
            }
            else
            {
                queryStatus = $"Preparing markers {nextMarker}/{records.Count}…";
            }
        }

        if (status != null)
        {
            status.Text = queryStatus;
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

        foreach (var marker in markers.Values)
        {
            bool nearby = selectedGuid == marker.Spawn.Guid || camera.GlobalPosition.DistanceSquaredTo(marker.Node.GlobalPosition) < 350 * 350;
            marker.Node.Visible = nearby;
            marker.Label.Visible = selectedGuid == marker.Spawn.Guid || camera.GlobalPosition.DistanceSquaredTo(marker.Node.GlobalPosition) < 120 * 120;
        }

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
        foreach (var marker in markers.Values)
        {
            if (selectedGuid != marker.Spawn.Guid && camera.GlobalPosition.DistanceSquaredTo(marker.Node.GlobalPosition) > 350 * 350)
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
            if (groups.TryGetValue(batch.Key, out var guids) && guids.SequenceEqual(batch.Value.Guids))
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
            int index = list.AddItem($"{spawn.Name} · #{spawn.Guid} · template {spawn.TemplateId}");
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
        var spawn = marker.Spawn;
        var pose = PoseOf(spawn);
        details.Text = FormattableString.Invariant($"{spawn.Name}\nGUID {spawn.Guid} · template {spawn.TemplateId} · display {spawn.DisplayId} · map {pose.Map}\nX {pose.X:F3}  Y {pose.Y:F3}  Z {pose.Z:F3}\nHeading {pose.Heading:F4} rad\nSpawn mask {spawn.SpawnMask} · phase mask {spawn.PhaseMask}\n{(draft?.Conflict(sourceKey, guid, spawn.Pose) == true ? "Source position changed; draft requires review." : draftMessage)}");
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

    /// <summary>
    /// Choose the nearest native bounding-box hit or screen-space marker fallback. Terrain occlusion
    /// and precise animated triangle picking are not implemented.
    /// </summary>
    public bool Pick(Camera3D camera, Vector2 screen)
    {
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
                float? hit = BoxHit(batch.Node.Bounds, inverse * origin, inverse.Basis * direction);
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

        if (!nearest.HasValue)
        {
            return false;
        }

        Select(nearest.Value);
        return true;
    }

    /// <summary>
    /// Slab intersection in instance-local space. Do not normalize transformed directions: ray
    /// parameters must stay comparable across scaled instances.
    /// </summary>
    static float? BoxHit(Aabb box, Vector3 origin, Vector3 direction)
    {
        float near = 0, far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            if (Math.Abs(direction[axis]) < 0.000001f)
            {
                if (origin[axis] < box.Position[axis] || origin[axis] > box.End[axis])
                {
                    return null;
                }

                continue;
            }

            float a = (box.Position[axis] - origin[axis]) / direction[axis], b = (box.End[axis] - origin[axis]) / direction[axis];
            near = Math.Max(near, Math.Min(a, b));
            far = Math.Min(far, Math.Max(a, b));
            if (far < near)
            {
                return null;
            }
        }

        return near;
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
