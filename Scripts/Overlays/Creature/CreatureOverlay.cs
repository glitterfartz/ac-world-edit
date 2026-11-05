using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Assets;

namespace ACWorldEdit;
/// <summary>
/// Transient scene/list projection of bounded database records. SpawnPoses keeps moves as `creature`
/// row edits on the shared content draft and ModelLibrary shared resources; unloading markers never
/// changes rows.
/// </summary>
public partial class CreatureOverlay : RegionOverlay, ICreaturePoseHost
{
    public CreatureOverlay() : base(true, "Waiting for an outdoor map.")
    {
        markers = new CreatureMarkers(this);
        modelView = new CreatureModelView(this);
        poseEditor = new CreaturePoseEditor(this);
    }

    public ModelLibrary? Models { get; set; }

    readonly CreatureModelView modelView;
    // Source records are immutable baselines. Markers/native transforms show effective
    // draft poses; neither a streaming refresh nor a failed save replaces source rows.
    SpawnPoses? poses;
    readonly HashSet<ulong> loadingRows = [];
    string sourceKey = "";
    /// <summary>Where editing feedback goes (the status bar); never the inspector.</summary>
    public Action<string>? Report { get; set; }

    /// <summary>The last status message, so a test can read what the user was told.</summary>
    public string LastMessage { get; private set; } = "";

    void Say(string message)
    {
        LastMessage = message;
        Report?.Invoke(message);
    }
    readonly CreaturePoseEditor poseEditor;
    TransformGizmo gizmo => poseEditor.Gizmo;
    OptionButton animation = null!;
    ushort previewAnimation;
    ulong? animationListGuid;
    public bool IsDragging => poseEditor.Dragging;
    public int ModelCreatureCount => modelView.ReadyCount;
    public int AdmittedCreatureCount => modelView.AdmittedCount;

    readonly CreatureMarkers markers;
    ItemList list = null!;
    Label details = null!, status = null!;
    LineEdit filter = null!;
    Label selectionName = null!, selectionMeta = null!;
    public SpawnPreview Preview { get; private set; } = new();
    public WorldEventInfo[] EventCatalog { get; private set; } = [];
    public PhaseCatalog PhaseCatalog { get; private set; } = PhaseCatalog.Empty;

    public event Action? SelectionChanged;
    public void SetPreview(SpawnPreview preview)
    {
        preview.Validate();
        Preview = preview;
        Restart(true);
    }

    public void Deselect()
    {
        // Cancel while the selected identity still exists so an unfinished move restores its pose.
        gizmo.Finish(false);
        if (selectedGuid.HasValue && markers.ContainsKey(selectedGuid.Value))
        {
            markers.Highlight(selectedGuid, null);
        }

        selectedGuid = null;
        group.Clear();
        list?.DeselectAll();
        animation.Disabled = true;
        selectionName.Text = "Select a creature";
        selectionMeta.Text = "Click a model or choose one above";
        details.Text = "Selection details appear here.";
        ShowPose(null);
        RefreshSpawnFields();
    }

    // The query may finish after cancellation; RegionQuery publishes only the current generation
    // and keeps at most one database query active.
    readonly RegionQuery<CreatureRegionResult> query = new();
    IReadOnlyList<CreatureSpawn> records = [];
    ulong? selectedGuid;
    List<CreatureSpawn> pendingMarkers = [];
    bool truncated;
    public ulong? SelectedGuid => selectedGuid;
    public IReadOnlyList<CreatureSpawn> Records => records;
    public bool IsLoading => query.IsLoading || pendingMarkers.Count > 0;

    public event Action<WorldPose>? FocusRequested;
    /// <summary>
    /// Attach the gizmo.
    /// </summary>
    public override void _Ready()
    {
        poseEditor.Attach();
        AddChild(group);
    }

