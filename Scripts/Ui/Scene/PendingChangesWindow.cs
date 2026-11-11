using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Change history: every staged and applied world change with its timestamp, status and optional
/// note. Staged rows come from local drafts; applying runs their SQL against the world database and
/// marks them applied in acworld. Applied rows revert by running their stored revert SQL. Any
/// checked rows export into one SQL file for sharing. The window only presents the list and raises
/// requests; the viewer owns the drafts and the database, so this window never touches them.
/// </summary>
public partial class PendingChangesWindow : ContentEditorWindow
{
    public event Action<IReadOnlyList<string>>? ApplyRequested;
    public event Action<IReadOnlyList<string>>? RevertRequested;
    public event Action<IReadOnlyList<string>>? DiscardStagedRequested;
    public event Action? DiscardAllStagedRequested;
    public event Action<IReadOnlyList<string>, bool>? ExportRequested;
    public event Action<IReadOnlyList<string>, bool>? PreviewRequested;
    public event Action<string, string>? NotesChanged;
    public event Action<bool>? ShowAllChanged;
    public event Action? HistoryRefreshRequested;

    VBoxContainer list = null!;
    ScrollContainer scroll = null!;
    Label summary = null!, status = null!;
    Button applySelected = null!, revertSelected = null!, previewSelected = null!, exportSelected = null!, discardAll = null!, refreshButton = null!;
    CheckButton showAll = null!;
    OptionButton exportKind = null!;

    /// <summary>
    /// Checked fingerprints, kept outside the rows so the periodic history refresh never drops the
    /// user's selection. Row boxes update this set when toggled and read it when rebuilt.
    /// </summary>
    readonly HashSet<string> checkedFingerprints = new(StringComparer.Ordinal);
    List<ChangeRecord>? shownRecords;
    string? shownError;
    string? currentSourceKey;

    protected override Vector2 TargetSize => new(860, 560);

    public PendingChangesWindow() : base("PendingChanges")
    {
        Size = TargetSize;
    }

