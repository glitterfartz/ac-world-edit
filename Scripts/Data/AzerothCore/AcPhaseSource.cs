using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>How many stored spawns of a layer sit in each phase of a region, whatever the preview's phase filter says.</summary>
public static class AcPhaseSource
{
    public static async Task<PhaseCatalog> ReadAsync(WorldReadSession session, CreatureRegion region, string table, CancellationToken token)
    {
        await using var command = session.Command($"""
            SELECT phaseMask,COUNT(*) FROM {table}
            WHERE map=@map AND position_x>@minX AND position_x<=@maxX AND position_y>@minY AND position_y<=@maxY
            GROUP BY phaseMask LIMIT 4096
            """);
        WorldReadSession.AddRegion(command, region, null, 1);
        List<(uint Mask, int Spawns)> groups = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            groups.Add((reader.GetUInt32(0), (int)reader.GetInt64(1)));
        }

        return AcPhaseMasks.Catalog(groups);
    }
}
