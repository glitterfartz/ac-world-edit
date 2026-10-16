using System.Text;
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Pending Changes: lists the net changes of the creature, graveyard and content drafts and
/// writes a reviewable apply/revert script pair per source database. Export is the only way a draft
/// leaves the application; nothing here connects to a database. Exporting does not change the drafts;
/// it records the changes as exported so they stop counting as unsaved.
/// </summary>
public partial class TerrainViewer
{
    /// <summary>
    /// One script pair per database source for these changes, ordered by source.
    /// </summary>
    internal static List<(string Source, SqlScripts Scripts)> BuildSqlExports(IEnumerable<PendingChange> changes) => changes
        .GroupBy(c => c.Source)
        .OrderBy(g => g.Key, StringComparer.Ordinal)
        .Select(g => (g.Key, SqlExport.Build(
            g.Key,
            g.Where(c => c.Move != null).Select(c => c.Move!),
            g.Where(c => c.Graveyard != null).Select(c => c.Graveyard!),
            g.Where(c => c.Row != null).Select(c => c.Row!))))
        .ToList();

    /// <summary>
    /// Write each pair as UTF-8 (no BOM) files in a new timestamped folder and return its path.
    /// </summary>
    internal static string WriteSqlExports(IEnumerable<(string Source, SqlScripts Scripts)> exports)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        string folder = Path.GetDirectoryName(ContentDrafts.LocalPath($"exports/{stamp}/x"))!;
        Directory.CreateDirectory(folder);
        var utf8 = new UTF8Encoding(false);
        foreach (var (source, scripts) in exports)
        {
            string name = source.Length > 8 ? source[..8] : source;
            File.WriteAllText(Path.Combine(folder, $"{name}-apply.sql"), scripts.Apply, utf8);
            File.WriteAllText(Path.Combine(folder, $"{name}-revert.sql"), scripts.Revert, utf8);
        }

        return folder;
    }


    PendingChanges pendingStore = null!;
    PendingChangesWindow pendingWindow = null!;
    double pendingPoll;
    int exportedHidden;

    /// <summary>
    /// Every net draft change that has not been exported, one entry per discardable unit.
    /// </summary>
    List<PendingChange> CollectPending()
    {
        pendingStore ??= new PendingChanges(ContentDrafts.LocalPath("exported.json"));
        var all = pendingStore.Collect(creatures.NetMoves(), graveyards.NetEdits(), drafts.Draft?.NetEdits() ?? []);
        return all;
    }

    /// <summary>
    /// Re-read the drafts, update the sidebar indicator and, when the window is open, its rows.
    /// Polled a few times a second because edits arrive from several editors.
    /// </summary>
    void RefreshPending()
    {
        var changes = CollectPending();
        sidebar.SetUnsaved(changes.Count);
        if (pendingWindow.Visible)
        {
            int total = creatures.NetMoves().Count + graveyards.NetEdits().Count + (drafts.Draft?.NetEdits().Count ?? 0);
            exportedHidden = total - changes.Count;
            pendingWindow.Refresh(changes, exportedHidden);
        }
    }

    void PollPending(double delta)
    {
        pendingPoll += delta;
        if (pendingPoll >= 0.4)
        {
            pendingPoll = 0;
            RefreshPending();
        }
    }

    void OpenPendingChanges()
    {
        pendingWindow.Open();
        RefreshPending();
    }

    /// <summary>
    /// Revert one change through the owning editor so markers and inspectors refresh. Only that
    /// creature, graveyard or row is touched.
    /// </summary>
    void DiscardPending(PendingChange change)
    {
        switch (change.Kind)
        {
            case PendingKind.Creature:
                creatures.Revert(change.Move!);
                break;
            case PendingKind.Graveyard:
                graveyards.Revert(change.Graveyard!);
                break;
            default:
                drafts.Revert(change.Source, change.Row!);
                break;
        }
    }

    void DiscardAllPending()
    {
        var changes = CollectPending();
        if (changes.Count == 0)
        {
            return;
        }

        var confirm = new ConfirmationDialog
        {
            Title = "Discard all",
            DialogText = $"Discard all {changes.Count} unsaved change(s) and return them to the database values? This only affects local drafts.",
            OkButtonText = "Discard all"
        };
        confirm.Confirmed += () =>
        {
            confirm.QueueFree();
            foreach (var change in changes)
            {
                DiscardPending(change);
            }

            RefreshPending();
            pendingWindow.SetStatus("Discarded all unsaved changes.");
        };
        confirm.Canceled += confirm.QueueFree;
        AddChild(confirm);
        confirm.PopupCentered();
    }

    /// <summary>
    /// Save scripts for every unsaved change, then mark them exported so the indicator clears. The
    /// drafts themselves are untouched; nothing connects to a database.
    /// </summary>
    void ExportAllPending()
    {
        var changes = CollectPending();
        if (changes.Count == 0)
        {
            return;
        }

        try
        {
            string folder = WriteSqlExports(BuildSqlExports(changes));
            pendingStore.MarkExported(changes);
            RefreshPending();
            pendingWindow.SetStatus("Exported.");
            ShowInfo("Export SQL", $"Saved {changes.Count} change(s) to:\n{folder}\n\nThe viewer never runs these scripts: review, back up, run the apply script yourself, and check that every preflight row reads ok. Rows changed since the draft was made are skipped.");
            OS.ShellOpen(folder);
        }
        catch (Exception)
        {
            pendingWindow.SetStatus("Could not write the scripts. Nothing was changed.");
        }
    }
}
