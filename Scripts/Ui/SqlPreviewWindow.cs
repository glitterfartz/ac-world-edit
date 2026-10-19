using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Read-only view of the scripts an export would write, switchable between apply and revert.
/// </summary>
public sealed partial class SqlPreviewWindow : AcceptDialog
{
    readonly TextEdit text = new()
    {
        Editable = false,
        SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
    };
    readonly OptionButton kind = new();
    IReadOnlyList<(string Source, SqlScripts Scripts)> exports = [];

    public SqlPreviewWindow()
    {
        Title = "SQL preview";
        OkButtonText = "Close";
        MinSize = new Vector2I(820, 560);
        var root = new VBoxContainer();
        AddChild(root);
        var bar = new HBoxContainer();
        root.AddChild(bar);
        kind.AddItem("Apply script");
        kind.AddItem("Revert script");
        kind.ItemSelected += _ => Render();
        bar.AddChild(kind);
        var copy = new Button { Text = "Copy" };
        copy.Pressed += () => DisplayServer.ClipboardSet(text.Text);
        bar.AddChild(copy);
        root.AddChild(text);
    }

    public void Open(IReadOnlyList<(string Source, SqlScripts Scripts)> scripts)
    {
        exports = scripts;
        Render();
        PopupCentered();
    }

    void Render()
    {
        bool revert = kind.Selected == 1;
        text.Text = exports.Count == 0
            ? "-- Nothing to export."
            : string.Join("\n\n", exports.Select(e => (exports.Count > 1 ? $"-- Source {e.Source}\n" : "") + (revert ? e.Scripts.Revert : e.Scripts.Apply)));
    }
}
