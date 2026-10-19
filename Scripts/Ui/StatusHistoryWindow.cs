using Godot;

namespace ACWorldEdit;

/// <summary>
/// The status bar's message history, newest first. Opened by clicking the message on the bar.
/// </summary>
public sealed partial class StatusHistoryWindow : AcceptDialog
{
    readonly StatusLog log;
    readonly ItemList list = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill, AutoHeight = true };

    public StatusHistoryWindow(StatusLog log)
    {
        this.log = log;
        Title = "Message history";
        OkButtonText = "Close";
        MinSize = new Vector2I(560, 360);
        AddButton("Clear", false, "clear");
        CustomAction += _ =>
        {
            log.Clear();
            Rebuild();
        };
        AddChild(list);
    }

    public void Open()
    {
        Rebuild();
        PopupCentered();
    }

    void Rebuild()
    {
        list.Clear();
        foreach (var (time, text) in log.Entries.Reverse())
        {
            list.AddItem($"{time:HH:mm:ss}   {text}", null, false);
        }
    }
}
