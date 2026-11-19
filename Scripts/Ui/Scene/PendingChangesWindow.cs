using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Change history: every staged and applied world change with its timestamp, status and optional
/// note. Staged rows come from local drafts; applying runs their SQL against the world database and
/// marks them applied in acworld. Applied rows revert by running their stored revert SQL. Any
/// checked rows export into one SQL file for sharing, and finished history can be trimmed by age.
/// The window only presents the list and raises requests; the viewer owns the drafts and the
/// database, so this window never touches them.
/// </summary>
public partial class PendingChangesWindow : ContentEditorWindow
{
    public event Action<IReadOnlyList<string>>? ApplyRequested;
    public event Action<IReadOnlyList<string>>? RevertRequested;
    public event Action<IReadOnlyList<string>>? DiscardStagedRequested;
    public event Action? DiscardAllStagedRequested;
    public event Action<int>? DeleteOldRequested;
    public event Action<IReadOnlyList<string>, bool>? ExportRequested;
    public event Action<IReadOnlyList<string>, bool>? PreviewRequested;
    public event Action<string, string>? NotesChanged;
    public event Action<bool>? ShowAllChanged;
    public event Action? HistoryRefreshRequested;

    VBoxContainer list = null!;
    ScrollContainer scroll = null!;
    Label summary = null!;
    Button applySelected = null!, revertSelected = null!, previewSelected = null!, exportSelected = null!, discardAll = null!, refreshButton = null!, deleteHistory = null!;
    CheckButton showAll = null!;
    CheckBox selectAll = null!;
    OptionButton exportKind = null!;

    /// <summary>
    /// Checked fingerprints, kept outside the rows so the periodic history refresh never drops the
    /// user's selection. Row boxes update this set when toggled and read it when rebuilt.
    /// </summary>
    readonly HashSet<string> checkedFingerprints = new(StringComparer.Ordinal);
    List<ChangeRecord>? shownRecords;
    string? shownError;
    string? currentSourceKey;
    bool syncingSelectAll;

    protected override Vector2 TargetSize => new(860, 560);

    public PendingChangesWindow() : base("PendingChanges", "Change History")
    {
    }

    protected override void BuildEditor()
    {
        summary = ClientStyle.Text("", 12, ClientStyle.Faded);
        Body.AddChild(summary);
        var selectBar = new HBoxContainer();
        Body.AddChild(selectBar);
        selectAll = new CheckBox { Text = "Select all", Disabled = true };
        selectAll.TooltipText = "Check or clear every change in the list.";
        selectAll.Toggled += pressed => SetAllChecked(pressed);
        selectBar.AddChild(selectAll);
        scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        Body.AddChild(scroll);
        list = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(list);

        applySelected = ButtonIn(Actions, "Apply selected", () => ApplyRequested?.Invoke(Checked()));
        applySelected.TooltipText = "Run the checked staged changes against the world database.";
        revertSelected = ButtonIn(Actions, "Revert selected", () => RevertRequested?.Invoke(Checked()));
        revertSelected.TooltipText = "Undo the checked applied changes by running their revert scripts.";
        previewSelected = ButtonIn(Actions, "Preview…", () => PreviewRequested?.Invoke(Checked(), exportKind.Selected == 1));
        previewSelected.TooltipText = "Show the combined script the export would write.";
        exportSelected = ButtonIn(Actions, "Export selected…", () => ExportRequested?.Invoke(Checked(), exportKind.Selected == 1));
        exportSelected.TooltipText = "Save the checked changes into one SQL file for sharing.";
        discardAll = ButtonIn(Actions, "Discard staged…", () => DiscardAllStagedRequested?.Invoke());
        discardAll.TooltipText = "Throw away every staged (not yet applied) change.";

        showAll = new CheckButton { Text = "Show all worlds" };
        showAll.TooltipText = "Show changes from every world database, not just the open one.";
        showAll.Toggled += pressed => ShowAllChanged?.Invoke(pressed);
        Actions.AddChild(showAll);
        exportKind = new OptionButton();
        exportKind.AddItem("Export: apply script");
        exportKind.AddItem("Export: revert script");
        exportKind.TooltipText = "Whether Export selected writes the apply or the revert scripts.";
        Actions.AddChild(exportKind);
        refreshButton = new Button { Text = "Refresh" };
        refreshButton.TooltipText = "Re-read drafts and the change history.";
        refreshButton.Pressed += () => HistoryRefreshRequested?.Invoke();
        Actions.AddChild(refreshButton);
        deleteHistory = new Button { Text = "Delete" };
        deleteHistory.TooltipText = "Delete applied, reverted and failed changes of the open world from the history. Staged edits stay.";
        deleteHistory.Pressed += OpenDeleteDialog;
        Actions.AddChild(deleteHistory);
    }

