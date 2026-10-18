namespace ACWorldEdit.Assets;

/// <summary>
/// Client DBC lookups for NPC interactions. Field offsets (3.3.5a enUS) were checked against known
/// rows: Spell.dbc 136 name / 153 rank (spell 133 "Fireball", "Rank 1"); TaxiNodes.dbc 1 map,
/// 2-4 x/y/z, 5 name (node 2 "Stormwind, Elwynn"); ItemExtendedCost.dbc 1 honor, 2 arena,
/// 4-8 items, 9-13 counts (2425 = 10000 honor); ItemDisplayInfo.dbc 5 inventory icon (1542
/// "INV_Sword_04"); Emotes.dbc 1 name (3 "ONESHOT_WAVE(DNR)"); EmotesText.dbc 1 command (101
/// "WAVE"); Faction.dbc 23 name (72 "Stormwind"); FactionTemplate.dbc 1 faction. Built on a worker;
/// immutable afterwards.
/// </summary>
public sealed class InteractionCatalog
{
    readonly DbcTable spells, taxi, costs, displays, emotes, textEmotes, factions, factionTemplates;
    readonly Dictionary<uint, string> spellNames = [];

    InteractionCatalog(DbcTable spells, DbcTable taxi, DbcTable costs, DbcTable displays, DbcTable emotes, DbcTable textEmotes, DbcTable factions, DbcTable factionTemplates)
    {
        this.spells = spells;
        this.taxi = taxi;
        this.costs = costs;
        this.displays = displays;
        this.emotes = emotes;
        this.textEmotes = textEmotes;
        this.factions = factions;
        this.factionTemplates = factionTemplates;
        foreach (uint id in spells.Ids)
        {
            string name = spells.Str(id, 136);
            string rank = spells.Str(id, 153);
            spellNames[id] = rank.Length > 0 ? $"{name} ({rank})" : name;
        }
    }

    public static InteractionCatalog Load(IClientFiles store) => new(
        new DbcTable(store.Read("DBFilesClient/Spell.dbc")),
        new DbcTable(store.Read("DBFilesClient/TaxiNodes.dbc")),
        new DbcTable(store.Read("DBFilesClient/ItemExtendedCost.dbc")),
        new DbcTable(store.Read("DBFilesClient/ItemDisplayInfo.dbc")),
        new DbcTable(store.Read("DBFilesClient/Emotes.dbc")),
        new DbcTable(store.Read("DBFilesClient/EmotesText.dbc")),
        new DbcTable(store.Read("DBFilesClient/Faction.dbc")),
        new DbcTable(store.Read("DBFilesClient/FactionTemplate.dbc")));

    /// <summary>
    /// Readable animation emote: "ONESHOT_WAVE(DNR)" becomes "wave".
    /// </summary>
    public string? EmoteName(uint id) => emotes.Contains(id) ? System.Text.RegularExpressions.Regex.Replace(emotes.Str(id, 1), @"^(ONESHOT_|STATE_)|\(.*?\)", "").Replace('_', ' ').Trim().ToLowerInvariant() : null;

    public string? TextEmoteName(uint id) => textEmotes.Contains(id) ? textEmotes.Str(id, 1).ToLowerInvariant() : null;

    public string? FactionName(uint id) => factions.Contains(id) ? factions.Str(id, 23) : null;

    public string? FactionTemplateName(uint id) => factionTemplates.Contains(id) && FactionName(factionTemplates.U(id, 1)) is { } name ? $"{name} ({id})" : null;

    /// <summary>
    /// All IDs with names for dropdown pickers (emotes, text emotes, factions).
    /// </summary>
    public IEnumerable<(uint Id, string Name)> All(string kind) => kind switch
    {
        "Emote" => emotes.Ids.Select(id => (id, EmoteName(id) ?? "")),
        "TextEmote" => textEmotes.Ids.Select(id => (id, TextEmoteName(id) ?? "")),
        "Faction" => factions.Ids.Select(id => (id, FactionName(id) ?? "")),
        "FactionTemplate" => factionTemplates.Ids.Select(id => (id, FactionTemplateName(id) ?? "")),
        _ => []
    };

    public string? SpellName(uint id) => spellNames.TryGetValue(id, out var name) ? name : null;

    /// <summary>
    /// Spells whose name contains the term (or whose ID equals it), bounded for picker lists.
    /// </summary>
    public IReadOnlyList<(uint Id, string Name)> SearchSpells(string term, int limit = 50)
    {
        term = term.Trim();
        if (term.Length == 0)
        {
            return [];
        }

        uint.TryParse(term, out uint exact);
        return spellNames.Where(s => s.Key == exact || s.Value.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Key == exact ? 0 : 1).ThenBy(s => s.Value.Length).ThenBy(s => s.Key).Take(limit).Select(s => (s.Key, s.Value)).ToList();
    }

    public bool ExtendedCostExists(uint id) => costs.Contains(id);

    /// <summary>
    /// Readable extended cost: honor, arena points and item requirements (item IDs; names come from
    /// item_template when the caller has them).
    /// </summary>
    public string DescribeExtendedCost(uint id, Func<uint, string?>? itemName = null)
    {
        if (!costs.Contains(id))
        {
            return $"Extended cost {id} (missing)";
        }

        List<string> parts = [];
        if (costs.U(id, 1) != 0)
        {
            parts.Add($"{costs.U(id, 1)} honor");
        }

        if (costs.U(id, 2) != 0)
        {
            parts.Add($"{costs.U(id, 2)} arena");
        }

        for (int i = 0; i < 5; i++)
        {
            uint item = costs.U(id, 4 + i), count = costs.U(id, 9 + i);
            if (item != 0)
            {
                parts.Add($"{count}× {itemName?.Invoke(item) ?? $"item {item}"}");
            }
        }

        return parts.Count == 0 ? $"Extended cost {id}" : string.Join(", ", parts);
    }

    /// <summary>
    /// Client icon texture path for an item display ID, or null when unknown.
    /// </summary>
    public string? ItemIconPath(uint displayId) => displays.Contains(displayId) && displays.Str(displayId, 5) is { Length: > 0 } icon ? $"Interface/Icons/{icon}.blp" : null;

    /// <summary>
    /// Nearest taxi node on the same map (server coordinates), as flight masters use it.
    /// </summary>
    public (uint Id, string Name, double Distance)? NearestTaxiNode(uint map, double x, double y, double z)
    {
        (uint, string, double)? best = null;
        foreach (uint id in taxi.Ids)
        {
            if (taxi.U(id, 1) != map)
            {
                continue;
            }

            double d = Math.Sqrt(Math.Pow(taxi.F(id, 2) - x, 2) + Math.Pow(taxi.F(id, 3) - y, 2) + Math.Pow(taxi.F(id, 4) - z, 2));
            if (best == null || d < best.Value.Item3)
            {
                best = (id, taxi.Str(id, 5), d);
            }
        }

        return best;
    }
}