    /// <summary>
    /// Keep creature poses on the shared content draft, and redraw whenever any editor changes it
    /// (an undo, a discard in Pending Changes, a profile switch). A draft that cannot open disables movement.
    /// </summary>
    public void Attach(ContentDrafts drafts)
    {
        poses = new SpawnPoses(drafts, SpawnKind.Creature, ContentDraftFiles.Sources.DialectOf);
        templateAddons = new TemplateAddons(drafts, ContentDraftFiles.Sources.DialectOf);
        drafts.Changed += _ =>
        {
            if (IsInsideTree() && gizmo != null)
            {
                Resync();
            }
        };
    }

    /// <summary>
    /// Load the selected creature's database row in the background: a pose edit replaces the whole row,
    /// but the region read only carries the pose. Selecting comes before dragging, so it is ready by then.
    /// </summary>
    async void LoadRow(ulong guid)
    {
        if (poses == null || !poses.Available || poses.Has(sourceKey, guid) || IsStaged(guid) || !loadingRows.Add(guid))
        {
            return;
        }

        string key = sourceKey;
        var config = Connection();
        // Rows are validated against the source's dialect, which the pending-changes poll may not have noted yet.
        ContentDraftFiles.Sources.Remember(key, config.Dialect);
        try
        {
            var rows = await DBSource.Open(config).ReadSpawnRowsAsync(SpawnKind.Creature, [guid], default);
            poses.Load(key, rows);
            RefreshSpawnFields();
        }
        catch (Exception)
        {
            Say("Could not read the creature's row; it cannot be moved yet.");
        }
        finally
        {
            loadingRows.Remove(guid);
        }
    }

    /// <summary>
    /// Restart on region changes while retaining same-map selection. Map changes clear
    /// source-specific selection.
    /// </summary>
    protected override void OnRegionChanged(bool mapChanged)
    {
        bool preserveSelection = region != null && !mapChanged;
        if (!preserveSelection)
        {
            point = null;
        }

        Restart(!preserveSelection);
    }

    /// <summary>A refresh keeps the markers until the new result is diffed in.</summary>
    protected override bool RefreshIsHard => false;

