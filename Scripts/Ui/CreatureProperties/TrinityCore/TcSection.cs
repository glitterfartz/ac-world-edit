using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Base of the TrinityCore creature sections: they draw the read model (<see cref="TcCreatureView"/>)
/// with editing enabled separately by each implemented page. They use the AzerothCore sections' ids, so
/// opening the window on "gossip" or "vendor" finds the page of the open database.
/// </summary>
public abstract partial class TcSection(CreatureContext ctx) : CreatureSection(ctx)
{
    public override bool Supports(WorldDialect dialect) => dialect == WorldDialect.TrinityCore;

    /// <summary>Fill the section for a loaded creature.</summary>
    protected abstract void Populate(TcCreatureView v);

    public override void Refresh()
    {
        Clear();
        if (Ctx.TcView is { } v)
        {
            Populate(v);
        }
        else
        {
            AddChild(Placeholder());
        }
    }

    protected void Heading(string text) => AddChild(ClientStyle.Text(text, 16, ClientStyle.Header));

    protected Label Line(string text, Color? color = null, int size = 13, Control? parent = null)
    {
        var label = ClientStyle.Text(text, size, color ?? ClientStyle.Body);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        (parent ?? this).AddChild(label);
        return label;
    }

    /// <summary>A two-column grid of caption/value pairs; values that are 0 or empty are still shown.</summary>
    protected void Facts(IEnumerable<(string Caption, string Value)> facts, Control? parent = null)
    {
        var grid = new GridContainer
        {
            Columns = 2
        };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 2);
        (parent ?? this).AddChild(grid);
        foreach (var (caption, value) in facts)
        {
            grid.AddChild(ClientStyle.Tag(caption));
            var label = ClientStyle.Text(value, 13, ClientStyle.Body);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            grid.AddChild(label);
        }
    }

    /// <summary>An indented block for one menu, trainer or item.</summary>
    protected VBoxContainer Block()
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        AddChild(margin);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        margin.AddChild(box);
        return box;
    }

    protected static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
