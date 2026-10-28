using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>
/// Live, read-only interaction checks: DBC field offsets against known rows, then real Goldshire
/// NPCs found by query (innkeeper, quest giver, vendor, trainer) read and built into views.
/// </summary>
[Trait("Category", "Db")]
public class InteractionDbTests
{
    [DbFact(NeedsClientData = true)]
    public async Task Passes() => await Core(ClientData.Store);

    static async Task Core(AssetStore store)
    {
        var catalog = InteractionCatalog.Load(store);
        Check(catalog.SpellName(133) == "Fireball (Rank 1)", "Spell.dbc name/rank offsets");
        Check(catalog.NearestTaxiNode(0, -8835, 490, 109) is { } node && node.Name.StartsWith("Stormwind", StringComparison.Ordinal), "TaxiNodes.dbc offsets");
        Check(catalog.ExtendedCostExists(2425) && catalog.DescribeExtendedCost(2425).Contains("10000 honor"), "ItemExtendedCost.dbc offsets");
        Check(catalog.ItemIconPath(1542) == "Interface/Icons/INV_Sword_04.blp", "ItemDisplayInfo.dbc icon offset");
        Check(catalog.SearchSpells("Fireball").Count > 0 && catalog.SearchSpells("").Count == 0, "Spell search");
        Console.WriteLine("INTERACTION_CATALOG_PASSED spell/taxi/extended-cost/icon offsets");

        var config = Config;
        var database = new WorldDatabase(config);
        var samples = new Dictionary<string, (uint Entry, uint Guid)>();
        await using (var session = await database.OpenAsync(CancellationToken.None))
        {
            string id = session.Has("creature.id1") ? "c.id1" : "c.id";
            // Goldshire neighborhood, nearest spawn per role.
            (string Role, string Filter)[] roles =
            [
                ("innkeeper", "(t.npcflag & 0x10000)<>0"),
                ("questgiver", $"EXISTS(SELECT 1 FROM creature_queststarter s WHERE s.id={id})"),
                ("vendor", $"EXISTS(SELECT 1 FROM npc_vendor v WHERE v.entry={id})"),
                ("trainer", $"EXISTS(SELECT 1 FROM creature_default_trainer d WHERE d.CreatureId={id})")
            ];
            foreach (var (role, filter) in roles)
            {
                await using var command = session.ReadOnly($"""
                    SELECT {id},c.guid FROM creature c JOIN creature_template t ON t.entry={id}
                    WHERE c.map=0 AND c.position_x BETWEEN -9700 AND -9200 AND c.position_y BETWEEN -300 AND 300 AND {filter}
                    ORDER BY POW(c.position_x+9460,2)+POW(c.position_y-30,2) LIMIT 1
                    """);
                await using var reader = await command.ExecuteReaderAsync();
                Check(await reader.ReadAsync(), $"No Goldshire {role} found");
                samples[role] = (reader.GetUInt32(0), reader.GetUInt32(1));
            }
        }

        var source = AcSource(config);
        foreach (var (role, (entry, guid)) in samples)
        {
            var snapshot = await source.ReadInteractionAsync(entry, guid, [], rows => rows, catalog, CancellationToken.None);
            var view = NpcInteraction.Build(entry, guid, snapshot.Name, snapshot.ScriptName, snapshot.AIName, snapshot.SpawnCount, snapshot.Rows, snapshot.Rows, snapshot.Lookup, snapshot.Scripts, snapshot.Conditions);
            int options = view.Menus.Values.Sum(m => m.Options.Length);
            Console.WriteLine($"INTERACTION_SAMPLE {role} entry={entry} '{view.Name}' <{view.Subname}> flags=[{string.Join(", ", NpcFlags.Names(view.NpcFlag))}] menus={view.Menus.Count} options={options} hidden={view.Menus.Values.Sum(m => m.Options.Count(o => o.HiddenByFlags))} starts={view.Starts.Length} ends={view.Ends.Length} vendor={view.Vendor.Length} trainerSpells={view.Trainer?.Spells.Length ?? 0} scripts={view.Scripts.Length} conditions={string.Join(",", view.Conditions.Select(c => $"{c.Key}:{c.Value}"))} errors={view.Issues.Count(i => i.Error)} warnings={view.Issues.Count(i => !i.Error)} spawns={view.SpawnCount}");
            switch (role)
            {
                case "innkeeper":
                    Check((view.NpcFlag & NpcFlags.Innkeeper) != 0 && options > 0 && view.Menus.Values.Any(m => m.Texts.Length > 0 && m.Texts.All(t => t.Male.Length > 0 || t.Female.Length > 0)), "Innkeeper gossip graph");
                    Check(view.Menus.Values.SelectMany(m => m.Options).Any(o => o.Type == 8), "Innkeeper bind option (type 8)");
                    break;
                case "questgiver":
                    Check(view.Starts.Length > 0 && view.Starts.All(q => q.Info is { Title.Length: > 0 }), "Quest titles");
                    break;
                case "vendor":
                    Check(view.Vendor.Length > 0 && view.Vendor.All(v => v.Info is { Name.Length: > 0 }), "Vendor item names");
                    Check(view.Vendor.Where(v => v.Info != null).All(v => catalog.ItemIconPath(v.Info!.DisplayId) != null || v.Info.DisplayId == 0), "Vendor item icons");
                    break;
                case "trainer":
                    Check(view.Trainer is { Exists: true, Spells.Length: > 0 } && view.Trainer.Spells.All(s => !s.Name.StartsWith("Spell ", StringComparison.Ordinal)), "Trainer spells with Spell.dbc names");
                    break;
            }
        }

        var items = await source.SearchItemDetailsAsync("Linen", CancellationToken.None);
        Check(items.Count is > 0 and <= DbLimits.MaxSearch && items.All(i => i.Info.Name.Contains("Linen", StringComparison.OrdinalIgnoreCase)), "Item search");
        var wildcard = await source.SearchItemDetailsAsync("%", CancellationToken.None);
        Check(wildcard.All(i => i.Info.Name.Contains('%')), "Item search escaping");
        Check((await source.SearchQuestsAsync("Kobold", CancellationToken.None)).Count > 0, "Quest search");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            await source.ReadInteractionAsync(samples["vendor"].Entry, 0, [], rows => rows, catalog, canceled.Token);
            throw new Exception("Canceled interaction read completed");
        }
        catch (OperationCanceledException)
        {
        }

        Console.WriteLine("INTERACTION_CHECKS_PASSED catalog/innkeeper/questgiver/vendor/trainer/search/escape/cancel");
    }
}