    public override void _Ready()
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);
        var header = AddHeader(root, "Change History");
        ButtonIn(header.Row, "✕", () => Visible = false);
        summary = ClientStyle.Text("", 12, ClientStyle.Faded);
        root.AddChild(summary);
        scroll = new ScrollContainer
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

        var options = new HBoxContainer();
        options.AddThemeConstantOverride("separation", 8);
        root.AddChild(options);
        showAll = new CheckButton { Text = "Show all worlds" };
        showAll.TooltipText = "Show changes from every world database, not just the open one.";
        showAll.Toggled += pressed => ShowAllChanged?.Invoke(pressed);
        options.AddChild(showAll);
        exportKind = new OptionButton();
        exportKind.AddItem("Export: apply script");
        exportKind.AddItem("Export: revert script");
        exportKind.TooltipText = "Whether Export selected writes the apply or the revert scripts.";
        options.AddChild(exportKind);
        refreshButton = new Button { Text = "Refresh" };
        refreshButton.TooltipText = "Re-read drafts and the change history.";
        refreshButton.Pressed += () => HistoryRefreshRequested?.Invoke();
        options.AddChild(refreshButton);

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 6);
        root.AddChild(footer);
        applySelected = ButtonIn(footer, "Apply selected", () => ApplyRequested?.Invoke(Checked()));
        applySelected.TooltipText = "Run the checked staged changes against the world database.";
        revertSelected = ButtonIn(footer, "Revert selected", () => RevertRequested?.Invoke(Checked()));
        revertSelected.TooltipText = "Undo the checked applied changes by running their revert scripts.";
        previewSelected = ButtonIn(footer, "Preview…", () => PreviewRequested?.Invoke(Checked(), exportKind.Selected == 1));
        previewSelected.TooltipText = "Show the combined script the export would write.";
        exportSelected = ButtonIn(footer, "Export selected…", () => ExportRequested?.Invoke(Checked(), exportKind.Selected == 1));
        exportSelected.TooltipText = "Save the checked changes into one SQL file for sharing.";
        discardAll = ButtonIn(footer, "Discard staged…", () => DiscardAllStagedRequested?.Invoke());
        discardAll.TooltipText = "Throw away every staged (not yet applied) change.";
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

    public void SetShowAll(bool value)
    {
        if (showAll != null && showAll.ButtonPressed != value)
        {
            showAll.ButtonPressed = value;
        }
    }

    List<string> Checked()
    {
        var selected = new List<string>();
        void Walk(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is CheckBox box && box.ButtonPressed && box.HasMeta("fingerprint"))
                {
                    selected.Add(box.GetMeta("fingerprint").AsString());
                }

                Walk(child);
            }
        }

        Walk(list);
        return selected;
    }

    /// <summary>
    /// Rebuild the rows from the change history. Staged rows can be applied or discarded, applied
    /// rows can be reverted; notes save when the field loses focus or Enter is pressed. Refreshes
    /// with identical content are skipped, and rebuilds preserve the checked boxes, in-progress
    /// notes, focus and scroll position, so the periodic sync never disturbs the selection.
    /// </summary>
    public void RefreshHistory(IReadOnlyList<ChangeRecord> records, string? historyError = null, string? currentSource = null)
    {
        if (shownRecords != null && shownError == historyError && currentSourceKey == currentSource && shownRecords.SequenceEqual(records))
        {
            return;
        }

        shownRecords = records.ToList();
        shownError = historyError;
        currentSourceKey = currentSource;
        // Rows that disappeared must not come back pre-checked if the same fingerprint is recreated.
        checkedFingerprints.IntersectWith(records.Select(r => r.Fingerprint));

        // Capture what the user is doing before the rows are replaced.
        var notesText = new Dictionary<string, string>(StringComparer.Ordinal);
        string? focused = null;
        var focusOwner = GetViewport()?.GuiGetFocusOwner();
        int scrollPos = scroll?.ScrollVertical ?? 0;
        foreach (var row in list.GetChildren())
        {
            string? fingerprint = FingerprintOf(row);
            if (fingerprint == null)
            {
                continue;
            }

            var field = FindFirst<LineEdit>(row);
            if (field != null)
            {
                notesText[fingerprint] = field.Text;
                if (field == focusOwner)
                {
                    focused = fingerprint;
                }
            }
        }

        foreach (var child in list.GetChildren())
        {
            list.RemoveChild(child);
            child.QueueFree();
        }

        int staged = records.Count(r => r.Status == ChangeStatus.Staged);
        int applied = records.Count(r => r.Status == ChangeStatus.Applied);
        summary.Text = records.Count == 0
            ? "No tracked changes yet. Edit something and it stages here."
            : $"{records.Count} change(s): {staged} staged, {applied} applied.";
        if (!string.IsNullOrEmpty(historyError))
        {
            summary.Text += " " + historyError;
        }

        bool any = records.Count > 0;
        bool unavailable = !string.IsNullOrEmpty(historyError);
        applySelected.Disabled = revertSelected.Disabled = !any || unavailable;
        previewSelected.Disabled = exportSelected.Disabled = !any;
        discardAll.Disabled = staged == 0 || unavailable;

        Control? focusTarget = null;
        foreach (var record in records)
        {
            var row = new VBoxContainer();
            row.AddThemeConstantOverride("separation", 2);
            var top = new HBoxContainer();
            top.AddThemeConstantOverride("separation", 8);
            string fp = record.Fingerprint;
            var box = new CheckBox();
            box.SetMeta("fingerprint", fp);
            box.ButtonPressed = checkedFingerprints.Contains(fp);
            box.Toggled += pressed =>
            {
                if (pressed)
                {
                    checkedFingerprints.Add(fp);
                }
                else
                {
                    checkedFingerprints.Remove(fp);
                }
            };
            top.AddChild(box);
            bool foreign = currentSourceKey != null && record.SourceKey != currentSourceKey;
            var source = ClientStyle.Text(SourceLabel(record.SourceKey), 11, ClientStyle.Faded);
            source.TooltipText = foreign
                ? "Another world database: " + record.SourceKey
                : "World database: " + record.SourceKey;
            top.AddChild(source);
            top.AddChild(ClientStyle.Tag(record.Status switch
            {
                ChangeStatus.Staged => "STAGED",
                ChangeStatus.Applied => "APPLIED",
                ChangeStatus.Reverted => "REVERTED",
                _ => "FAILED"
            }));
            var when = record.Status == ChangeStatus.Applied && record.AppliedAt != null
                ? $"applied {record.AppliedAt:yyyy-MM-dd HH:mm} UTC"
                : record.Status == ChangeStatus.Reverted && record.RevertedAt != null
                    ? $"reverted {record.RevertedAt:yyyy-MM-dd HH:mm} UTC"
                    : $"staged {record.CreatedAt:yyyy-MM-dd HH:mm} UTC";
            var time = ClientStyle.Text(when, 12, ClientStyle.Faded);
            time.CustomMinimumSize = new Vector2(190, 0);
            top.AddChild(time);
            var text = ClientStyle.Text(record.Label, 13);
            text.TooltipText = record.Label;
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            top.AddChild(text);
            if (record.Status == ChangeStatus.Staged)
            {
                var apply = ButtonIn(top, "Apply", () => ApplyRequested?.Invoke([fp]));
                var discard = ButtonIn(top, "Discard", () => DiscardStagedRequested?.Invoke([fp]));
                if (foreign)
                {
                    apply.Disabled = discard.Disabled = true;
                    apply.TooltipText = discard.TooltipText = "This change belongs to another world; connect to that world to run it.";
                }
            }
            else if (record.Status == ChangeStatus.Applied)
            {
                var revert = ButtonIn(top, "Revert", () => RevertRequested?.Invoke([fp]));
                if (foreign)
                {
                    revert.Disabled = true;
                    revert.TooltipText = "This change belongs to another world; connect to that world to revert it.";
                }
            }

            row.AddChild(top);
            var notes = new LineEdit
            {
                // The user's in-progress text wins over the stored note, so typing through a
                // background refresh never loses a keystroke.
                Text = notesText.TryGetValue(fp, out var typed) ? typed : record.Notes ?? "",
                PlaceholderText = "Optional note…",
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            notes.TooltipText = "Optional note saved with this change.";
            notes.TextSubmitted += text => NotesChanged?.Invoke(fp, text);
            notes.FocusExited += () => NotesChanged?.Invoke(fp, notes.Text);
            row.AddChild(notes);
            if (fp == focused)
            {
                focusTarget = notes;
            }

            list.AddChild(row);
        }

        if (scroll != null)
        {
            scroll.ScrollVertical = scrollPos;
        }

        if (focusTarget != null)
        {
            focusTarget.GrabFocus();
        }
    }

    static string SourceLabel(string sourceKey) =>
        sourceKey.Length <= 8 ? sourceKey : sourceKey[..8];

    static string? FingerprintOf(Node row)
    {
        var box = FindFirst<CheckBox>(row);
        return box != null && box.HasMeta("fingerprint") ? box.GetMeta("fingerprint").AsString() : null;
    }

    static T? FindFirst<T>(Node node) where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
            {
                return match;
            }

            var deeper = FindFirst<T>(child);
            if (deeper != null)
            {
                return deeper;
            }
        }

        return null;
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

    public void OpenText(string combined)
    {
        exports = [];
        text.Text = combined;
        PopupCentered();
    }

    void Render()
    {
        if (exports.Count == 0 && string.IsNullOrEmpty(text.Text))
        {
            text.Text = "-- Nothing to export.";
            return;
        }

        if (exports.Count == 0)
        {
            return;
        }

        bool revert = kind.Selected == 1;
        text.Text = exports.Count == 0
            ? "-- Nothing to export."
            : string.Join("\n\n", exports.Select(e => (exports.Count > 1 ? $"-- Source {e.Source}\n" : "") + (revert ? e.Scripts.Revert : e.Scripts.Apply)));
    }
}
