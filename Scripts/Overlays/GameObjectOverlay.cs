using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;
/// <summary>
/// Bounded read-only GO projection. Only admitted nearby objects request native meshes; all SQL
/// records remain detached, and switching filters/layers cancels obsolete generations.
/// </summary>
public partial class GameObjectOverlay : Node3D
{
    public ModelLibrary Models { get; set; } = null!;
    public Func<DatabaseSettings> Connection { get; set; } = () => DatabaseSettings.Empty;
    public SpawnPreview Preview { get; private set; } = new();
    public bool LayerEnabled { get; private set; } = true;
    public IReadOnlyList<GameObjectSpawn> Records => records;
    public WorldEventInfo[] EventCatalog { get; private set; } = [];
    public uint? SelectedGuid => selected;
    public int ModelObjectCount => batches.Values.Where(b => b.Node.ModelReady).Sum(b => b.Guids.Length);
    public int AdmittedObjectCount { get; private set; }
    public bool IsLoading => query.IsLoading;

    public event Action<WorldPose>? FocusRequested;
    public event Action? SelectionChanged;
    IReadOnlyList<GameObjectSpawn> records = [];
    readonly Dictionary<uint, GameObjectSpawn> lookup = [];
    readonly Dictionary<uint, string?> paths = [];
    readonly Dictionary<ModelKey, (ModelInstance Node, uint[] Guids)> batches = [];
    readonly Dictionary<uint, MeshInstance3D> fallbacks = [];
    readonly Dictionary<uint, Label3D> names = [];
    readonly BoxMesh fallbackMesh = new()
    {
        Size = Vector3.One
    };
    readonly StandardMaterial3D fallbackMaterial = new()
    {
        AlbedoColor = new("e4b56c")
    };
    Label3D selectionLabel = null!;
    uint? selected;
    CreatureRegion? region;
    SpawnQueryPoint? point;
    readonly RegionQuery<GameObjectRegionResult> query = new();
    long modelUpdate;
    ItemList list = null!;
    LineEdit search = null!;
    Label status = null!, title = null!, identity = null!, details = null!;
    Button fly = null!;
    string queryStatus = "Waiting for a world…";
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
    }

    public void SetRegion(CreatureRegion? value)
    {
        if (region == value)
        {
            return;
        }

        if (region?.Map != value?.Map)
        {
            point = null;
        }

        region = value;
        Restart();
    }

    public void SetPreview(SpawnPreview value)
    {
        value.Validate();
        Preview = value;
        Restart();
    }

    public void SetLayerEnabled(bool value)
    {
        if (LayerEnabled != value)
        {
            LayerEnabled = value;
            Restart();
        }
    }

    public void Refresh() => Restart();
    /// <summary>
    /// Cancel and reissue the query. `hard` wipes every native model/fallback/label and the record
    /// cache (used for an explicit filter/map/layer change); the routine camera-driven requery in
    /// Update() uses `hard: false` so the existing display stays exactly as it is until the new
    /// result arrives, which Reconcile() then diffs in rather than rebuilding from nothing. Without
    /// this, flying triggered a visible "clear and reload" every time the quantized query center
    /// shifted, even though almost the same objects were still in range.
    /// </summary>
    void Restart(bool hard = true)
    {
        query.Restart();
        if (hard)
        {
            AdmittedObjectCount = 0;
            foreach (var batch in batches.Values)
            {
                batch.Node.Visible = false;
                batch.Node.QueueFree();
            }

            foreach (var node in fallbacks.Values)
            {
                node.Visible = false;
                node.QueueFree();
            }

            foreach (var node in names.Values)
            {
                node.Visible = false;
                node.QueueFree();
            }

            names.Clear();
            batches.Clear();
            fallbacks.Clear();
            paths.Clear();
            lookup.Clear();
            records = [];
            list?.Clear();
            Deselect();
            modelUpdate = 0;
            queryStatus = LayerEnabled ? "Loading nearby objects…" : "GameObject layer hidden";
        }
        else
        {
            queryStatus = LayerEnabled ? (records.Count > 0 ? "Refreshing nearby objects…" : "Loading nearby objects…") : "GameObject layer hidden";
        }
    }

    public void Update(Camera3D camera)
    {
        if (LayerEnabled && region != null)
        {
            var server = WorldCoordinates.ToServer(new(camera.Position.X, camera.Position.Y, camera.Position.Z));
            var next = SpawnQueryPoint.FromPose(new(region.Map, server.X, server.Y, server.Z, 0));
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
            query.Start(token => new GameObjectSource(config).ReadAsync(area, preview, center, term, token));
        }

        if (LayerEnabled && Models.GameObjects != null && System.Diagnostics.Stopwatch.GetElapsedTime(modelUpdate).TotalSeconds > 0.2)
        {
            Reconcile(camera);
            modelUpdate = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        status.Text = queryStatus;
        fly.Disabled = !selected.HasValue || !lookup.ContainsKey(selected.Value);
        selectionLabel.Visible = LayerEnabled && selected.HasValue && lookup.ContainsKey(selected.Value);
    }

    void Publish(GameObjectRegionResult result)
    {
        var incoming = result.Spawns.ToDictionary(s => s.Guid);
        // Drop cached objects no longer in range; Reconcile()'s own diffing then removes
        // their native model/fallback/label on its next tick without needing a full wipe.
        foreach (uint guid in lookup.Keys.Where(g => !incoming.ContainsKey(g)).ToArray())
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
        var admitted = records.Where(s => selected == s.Guid || ViewPosition(s).DistanceSquaredTo(camera.Position) <= Preview.ViewDistance * Preview.ViewDistance).OrderBy(s => selected == s.Guid ? 0 : 1).ThenBy(s => ViewPosition(s).DistanceSquaredTo(camera.Position)).Take(Preview.ModelLimit).ToArray();
        AdmittedObjectCount = admitted.Length;
        Dictionary<ModelKey, List<uint>> groups = [];
        HashSet<uint> missing = [];
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

            pair.Value.Node.Visible = false;
            pair.Value.Node.QueueFree();
            batches.Remove(pair.Key);
        }

        foreach (var pair in groups)
        {
            if (!batches.ContainsKey(pair.Key))
            {
                batches[pair.Key] = (Models.Attach(this, pair.Key, pair.Value.Select(guid => ModelTransform(lookup[guid])).ToArray()), pair.Value.ToArray());
            }

            if (!batches[pair.Key].Node.ModelReady)
            {
                foreach (uint guid in pair.Value)
                {
                    missing.Add(guid);
                }
            }
        }

        foreach (var pair in fallbacks.ToArray())
        {
            if (!missing.Contains(pair.Key))
            {
                pair.Value.Visible = false;
                pair.Value.QueueFree();
                fallbacks.Remove(pair.Key);
            }
        }

        foreach (uint guid in missing)
        {
            if (fallbacks.ContainsKey(guid))
            {
                continue;
            }

            var node = new MeshInstance3D
            {
                Mesh = fallbackMesh,
                MaterialOverride = fallbackMaterial,
                Position = ViewPosition(lookup[guid]) + Vector3.Up * 0.5f,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            AddChild(node);
            fallbacks[guid] = node;
        }

        // Labels are optional and cost more draw calls than instanced meshes. Bound them separately.
        var named = Preview.ShowNames ? admitted.Where(s => s.Guid != selected && ViewPosition(s).DistanceSquaredTo(camera.Position) < 80 * 80).Take(64).ToArray() : [];
        var namedGuids = named.Select(s => s.Guid).ToHashSet();
        foreach (var pair in names.ToArray())
        {
            if (!namedGuids.Contains(pair.Key))
            {
                pair.Value.Visible = false;
                pair.Value.QueueFree();
                names.Remove(pair.Key);
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
                Position = ViewPosition(spawn) + Vector3.Up * 2,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 28,
                PixelSize = 0.014f
            };
            AddChild(label);
            names[spawn.Guid] = label;
        }
    }

    public static Vector3 ViewPosition(GameObjectSpawn spawn)
    {
        var point = WorldCoordinates.ToView(new(spawn.Pose.X, spawn.Pose.Y, spawn.Pose.Z));
        return new((float)point.X, (float)point.Y, (float)point.Z);
    }

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

    public void Select(uint guid)
    {
        if (!LayerEnabled || !lookup.TryGetValue(guid, out var spawn))
        {
            return;
        }

        selected = guid;
        modelUpdate = 0;
        SelectionChanged?.Invoke();
        title.Text = spawn.Name;
        identity.Text = $"GUID {guid}   /   Template {spawn.TemplateId}";
        details.Text = FormattableString.Invariant($"Map {spawn.Pose.Map} · Display {spawn.DisplayId} · Type {spawn.Type}\nX {spawn.Pose.X:F2}    Y {spawn.Pose.Y:F2}    Z {spawn.Pose.Z:F2}\nScale {spawn.Scale:F2} · DB state {spawn.State}\nPhase 0x{spawn.PhaseMask:X} · Difficulty 0x{spawn.SpawnMask:X}\n{spawn.Visibility.Description}");
        selectionLabel.Text = spawn.Name;
        selectionLabel.Position = ViewPosition(spawn) + Vector3.Up * 2;
        list.DeselectAll();
        InspectorUi.SelectRow(list, guid, reveal: false);
    }

    public void Deselect()
    {
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
        }
    }

    public (uint Guid, float Distance)? FindHit(Camera3D camera, Vector2 screen)
    {
        // Preserve the ray parameter across inverse-scaled batches so GO/creature hits are
        // comparable. This is bounding-box selection, without terrain or triangle occlusion.
        if (!LayerEnabled)
        {
            return null;
        }

        var origin = camera.ProjectRayOrigin(screen);
        var direction = camera.ProjectRayNormal(screen);
        uint? nearest = null;
        float distance = float.MaxValue;
        foreach (var batch in batches.Values)
        {
            if (!batch.Node.ModelReady)
            {
                continue;
            }

            for (int i = 0; i < batch.Guids.Length; i++)
            {
                var inverse = batch.Node.Transforms![i].AffineInverse();
                var hit = ScenePicking.BoxHit(batch.Node.Bounds, inverse * origin, inverse.Basis * direction);
                if (hit.HasValue && hit.Value < distance && hit.Value < camera.Far)
                {
                    nearest = batch.Guids[i];
                    distance = hit.Value;
                }
            }
        }

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

    public override void _ExitTree() => query.Dispose();
}
