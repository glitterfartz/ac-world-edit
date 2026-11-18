using Godot;
using ACWorldEdit.Foundation.Scripting;

namespace ACWorldEdit;

/// <summary>
/// One graph node: a header with the semantic display name and one slot row per pin. Exec pins are
/// white, data pins take the type registry's colour, and object pins explain their lifetime.
/// </summary>
public partial class ScriptNodeView : GraphNode
{
    public ScriptNodeView(ScriptDocument document, ScriptHostProfile host, ScriptNode node, ScriptPortMap ports)
    {
        NodeId = node.Id;
        Name = "Node_" + node.Id;
        Ports = ports;
        Title = Display(host, node);
        PositionOffset = new Vector2((float)node.Position.X, (float)node.Position.Y);

        int slot = 0;
        foreach (var pin in ScriptPins.Build(document, host, node))
        {
            bool input = pin.Direction == ScriptPinDirection.In;
            bool output = !input;
            var info = ScriptTypeRegistry.Default.Info(pin.Type);
            var row = new HBoxContainer
            {
                Name = $"Pin_{(input ? "in" : "out")}_{pin.Id}",
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            var label = new Label
            {
                Text = pin.Id,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = input ? Control.SizeFlags.ExpandFill : Control.SizeFlags.ShrinkEnd
            };
            label.AddThemeFontSizeOverride("font_size", 12);
            label.AddThemeColorOverride("font_color", new Color(info.Color));
            if (info.IsObject && pin.Nullable)
            {
                label.TooltipText = "Valid during this handler; store a GUID to keep it.";
            }

            row.AddChild(label);
            AddChild(row);
            SetSlot(slot, input, info.PortType, new Color(info.Color), output, info.PortType, new Color(info.Color));
            Ports.Add(pin.Id, slot, input, output);

            slot++;
        }
    }

    public string NodeId { get; }

    public ScriptPortMap Ports { get; }

    static string Display(ScriptHostProfile host, ScriptNode node)
    {
        if (host.Event(node.Kind) is { } definition)
        {
            return definition.Display;
        }

        if (host.Capability(node.Kind) is { } capability)
        {
            return capability.Display;
        }

        return ScriptBuiltinCatalog.Find(node.Kind)?.Display ?? node.Kind;
    }
}
