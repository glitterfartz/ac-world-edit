using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The creature markers on screen: one labelled capsule per spawn, created on the scene node and
/// highlighted on selection. A marker's GUID is the stable identity the list, picking and the gizmo
/// use; the node is replaced only when the marker itself is rebuilt.
/// </summary>
internal sealed class CreatureMarkers(Node3D owner)
{
    internal readonly record struct Marker(CreatureSpawn Spawn, Node3D Node, MeshInstance3D Body, Label3D Label);

    /// <summary>Label height above the capsule fallback marker (2 yd tall).</summary>
    public const float FallbackLabelHeight = 3;

    readonly Dictionary<ulong, Marker> markers = [];
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

    public int Count => markers.Count;
    public IEnumerable<ulong> Keys => markers.Keys;
    public IEnumerable<Marker> Values => markers.Values;
    public bool ContainsKey(ulong guid) => markers.ContainsKey(guid);
    public bool TryGetValue(ulong guid, out Marker marker) => markers.TryGetValue(guid, out marker);
    public Marker this[ulong guid] => markers[guid];

    /// <summary>Create a labeled fallback at the effective pose; model readiness controls its body visibility.</summary>
    public void Add(CreatureSpawn spawn, Vector3 position, float heading)
    {
        var node = new Node3D
        {
            Name = "Creature_" + spawn.Guid,
            Position = position,
            Rotation = new(0, heading, 0)
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
            Position = new(0, FallbackLabelHeight, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 32,
            PixelSize = 0.018f
        };
        node.AddChild(label);
        owner.AddChild(node);
        markers.Add(spawn.Guid, new Marker(spawn, node, body, label));
    }

    /// <summary>Refresh the cached record of a marker already on screen, keeping its node and model.</summary>
    public void Update(ulong guid, CreatureSpawn spawn)
    {
        if (markers.TryGetValue(guid, out var existing))
        {
            markers[guid] = existing with { Spawn = spawn };
        }
    }

    /// <summary>Remove and free one marker (its native model batch is owned elsewhere).</summary>
    public void Remove(ulong guid) => markers.Free(guid, m => m.Node);

    /// <summary>Remove and free every marker.</summary>
    public void Clear() => markers.FreeAll(m => m.Node);

    /// <summary>Move the selected highlight from one marker to another without touching the rest.</summary>
    public void Highlight(ulong? previous, ulong? current)
    {
        if (previous is ulong old && markers.TryGetValue(old, out var marker))
        {
            marker.Body.MaterialOverride = normal;
        }

        if (current is ulong now && markers.TryGetValue(now, out var chosen))
        {
            chosen.Body.MaterialOverride = selected;
        }
    }
}
