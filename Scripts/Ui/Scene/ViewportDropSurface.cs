using Godot;

namespace ACWorldEdit;

/// <summary>
/// A transparent full-screen control behind every panel that accepts a template dragged from the content
/// browser. Godot offers a drop to the control under the cursor, so dropping on a panel never reaches this
/// surface and only the open world receives it. It passes ordinary mouse events through to the viewer.
/// </summary>
public sealed partial class ViewportDropSurface : Control
{
    /// <summary>A template is being dragged over the world at this position.</summary>
    public Action<PlacementTool.Template, Vector2>? Hover { get; set; }

    /// <summary>A template was dropped on the world at this position.</summary>
    public Action<PlacementTool.Template, Vector2>? Dropped { get; set; }

    /// <summary>A drag ended, dropped or not.</summary>
    public Action? Ended { get; set; }

    public ViewportDropSurface()
    {
        Name = "DropSurface";
        MouseFilter = MouseFilterEnum.Pass;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (!TemplateDrag.TryUnpack(data, out var template))
        {
            return false;
        }

        Hover?.Invoke(template, GetGlobalTransformWithCanvas() * atPosition);
        return true;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (TemplateDrag.TryUnpack(data, out var template))
        {
            Dropped?.Invoke(template, GetGlobalTransformWithCanvas() * atPosition);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd)
        {
            Ended?.Invoke();
        }
    }
}