    /// <summary>
    /// Advance generation and cancel the query, awaiting its completion before starting another.
    /// Bound concurrency and reject stale results. A soft restart keeps the selection and markers.
    /// </summary>
    protected override void Restart(bool hard)
    {
        query.Restart();
        if (!hard)
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

        modelView.Clear();
        animationListGuid = null;
        if (animation != null)
        {
            animation.Disabled = true;
        }

        markers.Clear();
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

    void Publish(CreatureRegionResult result)
    {
        sourceKey = result.SourceKey;
        EventCatalog = result.Events ?? [];
        PhaseCatalog = result.Phases ?? PhaseCatalog.Empty;
        truncated = result.Truncated;
        databaseSpawns = result.Spawns;
        ReconcileSpawns(WithDraft());
        if (selectedGuid.HasValue && !records.Any(s => s.Guid == selectedGuid.Value))
        {
            Deselect();
        }

        PopulateList();
    }

    /// <summary>
    /// Publish only current queries, create bounded marker batches and reconcile models/list/gizmo
    /// state.
    /// </summary>
    public void Update(Camera3D camera)
    {
        if (LayerEnabled && region != null)
        {
            var next = ViewSpace.QueryPointAt(camera.Position, region.Map);
            if (point != next)
            {
                point = next;
                Restart(false);
            }
        }

        if (query.TakeSettledSearch())
        {
            Restart(true);
        }

        query.Poll(Publish, error => queryStatus = error);
        group.Sync(guid => markers.TryGetValue(guid, out var member) ? member.Node.Position : null);
        if (region != null && LayerEnabled && query.WantsStart)
        {
            var next = region;
            var path = Connection();
            var preview = Preview;
            var center = point;
            string term = filter.Text;
            query.Start(token => DBSource.Open(path).ReadCreaturesAsync(next, preview, center, term, token));
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
                if (selectWhenDrawn is ulong placed && markers.ContainsKey(placed))
                {
                    selectWhenDrawn = null;
                    Select(placed);
                    if (groupWhenDrawn != null)
                    {
                        foreach (ulong other in groupWhenDrawn.Where(markers.ContainsKey))
                        {
                            group.Add(other);
                            LoadRow(other);
                        }

                        groupWhenDrawn = null;
                        Select(placed);
                    }
                }

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

        modelView.Update(camera, markers.Values, Preview, LayerEnabled, selectedGuid, previewAnimation, Models);
        foreach (var batch in modelView.Batches.Values)
        {
            if (batch.Node.ModelReady)
            {
                foreach (ulong guid in batch.Guids)
                {
                    if (markers.TryGetValue(guid, out var marker))
                    {
                        marker.Body.Visible = false;
                        marker.Label.Position = new(0, modelView.LabelHeight(marker, batch.Node.Bounds), 0);
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
            bool nearby = LayerEnabled && modelView.IsAdmitted(marker.Spawn.Guid);
            if (marker.Node.Visible != nearby)
            {
                marker.Node.Visible = nearby;
            }

            bool showName = nearby && Preview.ShowNames && visibleNames < 64 && camera.GlobalPosition.DistanceSquaredTo(marker.Node.GlobalPosition) < 80 * 80;
            bool labelVisible = selectedGuid == marker.Spawn.Guid || showName;
            if (marker.Label.Visible != labelVisible)
            {
                marker.Label.Visible = labelVisible;
            }

            if (showName)
            {
                visibleNames++;
            }
        }

        UpdateInspectorActions();
        var editing = EditableMarker();
        gizmo.Update(camera, editing?.Node.GlobalPosition, editing?.Node.Rotation.Y ?? 0);
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
        foreach (ulong guid in markers.Keys.Where(g => !incoming.ContainsKey(g)).ToArray())
        {
            markers.Remove(guid);
            modelView.Forget(guid);
        }

        foreach (var spawn in spawns)
        {
            markers.Update(spawn.Guid, spawn);
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
        markers.Add(spawn, ViewPosition(pose), (float)pose.Heading);
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
    public void Select(ulong guid)
    {
        if (!markers.TryGetValue(guid, out var marker))
        {
            return;
        }

        if (selectedGuid != guid)
        {
            group.Clear();
        }

        if (selectedGuid.HasValue && markers.ContainsKey(selectedGuid.Value))
        {
            markers.Highlight(selectedGuid, guid);
        }

        if (selectedGuid != guid)
        {
            previewAnimation = 0;
            animationListGuid = null;
            modelView.ResetTimer();
            animation.Disabled = true;
        }

        selectedGuid = guid;
        SelectionChanged?.Invoke();
        LoadRow(guid);
        var spawn = marker.Spawn;
        var pose = PoseOf(spawn);
        selectionName.Text = spawn.Name;
        selectionMeta.Text = $"GUID {spawn.Guid}   /   Template {spawn.TemplateId}" + (group.Members.Count > 0 ? $"   (+{group.Members.Count} selected)" : "");
        details.Text = FormattableString.Invariant($"Map {pose.Map} · Display {spawn.DisplayId}\nX {pose.X:F2}    Y {pose.Y:F2}    Z {pose.Z:F2}\nHeading {PoseText.HeadingText(pose.Heading)}\n{SpawnPhasing.Describe(spawn.Phasing, spawn.PhaseMask, spawn.SpawnMask)}\n{spawn.Visibility?.Description}\n{(poses?.Conflict(sourceKey, guid, spawn.Pose) == true ? "Source position changed; draft requires review." : "")}");
        ShowPose(pose);
        list.DeselectAll();
        InspectorUi.SelectRow(list, guid);
        RefreshSpawnFields();
    }

    /// <summary>
    /// Resolve the effective local pose while retaining the immutable source baseline for conflict
    /// detection.
    /// </summary>
    WorldPose PoseOf(CreatureSpawn spawn) => poses?.Effective(sourceKey, spawn.Guid, spawn.Pose) ?? spawn.Pose;
    /// <summary>
    /// Require a writable local draft, selection and a hit on a gizmo handle before moving.
    /// </summary>
    public bool BeginDrag(Camera3D camera, Vector2 screen) => poses?.Available == true && selectedGuid.HasValue && gizmo.Begin(camera, screen);
    /// <summary>
    /// Update visual movement; persistence waits for completion of the gesture.
    /// </summary>
    public void Drag(Camera3D camera, Vector2 screen) => gizmo.Drag(camera, screen);
    /// <summary>
    /// Finish/cancel through the gizmo callback, committing at most one history command.
    /// </summary>
    public void EndDrag(bool commit) => gizmo.Finish(commit);
    void MoveSelection(GizmoEdit edit) => poseEditor.MoveSelection(edit);

    /// <summary>
    /// Record one draft command for a creature and reconcile its marker/models from effective
    /// history. Failed saves or stale baselines keep the persisted pose.
    /// </summary>
    void Commit(CreatureSpawn spawn, WorldPose after, string success) => poseEditor.Commit(spawn, after, success);

    /// <summary>Move a marker and its native model placements to the effective draft pose.</summary>
    void ApplyPose(ulong guid) => poseEditor.ApplyPose(guid);

    static Vector3 ViewPosition(WorldPose pose) => ViewSpace.ToView(pose.X, pose.Y, pose.Z);

    /// <summary>
    /// Cancel the active preview, persist undo/redo and reconcile marker/native poses from
    /// effective history.
    /// </summary>
    void History(bool redo) => poseEditor.History(redo);

    void Resync() => poseEditor.Resync();

    public (ulong Guid, float Distance)? FindHit(Camera3D camera, Vector2 screen)
    {
        if (!LayerEnabled)
        {
            return null;
        }

        var ray = PickRay.From(camera, screen);
        Vector3 origin = ray.Origin, direction = ray.Direction;
        var modelGuids = new HashSet<ulong>();
        var model = ScenePicking.NearestInBatches(ray, modelView.Batches.Values, modelGuids);
        ulong? nearest = model?.Guid;
        float distance = model?.Distance ?? float.MaxValue;

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


    Node ICreaturePoseHost.HostNode => this;
    SpawnPoses? ICreaturePoseHost.Poses => poses;
    string ICreaturePoseHost.SourceKey => sourceKey;
    bool ICreaturePoseHost.HasGroup => group.Members.Count > 0;
    IReadOnlyList<ulong> ICreaturePoseHost.MarkerKeys => markers.Keys.ToArray();
    GroundHit? ICreaturePoseHost.LastGround { get => LastGround; set => LastGround = value; }
    bool ICreaturePoseHost.TryMarker(ulong guid, out CreatureMarkers.Marker marker) => markers.TryGetValue(guid, out marker);
    WorldPose ICreaturePoseHost.PoseOf(CreatureSpawn spawn) => PoseOf(spawn);
    void ICreaturePoseHost.PreviewPose(ulong guid, Vector3 position, float yaw) => PreviewPose(guid, position, yaw);
    void ICreaturePoseHost.Say(string message) => Say(message);
    void ICreaturePoseHost.PopulateList() => PopulateList();
    IReadOnlyList<CreatureSpawn> ICreaturePoseHost.WithDraft() => WithDraft();
    void ICreaturePoseHost.ReconcileSpawns(IReadOnlyList<CreatureSpawn> spawns) => ReconcileSpawns(spawns);
    bool ICreaturePoseHost.MoveGroup(string success, WorldPose before, WorldPose after) => MoveGroup(success, before, after);

    /// <summary>Move a marker and its native model for a gizmo preview or a committed pose.</summary>
    void PreviewPose(ulong guid, Vector3 position, float yaw)
    {
        if (!markers.TryGetValue(guid, out var marker))
        {
            return;
        }

        marker.Node.Position = position;
        marker.Node.Rotation = new(0, yaw, 0);
        modelView.Place(guid, marker);
    }

    /// <summary>
    /// Cancel read-only queries on scene exit.
    /// </summary>
    public override void _ExitTree()
    {
        query.Dispose();
    }
}
