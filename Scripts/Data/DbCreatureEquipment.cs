using System.Globalization;

namespace ACWorldEdit.Data;

/// <summary>The equipment sets a creature template can wear (creature_equip_template), as IDs and item IDs.</summary>
internal static class DbCreatureEquipment
{
    public static async Task<IReadOnlyList<(uint Id, uint First, uint Second, uint Third)>> ReadAsync(WorldDatabase database, uint entry, CancellationToken token)
    {
        await using var session = await database.OpenAsync(token).ConfigureAwait(false);
        await using var command = session.Command("SELECT ID,ItemID1,ItemID2,ItemID3 FROM creature_equip_template WHERE CreatureID=@entry ORDER BY ID");
        command.Parameters.AddWithValue("@entry", entry);
        List<(uint, uint, uint, uint)> rows = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            rows.Add((Convert.ToUInt32(reader.GetValue(0), CultureInfo.InvariantCulture), Convert.ToUInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                Convert.ToUInt32(reader.GetValue(2), CultureInfo.InvariantCulture), Convert.ToUInt32(reader.GetValue(3), CultureInfo.InvariantCulture)));
        }

        return rows;
    }
}
