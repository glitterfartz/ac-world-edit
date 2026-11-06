using System.Globalization;
using System.Text;

namespace ACWorldEdit.Foundation;

/// <summary>
/// Groups net draft changes by their source database and writes the resulting script pairs. Plain
/// files and text only: nothing here connects to a database, so the viewer can hand it a folder and
/// the tests can hand it a temporary one.
/// </summary>
public static class SqlExportBatch
{
    /// <summary>
    /// One script pair per database source, ordered by source and written in that source's dialect.
    /// </summary>
    public static List<(string Source, SqlScripts Scripts)> Build(IEnumerable<PendingChange> changes, Func<string, IWorldSchema> schemaFor) => changes
        .GroupBy(c => c.Source)
        .OrderBy(g => g.Key, StringComparer.Ordinal)
        .Select(g => (g.Key, SqlExport.Build(
            g.Key,
            g.Where(c => c.Graveyard != null).Select(c => c.Graveyard!),
            g.Where(c => c.Row != null).Select(c => c.Row!),
            null,
            schemaFor(g.Key))))
        .ToList();

    /// <summary>
    /// Write each pair as UTF-8 (no BOM) files in a new timestamped folder under
    /// <paramref name="root"/> and return that folder. The first eight characters of the source name
    /// name the files, which is the long-standing export convention.
    /// </summary>
    public static string Write(IEnumerable<(string Source, SqlScripts Scripts)> exports, string root, DateTime? now = null)
    {
        string stamp = (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string folder = Path.Combine(root, stamp);
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
}
