namespace ACWorldEdit.Foundation;

/// <summary>
/// Groups net draft changes by their source database and builds the resulting script pairs.
/// Plain text only: nothing here connects to a database.
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
}
