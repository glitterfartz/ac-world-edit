using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// One page of the GameObject properties window. A page reads the shared
/// <see cref="GameObjectContext"/>; the window's frame and section picker are the Phase 10
/// shell. Every GameObject page works with both dialects; the page builders include only the
/// columns the open database actually has.
/// </summary>
public abstract partial class GameObjectSection(GameObjectContext ctx) : EntitySection
{
    protected GameObjectContext Ctx { get; } = ctx;

    public override bool Supports(WorldDialect dialect) => true;

    /// <summary>The line shown while loading, or when no GameObject is selected.</summary>
    protected Label Placeholder() => ClientStyle.Text(
        Ctx.Entry != null ? "Loading the GameObject template…" : "Select a GameObject in the world, or open one from the content browser.",
        15, ClientStyle.Faded);

    protected static void Host(Control panel, Control parent)
    {
        if (panel.GetParent() != parent)
        {
            panel.GetParent()?.RemoveChild(panel);
            parent.AddChild(panel);
        }
    }

    /// <summary>Drop the page's children; each Refresh rebuilds them from the context.</summary>
    protected void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }
}