    /// <summary>Show the window, centered over the viewer on first open.</summary>
    public new void Open() => base.Open();


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
        int failed = records.Count(r => r.Status == ChangeStatus.Failed);
        summary.Text = records.Count == 0
            ? "No tracked changes yet. Edit something and it stages here."
            : $"{records.Count} change(s): {staged} staged, {applied} applied" + (failed > 0 ? $", {failed} failed." : ".");
        if (!string.IsNullOrEmpty(historyError))
        {
            summary.Text += " " + historyError;
        }

        bool any = records.Count > 0;
        bool unavailable = !string.IsNullOrEmpty(historyError);
        applySelected.Disabled = revertSelected.Disabled = !any || unavailable;
        previewSelected.Disabled = exportSelected.Disabled = !any;
        discardAll.Disabled = staged == 0 || unavailable;
        deleteHistory.Disabled = CountFinished(0) == 0 || unavailable;

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

                UpdateSelectAllState();
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
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
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
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
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

        UpdateSelectAllState();

        if (focusTarget != null)
        {
            focusTarget.GrabFocus();
        }
    }

    /// <summary>Check or clear every row in the list, then mirror the header box.</summary>
    void SetAllChecked(bool selected)
    {
        if (syncingSelectAll || shownRecords == null)
        {
            return;
        }

        if (selected)
        {
            foreach (var record in shownRecords)
            {
                checkedFingerprints.Add(record.Fingerprint);
            }
        }
        else
        {
            checkedFingerprints.Clear();
        }

        syncingSelectAll = true;
        foreach (var row in list.GetChildren())
        {
            if (FindFirst<CheckBox>(row) is { } box && box.HasMeta("fingerprint"))
            {
                box.SetPressedNoSignal(selected);
            }
        }

        syncingSelectAll = false;
        UpdateSelectAllState();
    }

    /// <summary>The header box reads checked only when every visible row is checked.</summary>
    void UpdateSelectAllState()
    {
        if (selectAll == null || shownRecords == null)
        {
            return;
        }

        bool all = shownRecords.Count > 0 && shownRecords.All(r => checkedFingerprints.Contains(r.Fingerprint));
        syncingSelectAll = true;
        selectAll.Disabled = shownRecords.Count == 0;
        selectAll.SetPressedNoSignal(all);
        syncingSelectAll = false;
    }

    static string SourceLabel(string sourceKey) =>
        sourceKey.Length <= 8 ? sourceKey : sourceKey[..8];

    /// <summary>
    /// Ask for an age and raise the cleanup request. Only finished rows of the open world count, so
    /// "Show all worlds" never widens what a delete would remove.
    /// </summary>
    void OpenDeleteDialog()
    {
        var dialog = new DeleteChangesDialog(CountFinished);
        dialog.DeleteRequested += days => DeleteOldRequested?.Invoke(days);
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>Applied, reverted and failed rows of the open world older than the given age; 0 days means all.</summary>
    int CountFinished(int days)
    {
        if (shownRecords == null || currentSourceKey == null)
        {
            return 0;
        }

        DateTime cutoff = days <= 0 ? DateTime.UtcNow : DateTime.UtcNow.AddDays(-days);
        return shownRecords.Count(r =>
        {
            if (r.SourceKey != currentSourceKey || r.Status is not (ChangeStatus.Applied or ChangeStatus.Reverted or ChangeStatus.Failed))
            {
                return false;
            }

            DateTime when = r.Status switch
            {
                ChangeStatus.Reverted => r.RevertedAt ?? r.CreatedAt,
                ChangeStatus.Applied => r.AppliedAt ?? r.CreatedAt,
                _ => r.CreatedAt
            };
            return when < cutoff;
        });
    }

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
