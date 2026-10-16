using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The one place to review, export or discard unsaved draft changes. It only presents the list the
/// viewer gives it and raises requests; the viewer owns the drafts, so this window never touches
/// them or the database. Reuses the editor-window frame for the client look and header dragging.
/// </summary>
public partial class PendingChangesWindow : ContentEditorWindow
{
    public event Action<PendingChange>? DiscardRequested;
    public event Action? ExportAllRequested;
    public event Action? DiscardAllRequested;
    VBoxContainer list = null!;
    Label summary = null!, status = null!;
    Button exportAll = null!, discardAll = null!;

    protected override Vector2 TargetSize => new(760, 520);

    public PendingChangesWindow() : base("PendingChanges")
    {
        Size = TargetSize;
    }

    public override void _Ready()
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);
        var header = new HBoxContainer();
        header.GuiInput += DragHeader;
        root.AddChild(header);
        var heading = ClientStyle.Text("Pending Changes", 19, ClientStyle.Header);
        heading.MouseFilter = MouseFilterEnum.Ignore;
        heading.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(heading);
        ButtonIn(header, "✕", () => Visible = false);
        summary = ClientStyle.Text("", 12, ClientStyle.Faded);
        root.AddChild(summary);
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        root.AddChild(scroll);
        list = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(list);
        var footer = new HBoxContainer();
        root.AddChild(footer);
        exportAll = ButtonIn(footer, "Export All…", () => ExportAllRequested?.Invoke());
        exportAll.TooltipText = "Save an apply script and a revert script for every pending change.";
        discardAll = ButtonIn(footer, "Discard All…", () => DiscardAllRequested?.Invoke());
        status = ClientStyle.Text("", 12, ClientStyle.Faded);
        footer.AddChild(status);
    }

    /// <summary>
    /// Show the window, pinned near the top on first open.
    /// </summary>
    public void Open()
    {
        Visible = true;
        PlaceOnFirstOpen(0, 70);
        MoveToFront();
    }

    public void SetStatus(string text) => status.Text = text;

    /// <summary>
    /// Rebuild the rows. hiddenExported counts changes already exported that remain applied in the
    /// viewer but are no longer unsaved.
    /// </summary>
    public void Refresh(IReadOnlyList<PendingChange> changes, int hiddenExported)
    {
        foreach (var child in list.GetChildren())
        {
            list.RemoveChild(child);
            child.QueueFree();
        }

        string hidden = hiddenExported > 0 ? $" {hiddenExported} exported change(s) stay applied in the viewer until the database matches." : "";
        summary.Text = changes.Count == 0
            ? "No unsaved changes." + hidden
            : $"{changes.Count} unsaved change(s). Drafts are kept between sessions. Discard reverts only that one change." + hidden;
        exportAll.Disabled = changes.Count == 0;
        discardAll.Disabled = changes.Count == 0;
        foreach (var change in changes)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(ClientStyle.Tag(change.Kind switch
            {
                PendingKind.Creature => "CREATURE",
                PendingKind.Graveyard => "GRAVEYARD",
                _ => "ROW"
            }));
            var text = ClientStyle.Text(change.Label, 13);
            text.TooltipText = change.Label;
            row.AddChild(text);
            ButtonIn(row, "Discard", () => DiscardRequested?.Invoke(change));
            list.AddChild(row);
        }
    }
}
