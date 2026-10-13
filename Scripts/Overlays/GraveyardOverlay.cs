using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Bounded projection of game_graveyard rows near the streamed region: a billboard tombstone icon,
/// a faction-colored ground ring and a name label per graveyard. Source rows stay detached and
/// read-only; GraveyardDraft owns durable local edits. Hidden layers stop queries and free markers.
/// </summary>
public partial class GraveyardOverlay : Node3D
{
    /// <summary>
    /// Icon height above the graveyard point, in yards.
    /// </summary>
    const float IconHeight = 4;
    const float LabelHeight = 6.2f;
    static readonly Color AllianceColor = new("4a90ff"), HordeColor = new("e8484a"), NeutralColor = new("c0c4c8"), MixedColor = new("f2f2f2"), UnlinkedColor = new("6b6f75"), SelectedColor = new("ffcf69");

    public Func<string> ConnectionPath { get; set; } = () => CreatureSource.DefaultConfigPath;
    public Func<AreaLookup?> Areas { get; set; } = () => null;
    public bool LayerEnabled { get; private set; } = true;
    public uint? SelectedId => selected;
    public IReadOnlyList<GraveyardSite> Sites => sites;
    public bool IsLoading => pending != null;
    public string QueryStatus => queryStatus;
    public event Action? SelectionChanged;
    public event Action<WorldPose>? FocusRequested;

    readonly Dictionary<uint, GraveyardSite> lookup = [];
    readonly Dictionary<uint, (Node3D Node, Sprite3D Icon, MeshInstance3D Ring, Label3D Label)> markers = [];
    IReadOnlyList<GraveyardSite> sites = [];
    ImageTexture icon = null!;
    uint? selected;
    CreatureRegion? region;
    SpawnQueryPoint? point;
    Task<GraveyardRegionResult>? pending;
    CancellationTokenSource cancellation = new();
    int generation, pendingGeneration, requested = -1;
    bool searchPending;
    long searchChanged;
    string queryStatus = "Waiting for a world…";
    string sourceKey = "";

    public override void _Ready()
    {
        icon = TombstoneIcon();
        OpenDraft();
    }

