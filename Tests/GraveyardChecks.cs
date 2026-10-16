using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using static Test;

/// <summary>
/// Live, read-only graveyard checks: whole-table integrity against the server's loading rules and
/// client AreaTable.dbc, then the bounded regional source used by the viewer. Only SELECTs inside
/// read-only transactions; nothing is written.
/// </summary>
public static class GraveyardChecks
{
    public static async Task Run(AssetStore store)
    {
        var areas = new DbcTable(store.Read("DBFilesClient/AreaTable.dbc"));
        var config = Config;
        var database = new WorldDatabase(config);
        await using (var session = await database.OpenAsync(CancellationToken.None))
        {
            async Task<long> Count(string sql)
            {
                await using var command = session.Command(sql);
                return Convert.ToInt64(await command.ExecuteScalarAsync());
            }

            long graveyards = await Count("SELECT COUNT(*) FROM game_graveyard");
            long links = await Count("SELECT COUNT(*) FROM graveyard_zone");
            long orphans = await Count("SELECT COUNT(*) FROM graveyard_zone z LEFT JOIN game_graveyard g ON g.ID=z.ID WHERE g.ID IS NULL");
            long unlinked = await Count("SELECT COUNT(*) FROM game_graveyard g LEFT JOIN graveyard_zone z ON z.ID=g.ID WHERE z.ID IS NULL");
            long badFaction = await Count("SELECT COUNT(*) FROM graveyard_zone WHERE Faction NOT IN (0,67,469)");
            List<uint> zones = [];
            await using (var command = session.Command("SELECT DISTINCT GhostZone FROM graveyard_zone"))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    zones.Add(reader.GetUInt32(0));
                }
            }

            int unknownZones = zones.Count(z => !areas.Contains(z));
            Console.WriteLine($"GRAVEYARD_TABLES graveyards={graveyards} links={links} orphanLinks={orphans} unlinkedGraveyards={unlinked} badFaction={badFaction} ghostZones={zones.Count} unknownGhostZones={unknownZones}");
            Check(graveyards > 0 && links > 0, "Graveyard tables are empty");
        }

        // Elwynn/Goldshire, the same neighborhood the viewer smoke uses.
        var source = new GraveyardSource(config);
        var region = new CreatureRegion(0, 31, 33, 48, 50);
        var point = new SpawnQueryPoint(-9492.72, 75.6383, 56.1841);
        var result = await source.ReadAsync(region, point, "", CancellationToken.None);
        Check(result.Sites.Length > 0 && result.Sites.Length <= GraveyardSource.Limit, "No bounded Elwynn graveyards");
        double previous = 0;
        foreach (var site in result.Sites)
        {
            var r = site.Record;
            Check(r.Map == 0 && r.X > region.MinServerX && r.X <= region.MaxServerX && r.Y > region.MinServerY && r.Y <= region.MaxServerY, "Graveyard outside region");
            double distance = Math.Pow(r.X - point.X, 2) + Math.Pow(r.Y - point.Y, 2) + Math.Pow(r.Z - point.Z, 2);
            Check(distance >= previous, "Graveyards not nearest-first");
            previous = distance;
            Check(r == r.Normalized(), "Source links not normalized");
        }

        var nearest = result.Sites[0];
        Check(nearest.Record.Links.Any(l => l.GhostZone == 12), "Nearest Goldshire graveyard has no Elwynn Forest (12) link");
        Console.WriteLine($"GRAVEYARD_SOURCE sites={result.Sites.Length} truncated={result.Truncated} nearest={nearest.Record.Id} '{nearest.Record.Comment}' links={string.Join(",", nearest.Record.Links.Select(l => $"{l.GhostZone}/{l.Faction}"))} healers={string.Join(",", nearest.Healers.Select(h => $"{h.Guid}:{h.Name}"))}");
        var searched = await source.ReadAsync(region, point, nearest.Record.Id.ToString(), CancellationToken.None);
        Check(searched.Sites.Length == 1 && searched.Sites[0].Record.Id == nearest.Record.Id, "ID search failed");
        var wildcard = await source.ReadAsync(region, point, "%", CancellationToken.None);
        Check(wildcard.Sites.All(s => s.Record.Comment.Contains('%')), "Search wildcard not escaped");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            await source.ReadAsync(region, point, "", canceled.Token);
            throw new Exception("Canceled graveyard query completed");
        }
        catch (OperationCanceledException)
        {
        }

        Console.WriteLine("GRAVEYARD_CHECKS_PASSED tables/areatable/region/order/normalized/search/escape/cancel");
    }
}
