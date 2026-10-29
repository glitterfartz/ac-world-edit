using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;
/// <summary>
/// Bounded read-only GO projection. Only admitted nearby objects request native meshes; all SQL
/// records remain detached, and switching filters/layers cancels obsolete generations.
/// </summary>
public partial class GameObjectOverlay : RegionOverlay
{
    public GameObjectOverlay() : base(true, "Waiting for a world…")
    {
    }

    public ModelLibrary Models { get; set; } = null!;
    public SpawnPreview Preview { get; private set; } = new();
    public IReadOnlyList<GameObjectSpawn> Records => records;
    public WorldEventInfo[] EventCatalog { get; private set; } = [];
    public ulong? SelectedGuid => selected;
    public int ModelObjectCount => batches.Values.Where(b => b.Node.ModelReady).Sum(b => b.Guids.Length);
    public int AdmittedObjectCount { get; private set; }
    public bool IsLoading => query.IsLoading;

    public event Action<WorldPose>? FocusRequested;
    public event Action? SelectionChanged;
    IReadOnlyList<GameObjectSpawn> records = [];
    readonly Dictionary<ulong, GameObjectSpawn> lookup = [];
    readonly Dictionary<uint, string?> paths = [];
    readonly Dictionary<ModelKey, (ModelInstance Node, ulong[] Guids)> batches = [];
    readonly Dictionary<ulong, MeshInstance3D> fallbacks = [];
    readonly Dictionary<ulong, Label3D> names = [];
    readonly BoxMesh fallbackMesh = new()
    {
        Size = Vector3.One
    };
    readonly StandardMaterial3D fallbackMaterial = new()
    {
        AlbedoColor = new("e4b56c")
    };
    Label3D selectionLabel = null!;
    ulong? selected;
    readonly RegionQuery<GameObjectRegionResult> query = new();
    long modelUpdate;
    ItemList list = null!;
    LineEdit search = null!;
    Label status = null!, title = null!, identity = null!, details = null!;
    Button fly = null!;
    public override void _Ready()
    {
        selectionLabel = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 28,
            PixelSize = 0.014f,
            Visible = false
        };
        AddChild(selectionLabel);
        CreateGizmo();
    }

    protected override void OnRegionChanged(bool mapChanged)
    {
        if (mapChanged)
        {
            point = null;
        }

        Restart(true);
    }

    public void SetPreview(SpawnPreview value)
    {
        value.Validate();
        Preview = value;
        Restart();
    }

    /// <summary>
    /// Cancel and reissue the query. `hard` wipes every native model/fallback/label and the record
    /// cache (used for an explicit filter/map/layer change); the routine camera-driven requery in
    /// Update() uses `hard: false` so the existing display stays exactly as it is until the new
    /// result arrives, which Reconcile() then diffs in rather than rebuilding from nothing. Without
    /// this, flying triggered a visible "clear and reload" every time the quantized query center
    /// shifted, even though almost the same objects were still in range.
    /// </summary>
    protected override void Restart(bool hard = true)
    {
        query.Restart();
        if (hard)
        {
            AdmittedObjectCount = 0;
            batches.FreeAll(b => b.Node);
            fallbacks.FreeAll(n => n);
            names.FreeAll(n => n);
            paths.Clear();
            lookup.Clear();
            records = [];
            list?.Clear();
            Deselect();
            modelUpdate = 0;
        }

        queryStatus = !LayerEnabled ? "GameObject layer hidden"
            : !hard && records.Count > 0 ? "Refreshing nearby objects…"
            : "Loading nearby objects…";
    }

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
            Restart();
        }

        query.Poll(Publish, error => queryStatus = error);
        if (LayerEnabled && region != null && query.WantsStart)
        {
            var area = region;
            var preview = Preview;
            var center = point;
            string term = search.Text;
            var config = Connection();
            query.Start(token => DBSource.Open(config).ReadGameObjectsAsync(area, preview, center, term, token));
        }

        if (LayerEnabled && Models.GameObjects != null && System.Diagnostics.Stopwatch.GetElapsedTime(modelUpdate).TotalSeconds > 0.2)
        {
            Reconcile(camera);
            modelUpdate = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        status.Text = queryStatus;
        fly.Disabled = !selected.HasValue || !lookup.ContainsKey(selected.Value);
        selectionLabel.Visible = LayerEnabled && selected.HasValue && lookup.ContainsKey(selected.Value);
        UpdateInspectorActions();
        var editing = EditableSpawn();
        gizmo.Update(camera, editing is { } spawn ? ViewPosition(spawn) : null, editing is { } e ? (float)e.Pose.Heading : 0);
    }

    void Publish(GameObjectRegionResult result)
    {
        sourceKey = result.SourceKey;
        var incoming = result.Spawns.ToDictionary(s => s.Guid);
        // Drop cached objects no longer in range; Reconcile()'s own diffing then removes
        // their native model/fallback/label on its next tick without needing a full wipe.
        foreach (ulong guid in lookup.Keys.Where(g => !incoming.ContainsKey(g)).ToArray())
        {
            lookup.Remove(guid);
        }

        records = result.Spawns;
        EventCatalog = result.Events;
        foreach (var spawn in records)
        {
            lookup[spawn.Guid] = spawn;
        }

        Populate();
        queryStatus = result.Truncated ? $"{records.Count} nearest results · limit reached\nNarrow the distance or search." : $"{records.Count} matches in this neighborhood";
    }

    /// <summary>
    /// Doors and buttons show their spawn state through native clips: GO_STATE_READY (1) is
    /// closed, ACTIVE (0) and ACTIVE_ALTERNATIVE (2) are open (GameObjectData.h GOState). Effect
    /// "doors" such as the Ruby Sanctum flame rings fade to alpha zero in Opened. Models without
    /// these clips play Stand. Other types and live script/state changes are not simulated.
    /// </summary>
    static ushort StateAnimation(GameObjectSpawn spawn) => spawn.Type is 0 or 1 ? (spawn.State == 1 ? (ushort)147 : (ushort)149) : (ushort)0;

    /// <summary>128 default native instances, nearest first. Unadmitted records create no nodes or model requests.</summary>
    void Reconcile(Camera3D camera)
    {
        float viewDistanceSquared = Preview.ViewDistance * Preview.ViewDistance;
        var admitted = records
            .Select(s => (Spawn: s, Distance: ViewPosition(Effective(s)).DistanceSquaredTo(camera.Position)))
            .Where(c => selected == c.Spawn.Guid || c.Distance <= viewDistanceSquared)
            .OrderBy(c => selected == c.Spawn.Guid ? 0 : 1).ThenBy(c => c.Distance)
            .Take(Preview.ModelLimit)
            .Select(c => c.Spawn)
            .ToArray();
        AdmittedObjectCount = admitted.Length;
        Dictionary<ModelKey, List<ulong>> groups = [];
        HashSet<ulong> missing = [];
        foreach (var spawn in admitted)
        {
            if (!paths.TryGetValue(spawn.DisplayId, out var path))
            {
                try
                {
                    path = Models.GameObjects!.Resolve(spawn.DisplayId);
                }
                catch (Exception)
                {
                    path = null;
                    GD.PushWarning($"No native GameObject model for display {spawn.DisplayId}; using a bounded fallback.");
                }

                paths[spawn.DisplayId] = path;
            }

            if (path == null)
            {
                missing.Add(spawn.Guid);
                continue;
            }

            var key = new ModelKey(path, Animation: StateAnimation(spawn));
            if (!groups.TryGetValue(key, out var guids))
            {
                groups[key] = guids = [];
            }

            guids.Add(spawn.Guid);
        }

        foreach (var pair in batches.ToArray())
        {
            // Compare membership, not order: distance-sorted candidates reorder every tick while
            // flying even when the visible set is unchanged, and rebuilding the batch on pure
            // reordering is what flickers the fallback marker back in for no reason.
            if (groups.TryGetValue(pair.Key, out var guids) && guids.Count == pair.Value.Guids.Length && guids.ToHashSet().SetEquals(pair.Value.Guids))
            {
                continue;
            }

            batches.Free(pair.Key, b => b.Node);
        }

        foreach (var pair in groups)
        {
            if (!batches.ContainsKey(pair.Key))
            {
                batches[pair.Key] = (Models.Attach(this, pair.Key, pair.Value.Select(guid => ModelTransform(Effective(lookup[guid]))).ToArray()), pair.Value.ToArray());
            }

            if (!batches[pair.Key].Node.ModelReady)
            {
                foreach (ulong guid in pair.Value)
                {
                    missing.Add(guid);
                }
            }
        }

        foreach (var pair in fallbacks.ToArray())
        {
            if (!missing.Contains(pair.Key))
            {
                fallbacks.Free(pair.Key, n => n);
            }
        }

        foreach (ulong guid in missing)
        {
            if (fallbacks.ContainsKey(guid))
            {
                continue;
            }

            var node = new MeshInstance3D
            {
                Mesh = fallbackMesh,
                MaterialOverride = fallbackMaterial,
                Position = ViewPosition(Effective(lookup[guid])) + Vector3.Up * 0.5f,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            AddChild(node);
            fallbacks[guid] = node;
        }

        // Labels are optional and cost more draw calls than instanced meshes. Bound them separately.
        var named = Preview.ShowNames ? admitted.Where(s => s.Guid != selected && ViewPosition(Effective(s)).DistanceSquaredTo(camera.Position) < 80 * 80).Take(64).ToArray() : [];
        var namedGuids = named.Select(s => s.Guid).ToHashSet();
        foreach (var pair in names.ToArray())
        {
            if (!namedGuids.Contains(pair.Key))
            {
                names.Free(pair.Key, n => n);
            }
        }

        foreach (var spawn in named)
        {
            if (names.ContainsKey(spawn.Guid))
            {
                continue;
            }

            var label = new Label3D
            {
                Text = spawn.Name,
                Position = ViewPosition(Effective(spawn)) + Vector3.Up * 2,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 28,
                PixelSize = 0.014f
            };
            AddChild(label);
            names[spawn.Guid] = label;
        }
    }

    public static Vector3 ViewPosition(GameObjectSpawn spawn) => ViewSpace.ToView(spawn.Pose.X, spawn.Pose.Y, spawn.Pose.Z);

    public static Transform3D ModelTransform(GameObjectSpawn spawn)
    {
        // Conjugate native Z-up rotation into the same (-Y, Z, -X) basis as model vertices.
        // Database quaternions do not use the half-turn correction needed by ADT placements.
        var q = spawn.Rotation;
        var basis = new Basis(new Quaternion(-q.Y, q.Z, -q.X, q.W)).Scaled(Vector3.One * spawn.Scale);
        return new(basis, ViewPosition(spawn));
    }

    void Populate()
    {
        list.Clear();
        string term = search.Text.Trim();
        foreach (var spawn in records.Where(s => $"{s.Name} {s.Guid} {s.TemplateId}".Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            int index = list.AddItem($"{spawn.Name}   ·   {spawn.Guid}");
            list.SetItemMetadata(index, spawn.Guid.ToString());
            list.SetItemTooltip(index, $"GUID {spawn.Guid} · Template {spawn.TemplateId}\n{spawn.Visibility.Description}");
        }
    }

    public void Select(ulong guid)
    {
        if (!LayerEnabled || !lookup.ContainsKey(guid))
        {
            return;
        }

        selected = guid;
        modelUpdate = 0;
        SelectionChanged?.Invoke();
        LoadRow(guid);
        ShowSelection();
        list.DeselectAll();
        InspectorUi.SelectRow(list, guid, reveal: false);
    }

    /// <summary>Fill the inspector from the selected spawn's effective (draft) placement.</summary>
    void ShowSelection()
    {
        if (!selected.HasValue || !lookup.TryGetValue(selected.Value, out var source))
        {
            return;
        }

        var spawn = Effective(source);
        title.Text = spawn.Name;
        identity.Text = $"GUID {spawn.Guid}   /   Template {spawn.TemplateId}";
        string conflict = poses?.Conflict(sourceKey, spawn.Guid, GameObjectPlacement.From(source)) == true ? "\nSource placement changed; draft requires review." : "";
        details.Text = FormattableString.Invariant($"Map {spawn.Pose.Map} · Display {spawn.DisplayId} · Type {spawn.Type}\nX {spawn.Pose.X:F2}    Y {spawn.Pose.Y:F2}    Z {spawn.Pose.Z:F2}\nHeading {PoseText.HeadingText(spawn.Pose.Heading)}\nScale {spawn.Scale:F2} · DB state {spawn.State}\n{SpawnPhasing.Describe(spawn.Phasing, spawn.PhaseMask, spawn.SpawnMask)}\n{spawn.Visibility.Description}{conflict}");
        selectionLabel.Text = spawn.Name;
        selectionLabel.Position = ViewPosition(spawn) + Vector3.Up * 2;
        ShowPose(spawn.Pose);
    }

    public void Deselect()
    {
        // Cancel while the selection still exists so an unfinished move restores its placement.
        gizmo?.Finish(false);
        selected = null;
        list?.DeselectAll();
        if (selectionLabel != null)
        {
            selectionLabel.Visible = false;
        }

        if (title != null)
        {
            title.Text = "Select a GameObject";
            identity.Text = "Click a model or choose one above";
            details.Text = "Selection details appear here.";
            ShowPose(null);
        }
    }

    public (ulong Guid, float Distance)? FindHit(Camera3D camera, Vector2 screen)
    {
        // Preserve the ray parameter across inverse-scaled batches so GO/creature hits are
        // comparable. This is bounding-box selection, without terrain or triangle occlusion.
        if (!LayerEnabled)
        {
            return null;
        }

        var ray = PickRay.From(camera, screen);
        var (origin, direction) = (ray.Origin, ray.Direction);
        var model = ScenePicking.NearestInBatches(ray, batches.Values);
        ulong? nearest = model?.Guid;
        float distance = model?.Distance ?? float.MaxValue;

        foreach (var pair in fallbacks)
        {
            var hit = ScenePicking.BoxHit(new Aabb(pair.Value.Position - Vector3.One * 0.5f, Vector3.One), origin, direction);
            if (hit.HasValue && hit.Value < distance)
            {
                nearest = pair.Key;
                distance = hit.Value;
            }
        }

        return nearest.HasValue ? (nearest.Value, distance) : null;
    }

    public override void _ExitTree()
    {
        query.Dispose();
    }
}
