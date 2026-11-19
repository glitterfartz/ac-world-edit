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
    /// <summary>Raised when a rule names a conversation or scene and its Open button is pressed.</summary>
    public Action<SmartParamKind, uint> CinematicsRequested { get; set; } = (_, _) => { };
    Button undo = null!, redo = null!;

    public BehaviorEditorWindow() : base("BehaviorEditor", "Behavior")
    {
    }

    protected override void BuildEditor()
    {
        Panel.Connection = () => Connection();
        Panel.Store = () => Store();
        Panel.Drafts = Drafts;
        Panel.CinematicsRequested = (kind, id) => CinematicsRequested(kind, id);
        undo = ButtonIn(Actions, "Undo", () => Panel.History(false));
        undo.TooltipText = "Undo the last behavior change.";
        redo = ButtonIn(Actions, "Redo", () => Panel.History(true));
        redo.TooltipText = "Redo the last undone behavior change.";
        Body.AddChild(Panel);
        Panel.Rendered += Sync;
        Sync();
    }

    void Sync()
    {
        SetHeading(Panel.Title);
        Panel.SyncHistoryButtons(undo, redo);
    }

    /// <summary>Show the SmartAI rules of one AreaTrigger or spawned trigger template.</summary>
    public void OpenAreaTrigger(AreaTriggerOwner trigger)
    {
        Open();
        Panel.OpenAreaTrigger(trigger);
    }
}
