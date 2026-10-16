using System.Text;
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// File > Export SQL: collects the net changes of the creature, graveyard and content drafts and
/// writes a reviewable apply/revert script pair per source database. Export is the only way a draft
/// leaves the application; nothing here connects to a database, and the drafts are left unchanged so
/// export can be repeated.
/// </summary>
public partial class TerrainViewer
{
    /// <summary>
    /// One script pair per database source that has net draft changes, ordered by source.
    /// </summary>
    internal List<(string Source, SqlScripts Scripts)> BuildSqlExports()
    {
        var creatureMoves = creatures.NetMoves();
        var graveyardEdits = graveyards.NetEdits();
        var rowEdits = drafts.Draft?.NetEdits() ?? [];
        var sources = creatureMoves.Select(m => m.Source)
            .Concat(graveyardEdits.Select(e => e.Source))
            .Concat(rowEdits.Select(e => e.Source))
            .Distinct()
            .Order(StringComparer.Ordinal);
        return sources.Select(source => (source, SqlExport.Build(
            source,
            creatureMoves.Where(m => m.Source == source),
            graveyardEdits.Where(e => e.Source == source),
            rowEdits.Where(e => e.Source == source).Select(e => e.Edit)))).ToList();
    }

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

    void ExportSql()
    {
        var exports = BuildSqlExports();
        if (exports.Count == 0)
        {
            ShowInfo("Export SQL", "Nothing to export: the local drafts hold no net changes.");
            return;
        }

        var text = new StringBuilder();
        foreach (var (source, scripts) in exports)
        {
            text.Append($"Database {(source.Length > 8 ? source[..8] : source)} · {scripts.Changes} change(s)\n");
            foreach (string line in scripts.Summary.Take(40))
            {
                text.Append("  • ").Append(line).Append('\n');
            }

            if (scripts.Summary.Count > 40)
            {
                text.Append($"  … and {scripts.Summary.Count - 40} more\n");
            }

            text.Append('\n');
        }

        text.Append("Saves an apply script and a revert script per database. The viewer never runs them: review, back up, run the apply script yourself, and check that every preflight row reads ok. Rows changed since the draft was made are skipped.");
        var label = new Label
        {
            Text = text.ToString(),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(620, 0)
        };
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(640, 360)
        };
        scroll.AddChild(label);
        var dialog = new ConfirmationDialog
        {
            Title = "Export SQL",
            OkButtonText = "Save scripts"
        };
        dialog.AddChild(scroll);
        dialog.Confirmed += () =>
        {
            dialog.QueueFree();
            try
            {
                string folder = WriteSqlExports(exports);
                ShowInfo("Export SQL", $"Saved to:\n{folder}");
                OS.ShellOpen(folder);
            }
            catch (Exception)
            {
                ShowInfo("Export SQL", "Could not write the scripts. Nothing was changed.");
            }
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
    }
}
