using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Frameless editor body that can live in a standalone <see cref="ContentEditorWindow"/> or inside a
/// section of the creature properties window: the connection, asset and draft hooks, the shared
/// client catalog, validated commits and reload-on-foreign-change. Hosts wire the three hooks and
/// own the header, size and Undo/Redo chrome.
/// </summary>
public partial class ContentPanel : VBoxContainer
{
    public Func<DatabaseSettings> Connection { get; set; } = () => DatabaseSettings.Empty;
    public Func<IClientFiles?> Store { get; set; } = () => null;
    public ContentDrafts Drafts { get; set; } = null!;

    /// <summary>The panel's status line text; each panel shows it in its own label.</summary>
    protected string status = "";
    protected InteractionCatalog? catalog;
    bool reloadWhenShown;

    protected async Task<InteractionCatalog?> EnsureCatalog()
    {
        catalog = await ContentEditorWindow.LoadCatalog(Store());
        return catalog;
    }

    protected static Button ButtonOf(string text, Action action) => ContentEditorWindow.ButtonOf(text, action);

    protected static Button ButtonIn(Container parent, string text, Action action) => ContentEditorWindow.ButtonIn(parent, text, action);

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
    /// Undo or redo through <see cref="Drafts"/>, then redraw and let the panel reload its rows.
    /// </summary>
    protected void ApplyHistory(bool forward, Action rebuild, Action reload)
    {
        status = Drafts.History(this, forward);
        rebuild();
        reload();
    }

    /// <summary>
    /// Run <paramref name="reload"/> when another editor changes the draft while this panel has a
    /// target. A hidden panel only remembers it and reloads once it is shown again.
    /// </summary>
    protected void ReloadOnForeignChange(Func<bool> hasTarget, Action reload)
    {
        Drafts.Changed += sender =>
        {
            if (sender == this || !hasTarget())
            {
                return;
            }

            if (IsVisibleInTree())
            {
                reload();
            }
            else
            {
                reloadWhenShown = true;
            }
        };
        VisibilityChanged += () =>
        {
            if (reloadWhenShown && IsVisibleInTree() && hasTarget())
            {
                reloadWhenShown = false;
                reload();
            }
        };
    }

    /// <summary>
    /// Enable and label Undo/Redo buttons from the shared draft history.
    /// </summary>
    public void SyncHistoryButtons(Button undo, Button redo)
    {
        var draft = Drafts.Draft;
        undo.Disabled = draft?.CanUndo != true;
        redo.Disabled = draft?.CanRedo != true;
        undo.TooltipText = draft?.LastLabel is { } last ? $"Undo \"{last}\"" : "";
        redo.TooltipText = draft?.NextLabel is { } next ? $"Redo \"{next}\"" : "";
    }
}
