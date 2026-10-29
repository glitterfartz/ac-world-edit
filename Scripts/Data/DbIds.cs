using System.Globalization;
using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// The largest ID a table's key column holds, for <see cref="IdAllocator"/>. Table and column names
/// come from a <see cref="TableSchema"/>, never from user text, and the column must be a whole number.
/// </summary>
internal static class DbIds
{
    public static async Task<ulong> MaxAsync(WorldDatabase database, TableSchema table, string column, CancellationToken token)
    {
        var kind = table.Columns.FirstOrDefault(c => c.Name == column).Kind;
        if (!table.Has(column) || kind is ColumnKind.Float or ColumnKind.Text)
        {
            throw new InvalidDataException($"{table.Name}.{column} is not a whole-number column.");
        }

        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        object? max = await AcInteractionSource.Scalar(session, $"SELECT COALESCE(MAX(`{column}`),0) FROM `{table.SqlName}`", [], token).ConfigureAwait(false);
        decimal value = Convert.ToDecimal(max, CultureInfo.InvariantCulture);
        // A signed column can be negative; no new ID comes from below zero.
        return value < 0 ? 0 : (ulong)value;
    }
}
