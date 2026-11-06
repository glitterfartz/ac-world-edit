using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Standalone window around a <see cref="BehaviorPanel"/>, used for AreaTrigger rules (source_type 2)
/// which have no creature to open the creature properties window on.
/// </summary>
public partial class BehaviorEditorWindow : ContentEditorWindow
{
    protected override Vector2 TargetSize => new(980, 720);
    public BehaviorPanel Panel { get; } = new() { SpeechInScopes = false };
    Label title = null!;
    Button undo = null!, redo = null!;

    public BehaviorEditorWindow() : base("BehaviorEditor")
    {
        Size = TargetSize;
    }

    public override void _Ready()
    {
        Panel.Connection = () => Connection();
        Panel.Store = () => Store();
        Panel.Drafts = Drafts;
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);
        var header = AddHeader(root, "Behavior");
        title = header.Title;
        undo = ButtonIn(header.Row, "Undo", () => Panel.History(false));
        undo.TooltipText = "Undo the last behavior change.";
        redo = ButtonIn(header.Row, "Redo", () => Panel.History(true));
        redo.TooltipText = "Redo the last undone behavior change.";
        var close = ButtonIn(header.Row, "✕", () => Visible = false);
        close.TooltipText = "Close the behavior window.";
        root.AddChild(Panel);
        Panel.Rendered += Sync;
        Sync();
    }

    void Sync()
    {
        title.Text = Panel.Title;
        Panel.SyncHistoryButtons(undo, redo);
    }

    /// <summary>Show the SmartAI rules of one AreaTrigger or spawned trigger template.</summary>
    public void OpenAreaTrigger(AreaTriggerOwner trigger)
    {
        Visible = true;
        PlaceOnFirstOpen(40, 90);
        Panel.OpenAreaTrigger(trigger);
    }
}
