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
    public event Action? PreviewSqlRequested;
    public event Action? ExportAllRequested;
    public event Action? DiscardAllRequested;
    VBoxContainer list = null!;
    Label summary = null!, status = null!;
    Button previewSql = null!, exportAll = null!, discardAll = null!;

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
        previewSql = ButtonIn(footer, "Preview SQL…", () => PreviewSqlRequested?.Invoke());
        previewSql.TooltipText = "Show the apply and revert scripts an export would write.";
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
        previewSql.Disabled = changes.Count == 0;
        exportAll.Disabled = changes.Count == 0;
        discardAll.Disabled = changes.Count == 0;
        foreach (var change in changes)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(ClientStyle.Tag(change.Kind switch
            {
                PendingKind.Graveyard => "GRAVEYARD",
                PendingKind.Waypoint => "WAYPOINT",
                _ => change.Row?.Table switch
                {
                    "creature" or "creature_addon" => "CREATURE",
                    "gameobject" or "gameobject_addon" => "GAMEOBJECT",
                    _ => "ROW"
                }
            }));
            var text = ClientStyle.Text(change.Label, 13);
            text.TooltipText = change.Label;
            row.AddChild(text);
            ButtonIn(row, "Discard", () => DiscardRequested?.Invoke(change));
            list.AddChild(row);
        }
    }
}

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
