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
    readonly DbcTable? skills, mails, titles, questInfos, questSorts, spellIcons;
    readonly DbcTable? chrClasses, creatureTypes, creatureFamilies;
    readonly Dictionary<uint, string> spellNames = [];

    InteractionCatalog(DbcTable spells, DbcTable taxi, DbcTable costs, DbcTable displays, DbcTable emotes, DbcTable textEmotes, DbcTable factions, DbcTable factionTemplates,
        DbcTable? skills, DbcTable? mails, DbcTable? titles, DbcTable? questInfos, DbcTable? questSorts, DbcTable? spellIcons,
        DbcTable? chrClasses, DbcTable? creatureTypes, DbcTable? creatureFamilies)
    {
        this.spells = spells;
        this.taxi = taxi;
        this.costs = costs;
        this.displays = displays;
        this.emotes = emotes;
        this.textEmotes = textEmotes;
        this.factions = factions;
        this.factionTemplates = factionTemplates;
        this.skills = skills;
        this.mails = mails;
        this.titles = titles;
        this.questInfos = questInfos;
        this.questSorts = questSorts;
        this.spellIcons = spellIcons;
        this.chrClasses = chrClasses;
        this.creatureTypes = creatureTypes;
        this.creatureFamilies = creatureFamilies;
        foreach (uint id in spells.Ids)
        {
            string name = spells.Str(id, 136);
            string rank = spells.Str(id, 153);
            spellNames[id] = rank.Length > 0 ? $"{name} ({rank})" : name;
        }
    }

    public static InteractionCatalog Load(IClientFiles store)
    {
        // Spell.dbc dominates; the tables are independent, so read and parse them concurrently.
        string[] names = ["Spell", "TaxiNodes", "ItemExtendedCost", "ItemDisplayInfo", "Emotes", "EmotesText", "Faction", "FactionTemplate"];
        var tables = new DbcTable[names.Length];
        try
        {
            Parallel.For(0, names.Length, i => tables[i] = new DbcTable(store.Read($"DBFilesClient/{names[i]}.dbc")));
        }
        catch (AggregateException e)
        {
            // Surface the real failure (e.g. a missing DBC), as the sequential reads did.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
        }

        // The editor reference tables are optional: a client missing one loses that picker's names,
        // not the whole catalog.
        DbcTable? Optional(string name)
        {
            try
            {
                return new DbcTable(store.Read($"DBFilesClient/{name}.dbc"));
            }
            catch (Exception)
            {
                return null;
            }
        }

        return new(tables[0], tables[1], tables[2], tables[3], tables[4], tables[5], tables[6], tables[7],
            Optional("SkillLine"), Optional("MailTemplate"), Optional("CharTitles"),
            Optional("QuestInfo"), Optional("QuestSort"), Optional("SpellIcon"),
            Optional("ChrClasses"), Optional("CreatureType"), Optional("CreatureFamily"));
    }

    /// <summary>
    /// Readable animation emote: "ONESHOT_WAVE(DNR)" becomes "wave".
    /// </summary>
    public string? EmoteName(uint id) => emotes.Contains(id) ? System.Text.RegularExpressions.Regex.Replace(emotes.Str(id, 1), @"^(ONESHOT_|STATE_)|\(.*?\)", "").Replace('_', ' ').Trim().ToLowerInvariant() : null;

    /// <summary>The AnimationData clip an emote loops (Emotes.dbc field 2: wave is 67, dance is 69),
    /// or 0 when the row is missing or names no clip (pure states like sit and sleep use 0).</summary>
    public uint EmoteAnim(uint id)
    {
        try
        {
            return emotes.U(id, 2);
        }
        catch (InvalidDataException)
        {
            return 0;
        }
    }

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

    /// <summary>The icon texture path of a spell (SpellIcon.dbc through Spell.dbc's SpellIconID), or null.</summary>
    public string? SpellIconPath(uint id)
    {
        if (spellIcons == null || !spells.Contains(id))
        {
            return null;
        }

        try
        {
            uint icon = spells.U(id, 133);
            if (icon == 0 || !spellIcons.Contains(icon))
            {
                return null;
            }

            string path = spellIcons.Str(icon, 1).Replace('\\', '/');
            if (path.Length == 0)
            {
                return null;
            }

            if (!path.EndsWith(".blp", StringComparison.OrdinalIgnoreCase))
            {
                path += ".blp";
            }

            return path.Contains('/') ? path : $"Interface/Icons/{path}";
        }
        catch (InvalidDataException)
        {
            // A client whose Spell.dbc has fewer fields than 3.3.5 simply has no spell icons.
            return null;
        }
    }

    /// <summary>A profession or secondary skill name (SkillLine.dbc).</summary>
    public string? SkillName(uint id) => skills != null && skills.Contains(id) ? skills.Str(id, 2) : null;

    public string? SkillIconPath(uint id)
    {
        if (skills == null || spellIcons == null || !skills.Contains(id))
        {
            return null;
        }

        try
        {
            uint icon = skills.U(id, 4);
            if (icon == 0 || !spellIcons.Contains(icon))
            {
                return null;
            }

            string path = spellIcons.Str(icon, 1).Replace('\\', '/');
            return path.Length == 0 ? null : path.Contains('/') ? path : $"Interface/Icons/{path}";
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Wrath's CurrencyTypes.dbc has no string block (currency names are client-side), so this is
    /// null; retail reads names from CurrencyTypes.db2 instead.
    /// </summary>
    public string? CurrencyName(uint id) => null;

    public string? MailSubject(uint id) => mails != null && mails.Contains(id) ? mails.Str(id, 18) : null;

    public string? TitleName(uint id) => titles != null && titles.Contains(id) ? titles.Str(id, 2) : null;

    public string? QuestInfoName(uint id) => questInfos != null && questInfos.Contains(id) ? questInfos.Str(id, 1) : null;

    public string? QuestSortName(uint id) => questSorts != null && questSorts.Contains(id) ? questSorts.Str(id, 1) : null;

    public string? ClassName(uint id) => chrClasses != null && chrClasses.Contains(id) ? chrClasses.Str(id, 4) : null;

    public string? CreatureTypeName(uint id) => creatureTypes != null && creatureTypes.Contains(id) ? creatureTypes.Str(id, 1) : null;

    public string? CreatureFamilyName(uint id) => creatureFamilies != null && creatureFamilies.Contains(id) ? creatureFamilies.Str(id, 10) : null;

    public IReadOnlyList<(uint Id, string Name)> Classes() => AllTable(chrClasses, 4);

    public IReadOnlyList<(uint Id, string Name)> CreatureTypes() => AllTable(creatureTypes, 1);

    public IReadOnlyList<(uint Id, string Name)> CreatureFamilies() => AllTable(creatureFamilies, 10);

    /// <summary>Faction templates by ID or by the reputation faction's name.</summary>
    public IReadOnlyList<(uint Id, string Name)> SearchFactionTemplates(string term, int limit)
    {
        term = term.Trim();
        uint.TryParse(term, out uint exact);
        return All("FactionTemplate")
            .Where(f => f.Name.Length > 0 && (term.Length == 0 || f.Id == exact || f.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f.Id == exact ? 0 : 1).ThenBy(f => f.Name.Length).ThenBy(f => f.Id)
            .Take(limit).ToList();
    }

    public IReadOnlyList<(uint Id, string Name)> SearchSkills(string term, int limit = 50) => SearchTable(skills, 2, term, limit);

    public IReadOnlyList<(uint Id, string Name)> SearchCurrencies(string term, int limit = 50) => [];

    public IReadOnlyList<(uint Id, string Name)> SearchMails(string term, int limit = 50) => SearchTable(mails, 18, term, limit);

    public IReadOnlyList<(uint Id, string Name)> SearchTitles(string term, int limit = 50) => SearchTable(titles, 2, term, limit);

    public IReadOnlyList<(uint Id, string Name)> QuestInfos() => AllTable(questInfos, 1);

    public IReadOnlyList<(uint Id, string Name)> QuestSorts() => AllTable(questSorts, 1);

    static IReadOnlyList<(uint Id, string Name)> AllTable(DbcTable? table, int nameField) =>
        table == null ? [] : table.Ids.Select(id => (id, table.Str(id, nameField))).Where(e => e.Item2.Length > 0).ToList();

    static IReadOnlyList<(uint Id, string Name)> SearchTable(DbcTable? table, int nameField, string term, int limit)
    {
        if (table == null)
        {
            return [];
        }

        term = term.Trim();
        uint.TryParse(term, out uint exact);
        return table.Ids
            .Select(id => (Id: id, Name: table.Str(id, nameField)))
            .Where(e => e.Name.Length > 0 && (term.Length == 0 || e.Id == exact || e.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => e.Id == exact ? 0 : 1).ThenBy(e => e.Name.Length).ThenBy(e => e.Id)
            .Take(limit).ToList();
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
