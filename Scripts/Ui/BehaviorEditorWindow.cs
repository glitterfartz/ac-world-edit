using Godot;

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
        var header = new HBoxContainer();
        header.GuiInput += DragHeader;
        root.AddChild(header);
        title = ClientStyle.Text("Behavior", 19, ClientStyle.Header);
        title.MouseFilter = MouseFilterEnum.Ignore;
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        undo = ButtonIn(header, "Undo", () => Panel.History(false));
        redo = ButtonIn(header, "Redo", () => Panel.History(true));
        ButtonIn(header, "✕", () => Visible = false);
        root.AddChild(Panel);
        Panel.Rendered += Sync;
        Sync();
    }

    void Sync()
    {
        title.Text = Panel.Title;
        Panel.SyncHistoryButtons(undo, redo);
    }

    /// <summary>Show the SmartAI rules of one AreaTrigger.</summary>
    public void OpenAreaTrigger(uint trigger)
    {
        Visible = true;
        PlaceOnFirstOpen(40, 90);
        Panel.OpenAreaTrigger(trigger);
    }
}
