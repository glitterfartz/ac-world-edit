using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>How many stored spawns of a layer sit in each phase of a region, named from `phase_name` where it exists.</summary>
public static class TcPhaseSource
{
    public static async Task<PhaseCatalog> ReadAsync(WorldReadSession session, CreatureRegion region, string table, CancellationToken token)
    {
        bool named = session.Has("phase_name.ID") && session.Has("phase_name.Name");
        await using var command = session.Command($"""
            SELECT s.PhaseId,{(named ? "COALESCE(MAX(n.Name),'')" : "''")},COUNT(*)
            FROM {table} s {(named ? "LEFT JOIN phase_name n ON n.ID=s.PhaseId" : "")}
            WHERE s.PhaseId<>0 AND s.map=@map AND s.position_x>@minX AND s.position_x<=@maxX AND s.position_y>@minY AND s.position_y<=@maxY
            GROUP BY s.PhaseId ORDER BY s.PhaseId LIMIT 4096
            """);
        WorldReadSession.AddRegion(command, region, null, 1);
        List<PhaseInfo> phases = [];
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            phases.Add(new(reader.GetUInt32(0), reader.GetString(1), (int)reader.GetInt64(2)));
        }

        return new(phases.ToArray());
    }
}