    /// <summary>
    /// Draw the editor's tombstone glyph (64x64, white with a dark outline) so it can be tinted per
    /// state. This is an editor icon, not a client asset.
    /// </summary>
    static ImageTexture TombstoneIcon()
    {
        const int size = 64;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        bool Inside(float x, float y)
        {
            // Rounded-top slab 34 px wide, 46 px tall, sitting on a 44 px base.
            float cx = 32, top = 22, half = 17;
            bool body = x >= cx - half && x <= cx + half && y >= top && y <= 54;
            bool cap = (x - cx) * (x - cx) + (y - top) * (y - top) <= half * half && y < top;
            bool foot = x >= 10 && x <= 54 && y >= 54 && y <= 60;
            return body || cap || foot;
        }

        bool Cross(float x, float y) => (x >= 29 && x <= 35 && y >= 18 && y <= 44) || (x >= 22 && x <= 42 && y >= 25 && y <= 31);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inside = Inside(x + 0.5f, y + 0.5f);
                bool outline = !inside && (Inside(x - 1.5f, y + 0.5f) || Inside(x + 2.5f, y + 0.5f) || Inside(x + 0.5f, y - 1.5f) || Inside(x + 0.5f, y + 2.5f));
                var color = inside ? (Cross(x + 0.5f, y + 0.5f) ? new Color(0.55f, 0.58f, 0.62f) : Colors.White) : outline ? new Color(0.05f, 0.06f, 0.08f) : Colors.Transparent;
                image.SetPixel(x, y, color);
            }
        }

        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    public void SetRegion(CreatureRegion? value)
    {
        if (region == value)
        {
            return;
        }

        bool mapChanged = region?.Map != value?.Map;
        region = value;
        // Re-center nearest-first ordering on the camera at the next update.
        point = null;
        Restart(mapChanged);
    }

    public void SetLayerEnabled(bool value)
    {
        if (LayerEnabled != value)
        {
            LayerEnabled = value;
            Restart(true);
        }
    }

    public void Refresh() => Restart(true);

    /// <summary>
    /// Cancel and reissue the query. A hard restart (map, layer or search change) frees every marker
    /// and selection; a region step keeps the display until the new result arrives.
    /// </summary>
    void Restart(bool hard)
    {
        generation++;
        cancellation.Cancel();
        cancellation.Dispose();
        cancellation = new();
        if (hard)
        {
            Deselect();
            foreach (var marker in markers.Values)
            {
                marker.Node.Visible = false;
                marker.Node.QueueFree();
            }

            markers.Clear();
            lookup.Clear();
            sites = [];
            list?.Clear();
        }

        queryStatus = LayerEnabled ? "Loading graveyards…" : "Graveyard layer hidden";
    }

    public void Update(Camera3D camera)
    {
        if (LayerEnabled && region != null)
        {
            var server = WorldCoordinates.ToServer(new(camera.Position.X, camera.Position.Y, camera.Position.Z));
            point ??= SpawnQueryPoint.FromPose(new(region.Map, server.X, server.Y, server.Z, 0));
        }

        if (searchPending && System.Diagnostics.Stopwatch.GetElapsedTime(searchChanged).TotalSeconds >= 0.3)
        {
            searchPending = false;
            Restart(true);
        }

        if (pending is { IsCompleted: true })
        {
            try
            {
                var result = pending.GetAwaiter().GetResult();
                if (pendingGeneration == generation)
                {
                    Publish(result);
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

        if (LayerEnabled && requested != generation && pending == null && region != null)
        {
            requested = pendingGeneration = generation;
            var area = region;
            var center = point;
            string term = search.Text, config = ConnectionPath();
            var token = cancellation.Token;
            pending = Task.Run(() => new GraveyardSource(config).ReadAsync(area, center, term, token), token);
        }

        foreach (var (id, marker) in markers)
        {
            marker.Node.Visible = LayerEnabled;
            marker.Label.Visible = LayerEnabled && (selected == id || camera.GlobalPosition.DistanceSquaredTo(marker.Node.GlobalPosition) < 250 * 250);
        }

        status.Text = queryStatus;
        UpdateInspectorActions();
        gizmo.Update(camera, EditableMarker()?.Node.GlobalPosition, 0);
    }

    /// <summary>
    /// Replace the source snapshot and reconcile markers: keep nodes for surviving IDs, free the
    /// rest and create new ones. The selection survives when its graveyard is still present.
    /// </summary>
    void Publish(GraveyardRegionResult result)
    {
        sourceKey = result.SourceKey;
        sites = result.Sites;
        var incoming = result.Sites.ToDictionary(s => s.Record.Id);
        foreach (uint id in markers.Keys.Where(id => !incoming.ContainsKey(id)).ToArray())
        {
            if (selected == id)
            {
                Deselect();
            }

            markers[id].Node.QueueFree();
            markers.Remove(id);
        }

        lookup.Clear();
        foreach (var site in result.Sites)
        {
            lookup[site.Record.Id] = site;
            if (!markers.ContainsKey(site.Record.Id))
            {
                AddMarker(site);
            }

            ApplyRecord(site.Record.Id);
        }

        PopulateList();
        queryStatus = result.Sites.Length == 0 ? "No graveyards in this region." : result.Truncated ? $"{result.Sites.Length} nearest graveyards · limit reached" : $"{result.Sites.Length} graveyards in this region";
        if (selected.HasValue)
        {
            Select(selected.Value);
        }
    }

    void AddMarker(GraveyardSite site)
    {
        var node = new Node3D
        {
            Name = "Graveyard_" + site.Record.Id
        };
        var sprite = new Sprite3D
        {
            Texture = icon,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            PixelSize = 0.035f,
            Position = new(0, IconHeight, 0),
            Shaded = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard
        };
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh
            {
                InnerRadius = 2.4f,
                OuterRadius = 2.8f,
                Rings = 48,
                RingSegments = 6
            },
            Position = new(0, 0.15f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            }
        };
        var label = new Label3D
        {
            Position = new(0, LabelHeight, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 30,
            PixelSize = 0.018f,
            OutlineSize = 8
        };
        node.AddChild(ring);
        node.AddChild(sprite);
        node.AddChild(label);
        AddChild(node);
        markers[site.Record.Id] = (node, sprite, ring, label);
    }

    /// <summary>
    /// Move a marker to its effective (draft) record and refresh its label and colors.
    /// </summary>
    void ApplyRecord(uint id)
    {
        if (!markers.TryGetValue(id, out var marker) || !lookup.TryGetValue(id, out var site))
        {
            return;
        }

        var record = RecordOf(site);
        marker.Node.Position = ViewPosition(record);
        marker.Label.Text = $"{(record.Comment.Length > 0 ? record.Comment : "Graveyard")}\n#{record.Id}";
        var faction = FactionColor(record);
        ((StandardMaterial3D)marker.Ring.MaterialOverride).AlbedoColor = faction;
        marker.Icon.Modulate = selected == id ? SelectedColor : faction.Lerp(Colors.White, 0.45f);
    }

    static Color FactionColor(GraveyardRecord record)
    {
        if (record.Links.Length == 0)
        {
            return UnlinkedColor;
        }

        var factions = record.Links.Select(l => l.Faction).Distinct().ToArray();
        return factions.Length > 1 ? MixedColor : factions[0] switch
        {
            GraveyardLink.Alliance => AllianceColor,
            GraveyardLink.Horde => HordeColor,
            _ => NeutralColor
        };
    }

    static Vector3 ViewPosition(GraveyardRecord record)
    {
        var p = WorldCoordinates.ToView(new(record.X, record.Y, record.Z));
        return new((float)p.X, (float)p.Y, (float)p.Z);
    }

    public void Select(uint id)
    {
        if (!lookup.ContainsKey(id))
        {
            return;
        }

        uint? previous = selected;
        selected = id;
        if (previous.HasValue && previous != id)
        {
            ApplyRecord(previous.Value);
        }

        ApplyRecord(id);
        if (previous != id)
        {
            SelectionChanged?.Invoke();
        }

        ShowSelection();
    }

    public void Deselect()
    {
        gizmo?.Finish(false);
        uint? previous = selected;
        selected = null;
        if (previous.HasValue)
        {
            ApplyRecord(previous.Value);
        }

        list?.DeselectAll();
        if (title != null)
        {
            ShowSelection();
        }
    }

    /// <summary>
    /// Ray test against a box around each icon, returning the ray distance so hits compare with
    /// creature and GameObject picks.
    /// </summary>
    public (uint Id, float Distance)? FindHit(Camera3D camera, Vector2 screen)
    {
        if (!LayerEnabled)
        {
            return null;
        }

        var origin = camera.ProjectRayOrigin(screen);
        var direction = camera.ProjectRayNormal(screen);
        (uint Id, float Distance)? best = null;
        foreach (var (id, marker) in markers)
        {
            var center = marker.Node.GlobalPosition + Vector3.Up * IconHeight;
            var hit = ScenePicking.BoxHit(new Aabb(center - new Vector3(1.3f, 1.3f, 1.3f), new Vector3(2.6f, 2.6f, 2.6f)), origin, direction);
            if (hit.HasValue && hit.Value < camera.Far && (best == null || hit.Value < best.Value.Distance))
            {
                best = (id, hit.Value);
            }
        }

        return best;
    }

    public Vector3? PositionOf(uint id) => markers.TryGetValue(id, out var marker) ? marker.Node.GlobalPosition : null;

    public override void _ExitTree()
    {
        cancellation.Cancel();
        draft?.Dispose();
    }
}
