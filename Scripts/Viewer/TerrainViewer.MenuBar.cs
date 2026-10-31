using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The command strip at the top of the screen: the File/Edit/View/Help menu bar above the toolbar.
/// </summary>
public partial class TerrainViewer
{
    EditorMenu menu = null!;
    EditorToolbar toolbar = null!;
    bool showCellBorders;
    int uiScalePercent = 100;
    static readonly int[] UiScalePercents = [75, 90, 100, 110, 125, 150, 175, 200];

    /// <summary>
    /// Full-width flat strip holding the menu bar above the toolbar.
    /// </summary>
    Control BuildStrip(Theme theme)
    {
        var strip = new PanelContainer
        {
            Name = "CommandStrip",
            Theme = theme
        };
        strip.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.06f, 0.085f, 0.97f),
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 4,
            ContentMarginBottom = 6,
            BorderWidthBottom = 1,
            BorderColor = new Color(1, 1, 1, 0.08f)
        });
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 4);
        strip.AddChild(rows);
        menu = new EditorMenu();
        rows.AddChild(menu);
        toolbar = new EditorToolbar();
        rows.AddChild(toolbar);
        return strip;
    }

    /// <summary>
    /// File/Edit/View/Help commands. Placement commands act on the selected creature's local draft;
    /// GameObjects stay read-only. Layer and SQL visibility controls remain only in the Scene
    /// explorer so there is one source of truth.
    /// </summary>
    void BuildMenus()
    {
        const Key ctrl = (Key)KeyModifierMask.MaskCtrl;
        const Key shift = (Key)KeyModifierMask.MaskShift;
        var file = menu.Menu("File");
        menu.Command(file, "Load client…", ChooseClient, () => !busy);
        menu.Command(file, "Update listfile…", () => _ = UpdateListfile(), () => !busy && listfileDownload == null);
        menu.Command(file, "Database connection…", () =>
        {
            _ = PromptConnection();
        });
        file.AddSeparator();
        menu.Command(file, "Pending changes…", OpenPendingChanges);
        menu.Command(file, "Clear cache…", ClearCache, () => !busy);
        menu.Command(file, "Reset configs and delete cache…", ResetEverything, () => OperatingSystem.IsWindows());
        file.AddSeparator();
        menu.Command(file, "Quit", () => GetTree().Quit(), accelerator: ctrl | Key.Q);

        var edit = menu.Menu("Edit");
        menu.Command(edit, "Undo", () => ActiveEditor.Undo(), () => ActiveEditor.CanUndo && !ActiveEditor.IsDragging, ctrl | Key.Z);
        menu.Command(edit, "Redo", () => ActiveEditor.Redo(), () => ActiveEditor.CanRedo && !ActiveEditor.IsDragging, ctrl | Key.Y);
        edit.AddSeparator();
        menu.Command(edit, "Duplicate", () => ActiveEditor.Duplicate(), () => ActiveEditor.CanDuplicate && !ActiveEditor.IsDragging, ctrl | Key.D);
        menu.Command(edit, "Delete", () => ActiveEditor.Delete(), () => ActiveEditor.CanDelete && !ActiveEditor.IsDragging, Key.Delete);
        edit.AddSeparator();
        menu.Command(edit, "Drop to ground", () => ActiveEditor.DropToGround(), () => ActiveEditor.CanEdit, Key.End);
        menu.Command(edit, "Reset to source", () => ActiveEditor.ResetToSource(), () => ActiveEditor.CanEdit);
        menu.Command(edit, "Copy .go command", () => ActiveEditor.CopyGoCommand(), () => ActiveEditor.HasSelection, ctrl | shift | Key.C);

        var view = menu.Menu("View");
        menu.Command(view, "Fly to selection", FlyToSelection, () => HasSelection, Key.F);
        menu.Command(view, "Quest editor", () => questEditor.Open(0), () => databaseEnabled);
        menu.Command(view, "Creature behavior (selected)", OpenBehaviorEditor, () => BehaviorEditors);
        menu.Command(view, "Scene explorer", () =>
        {
            sidebar.Visible = true;
            sidebar.ShowSceneTab();
        });
        view.AddSeparator();
        menu.Check(view, "Terrain cell borders", () =>
        {
            showCellBorders = !showCellBorders;
            renderer.SetGrid(showCellBorders);
        }, () => showCellBorders);
        menu.Check(view, "Navigation panel", () => leftPanel.Visible = !leftPanel.Visible, () => leftPanel.Visible);
        menu.Check(view, "Scene explorer panel", () => sidebar.Visible = !sidebar.Visible, () => sidebar.Visible);
        menu.Check(view, "Content browser", ToggleContentBrowser, () => contentBrowser.Visible, ctrl | Key.B);
        var scale = menu.Submenu(view, "UI scale");
        foreach (int percent in UiScalePercents)
        {
            menu.Check(scale, $"{percent}%", () => SetUiScale(percent, true), () => uiScalePercent == percent);
        }

        var help = menu.Menu("Help");
        menu.Command(help, "Controls…", () => ShowInfo("Controls", ControlsText));
        menu.Command(help, "About", () => ShowInfo("About AC World Edit", "AC World Edit · world viewer and local placement drafts for AzerothCore. Clients: Wrath 3.3.5a (with the world database) and Retail (viewing only, no database yet).\n\nClient archives and the world database are read-only. Moves save to a local draft file; nothing is written to the server."));
    }

    /// <summary>
    /// Scale the whole interface (menus, panels and dialogs) without touching the 3D view.
    /// </summary>
    void SetUiScale(int percent, bool save)
    {
        uiScalePercent = percent;
        var window = GetWindow();
        window.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        window.ContentScaleFactor = percent / 100f;
        if (save)
        {
            UserSettings.SaveUiScale(percent);
            statusLog.Post($"UI scale {percent}%.");
        }
    }

    const string ControlsText = """
        Camera
          Right mouse (hold)    look / fly
          W A S D               move;  Q / E  down / up
          Shift                 faster;  Wheel  flight speed
          Esc                   release mouse

        Editing (selected creature)
          Q / W / E             Select / Move / Rotate tool
          Drag colored axis     move along that axis
          Drag yellow ring      rotate heading (hold Ctrl for 15° steps)
          End                   drop to ground
          Ctrl+Z / Ctrl+Y       undo / redo (also Ctrl+Shift+Z)
          F                     fly to selection
          Right-click (no drag) a creature to see its gossip, quests, vendor and trainer
          Esc during a drag     cancel
        """;
}
