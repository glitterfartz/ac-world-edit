using Godot;
using ACWorldEdit.Experiments;
namespace ACWorldEdit.Experiments;

[Tool]
public partial class FoundationTrialPlugin : EditorPlugin
{
    EditorDock? dock;
    public override void _EnterTree()
    {
        dock = new EditorDock { Title = "Phase 0 trial", DefaultSlot = EditorDock.DockSlot.RightUl };
        var panel = new FoundationShellTrial(); dock.AddChild(panel); AddDock(dock);
        if (OS.GetCmdlineUserArgs().Contains("--foundation-plugin-smoke"))
        { panel.RunSmoke(); GD.Print("Foundation EditorPlugin shell instantiated and fixture workflow passed."); }
    }
    public override void _ExitTree()
    {
        if (dock == null) return;
        RemoveDock(dock); dock.QueueFree(); dock = null;
    }
}
