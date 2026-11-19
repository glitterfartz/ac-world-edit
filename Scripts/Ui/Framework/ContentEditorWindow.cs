using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Shared frame of the draft-backed editor windows (behavior, quest, cinematics, change history,
/// bookmarks and the properties windows): a native <see cref="Window"/> with a client-drawn title
/// bar, a scrolling body, an action row, a status row and a resize grip. It also carries the
/// connection, asset and draft hooks the viewer wires up, and the client spell/item catalog loaded
/// once per asset store. Everything else (pages, loading, commits) stays in each window.
/// </summary>
public partial class ContentEditorWindow : Window
{
    public Func<WorldConnection> Connection { get; set; } = () => WorldConnection.Empty;
    public Func<IClientFiles?> Store { get; set; } = () => null;
    public ContentDrafts Drafts { get; set; } = null!;

    /// <summary>Status and error text; the viewer routes this to the status bar.</summary>
    public Action<string>? Report { get; set; }

    /// <summary>The window's status line text; the row is fed by <see cref="SetStatus"/>.</summary>
    protected string status = "";

    // One catalog per loaded asset store, shared by every editor window (reloading archives gives
    // the store a new identity). A failed load degrades to null: names and icons are then limited.
    static IClientFiles? catalogStore;
    static Task<InteractionCatalog?>? catalogLoad;
    protected InteractionCatalog? catalog;

    bool placed;

    protected ContentEditorWindow(string name, string title)
    {
        Name = name;
        Title = title;
        Visible = false;
        Theme = ClientStyle.Theme();
        // A tool window over the viewer that keeps its own dialogs inside itself.
        ForceNative = !DiagnosticRun;
        Transient = true;
        Borderless = true;
        GuiEmbedSubwindows = true;
    }

    /// <summary>
    /// Headless runs and diagnostic runs (smokes, client shots, UI/state dumps) keep the window
    /// embedded, so the existing click and capture paths still reach it.
    /// </summary>
    public static bool DiagnosticRun { get; } =
        DisplayServer.GetName() == "headless"
        || OS.GetCmdlineUserArgs().Any(arg =>
            arg.EndsWith("-smoke", StringComparison.Ordinal)
            || arg.StartsWith("--client-shot", StringComparison.Ordinal)
            || arg.StartsWith("--dump-target", StringComparison.Ordinal)
            || arg is "--ui-dump" or "--state-dump");

    /// <summary>The size the window opens at; a Window never grows to its content.</summary>
    protected virtual Vector2 TargetSize => new(1000, 700);

    /// <summary>The floor the user can resize the window to; the scroll body handles the overflow.</summary>
    protected virtual Vector2I MinimumSize => new(640, 420);

    public Label TitleLabel { get; private set; } = null!;
    public Label SubtitleLabel { get; private set; } = null!;
    public Button CloseButton { get; private set; } = null!;

    /// <summary>The scrolling editor body; subclass content goes here.</summary>
    public VBoxContainer Body { get; private set; } = null!;

    /// <summary>The toolbar row under the title; a flow container so the actions wrap.</summary>
    public HFlowContainer Actions { get; private set; } = null!;

    /// <summary>The status row under the toolbar.</summary>
    public Label StatusLabel { get; private set; } = null!;

    public override void _Ready()
    {
        BuildFrame();
        MinSize = MinimumSize;
        Size = (Vector2I)TargetSize;
        BuildEditor();
    }

    /// <summary>Build the window's own content into <see cref="Body"/>.</summary>
    protected virtual void BuildEditor()
    {
    }

    void BuildFrame()
    {
        var panel = new PanelContainer();
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        panel.AddThemeStyleboxOverride("panel", ClientStyle.Panel());
        AddChild(panel);

        var root = new VBoxContainer();
        panel.AddChild(root);

        var header = new HBoxContainer();
        header.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                StartDrag();
                header.AcceptEvent();
            }
        };
        root.AddChild(header);
        var titles = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        header.AddChild(titles);
        TitleLabel = ClientStyle.Text(Title, 19, ClientStyle.Header);
        TitleLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        titles.AddChild(TitleLabel);
        SubtitleLabel = ClientStyle.Text("", 13, ClientStyle.Faded);
        SubtitleLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        SubtitleLabel.Visible = false;
        titles.AddChild(SubtitleLabel);
        CloseButton = new Button { Text = "✕", TooltipText = "Close (Escape)" };
        CloseButton.Pressed += Close;
        header.AddChild(CloseButton);
        root.AddChild(new HSeparator());

        // The body scrolls instead of raising the window's minimum: the editor is taller than a
        // comfortable tool window, and a Window never grows to its content unless WrapControls is
        // on. A ScrollContainer only stretches its child on an axis the child expands on, so the
        // body needs ExpandFill vertically as well or it stays at its minimum when the window grows.
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        root.AddChild(scroll);
        Body = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        scroll.AddChild(Body);

        Actions = new HFlowContainer();
        Body.AddChild(Actions);
        StatusLabel = ClientStyle.Text("", 13, ClientStyle.Faded);
        StatusLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        StatusLabel.Visible = false;
        Body.AddChild(StatusLabel);

        var grip = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var handle = new Label
        {
            Text = "◢",
            TooltipText = "Drag to resize",
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        handle.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                StartResize(DisplayServer.WindowResizeEdge.BottomRight);
                handle.AcceptEvent();
            }
        };
        grip.AddChild(handle);
        root.AddChild(grip);

        // Key events are delivered to the window's viewport, not to the Window node itself.
        AddChild(new ToolWindowShortcuts { UnhandledKey = HandleShortcut });

        CloseRequested += Close;
    }

    /// <summary>Center the window over the viewer the first time it opens, then show and focus it.</summary>
    public void Open()
    {
        if (!placed)
        {
            placed = true;
            var main = GetTree().Root;
            Position = main.Position + (main.Size - Size) / 2;
        }

        Visible = true;
        GrabFocus();
    }

    public void Close() => Visible = false;

    /// <summary>Kept for the viewer and the smokes, which close these windows by name.</summary>
    public void CloseWindow() => Close();

    /// <summary>Set the client title row; an empty subtitle collapses the second line.</summary>
    public void SetHeading(string title, string? subtitle = null)
    {
        TitleLabel.Text = title;
        SubtitleLabel.Text = subtitle ?? "";
        SubtitleLabel.Visible = !string.IsNullOrEmpty(subtitle);
        Title = title;
    }

    /// <summary>Show a status message in the window row and pass it to the viewer's status bar.</summary>
    public void SetStatus(string message)
    {
        status = message;
        StatusLabel.Text = message;
        StatusLabel.Visible = message.Length > 0;
        Report?.Invoke(message);
    }

    bool HandleShortcut(InputEvent @event)
    {
        if (!Visible || @event is not InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            return false;
        }

        Close();
        return true;
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
