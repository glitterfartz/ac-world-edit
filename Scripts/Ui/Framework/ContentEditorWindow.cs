using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Shared frame of the draft-backed editor windows (interaction, quest, behavior): a client-styled
/// panel that can be dragged by its header and is pinned to its intended size, the connection, asset
/// and draft hooks the viewer wires up, and the client spell/item catalog loaded once per asset store.
/// Everything else (pages, loading, commits) stays in each window.
/// </summary>
public partial class ContentEditorWindow : PanelContainer
{
    public Func<WorldConnection> Connection { get; set; } = () => WorldConnection.Empty;
    public Func<IClientFiles?> Store { get; set; } = () => null;
    public ContentDrafts Drafts { get; set; } = null!;

    /// <summary>The window's status line text; each window shows it in its own label.</summary>
    protected string status = "";

    // One catalog per loaded asset store, shared by every editor window (reloading archives gives
    // the store a new identity). A failed load degrades to null: names and icons are then limited.
    static IClientFiles? catalogStore;
    static Task<InteractionCatalog?>? catalogLoad;
    protected InteractionCatalog? catalog;
    Vector2? dragOffset;

    protected ContentEditorWindow(string name)
    {
        Name = name;
        Visible = false;
        Theme = ClientStyle.Theme();
        AddThemeStyleboxOverride("panel", ClientStyle.Panel());
    }

    /// <summary>
    /// Intended size. Wrapped labels report a stale larger minimum until laid out, which would
    /// otherwise grow the window permanently, so _Process re-applies this whenever it differs.
    /// </summary>
    protected virtual Vector2 TargetSize => Size;

    public override void _Process(double delta)
    {
        if (Visible && Size != TargetSize)
        {
            Size = TargetSize;
        }
    }

    /// <summary>
    /// Drag the window by its header; it stays inside the viewport.
    /// </summary>
    protected void DragHeader(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            dragOffset = button.Pressed ? button.GlobalPosition - GlobalPosition : null;
            AcceptEvent();
        }
        else if (input is InputEventMouseMotion motion && dragOffset.HasValue)
        {
            GlobalPosition = (motion.GlobalPosition - dragOffset.Value).Clamp(Vector2.Zero, (GetViewportRect().Size - Size).Max(Vector2.Zero));
            AcceptEvent();
        }
    }

    protected void EndDrag() => dragOffset = null;

    /// <summary>The draggable title area and the labels the window updates later.</summary>
    public sealed record WindowHeader(HBoxContainer Row, Label Title, Label? Subtitle);

    /// <summary>
    /// The draggable title row every editor window built by hand. A subtitle adds the stacked titles
    /// box the properties window uses; without one the title sits directly in the row. Callers append
    /// their own buttons to <see cref="WindowHeader.Row"/>.
    /// </summary>
    protected WindowHeader AddHeader(VBoxContainer root, string title, string? subtitle = null)
    {
        var header = new HBoxContainer();
        header.GuiInput += DragHeader;
        root.AddChild(header);
        Label heading;
        Label? under = null;
        if (subtitle == null)
        {
            heading = ClientStyle.Text(title, 19, ClientStyle.Header);
            heading.MouseFilter = MouseFilterEnum.Ignore;
            heading.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            header.AddChild(heading);
        }
        else
        {
            var titles = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore
            };
            header.AddChild(titles);
            heading = ClientStyle.Text(title, 19, ClientStyle.Header);
            heading.MouseFilter = MouseFilterEnum.Ignore;
            titles.AddChild(heading);
            under = ClientStyle.Text(subtitle, 13, ClientStyle.Faded);
            under.MouseFilter = MouseFilterEnum.Ignore;
            titles.AddChild(under);
        }

        return new(header, heading, under);
    }

    /// <summary>
    /// First time shown: center horizontally (shifted by xShift) at the given height. Later opens
    /// keep wherever the user dragged it.
    /// </summary>
    protected void PlaceOnFirstOpen(float xShift, float y)
    {
        if (Position == Vector2.Zero)
        {
            Position = new(Math.Max(0, GetViewportRect().Size.X / 2 - TargetSize.X / 2 + xShift), y);
        }
    }

    protected async Task<InteractionCatalog?> EnsureCatalog()
    {
        catalog = await LoadCatalog(Store());
        return catalog;
    }

    /// <summary>
    /// The client catalog for a store, loaded once per store; null without a store or when it fails.
    /// </summary>
    public static async Task<InteractionCatalog?> LoadCatalog(IClientFiles? store)
    {
        if (store == null)
        {
            return null;
        }

        if (!ReferenceEquals(store, catalogStore))
        {
            catalogStore = store;
            catalogLoad = Task.Run(() =>
            {
                try
                {
                    return InteractionCatalog.Load(store);
                }
                catch (Exception)
                {
                    return null;
                }
            });
        }

        return await catalogLoad!;
    }

    public static Button ButtonOf(string text, Action action)
    {
        var button = new Button
        {
            Text = text
        };
        button.Pressed += action;
        return button;
    }

    public static Button ButtonIn(Container parent, string text, Action action)
    {
        var button = ButtonOf(text, action);
        parent.AddChild(button);
        return button;
    }

    /// <summary>
    /// Record one validated command through <see cref="Drafts"/>, put its message in the status
    /// line, and run the matching callback.
    /// </summary>
    protected bool CommitEdits(string sourceKey, RowSet snapshot, RowSet effective, string label, IReadOnlyList<RowEdit> edits,
        Func<RowSet, IEnumerable<InteractionIssue>> check, Action onOk, Action onFail)
    {
        var (ok, message) = Drafts.Commit(this, sourceKey, snapshot, effective, label, edits, check);
        status = message;
        (ok ? onOk : onFail)();
        return ok;
    }

    /// <summary>
    /// Undo or redo through <see cref="Drafts"/>, then redraw and let the window reload its rows.
    /// </summary>
    protected void ApplyHistory(bool forward, Action rebuild, Action reload)
    {
        status = Drafts.History(this, forward);
        rebuild();
        reload();
    }

    /// <summary>
    /// Run <paramref name="reload"/> when another editor changes the draft while this window is
    /// showing something that can be reloaded.
    /// </summary>
    protected void ReloadOnForeignChange(Func<bool> hasTarget, Action reload) => Drafts.Changed += sender =>
    {
        if (sender != this && hasTarget())
        {
            reload();
        }
    };

    /// <summary>
    /// Enable and label the header Undo/Redo buttons from the shared draft history.
    /// </summary>
    protected void SyncHistoryButtons(Button undo, Button redo)
    {
        var draft = Drafts.Draft;
        undo.Disabled = draft?.CanUndo != true;
        redo.Disabled = draft?.CanRedo != true;
        undo.TooltipText = draft?.LastLabel is { } last ? $"Undo \"{last}\"" : "";
        redo.TooltipText = draft?.NextLabel is { } next ? $"Redo \"{next}\"" : "";
    }
}
