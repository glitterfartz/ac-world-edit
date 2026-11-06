using ACWorldEdit.Foundation;
using DBCD;

namespace ACWorldEdit.Assets;

/// <summary>
/// An AreaTrigger.db2 row: where the trigger is and how its volume is shaped. ShapeId names the shape's
/// own row; for a polygon (ShapeType 3) it is the Path.db2 ID of its corners.
/// </summary>
public sealed record ClientAreaTrigger(uint Id, uint Map, float X, float Y, float Z, float Radius, float BoxLength, float BoxWidth, float BoxHeight, float BoxYaw, byte ShapeType, uint PhaseId, uint PhaseGroupId, uint PhaseUseFlags, uint ShapeId = 0);

/// <summary>
/// A polygon AreaTrigger's corners as offsets from the trigger position, and the height above it the
/// volume ends at (null when the path sets none), built as ObjectMgr::LoadAreaTriggerPolygons does.
/// Ground is the corners' mean height, also an offset from the trigger position: the server ignores
/// it, but many polygons sit at the map origin with world-space corners, so it says where the volume
/// really is.
/// </summary>
public sealed record ClientPolygon(IReadOnlyList<(float X, float Y)> Vertices, float? Height, float Ground);

/// <summary>An item as the client names it (ItemSparse + Item).</summary>
public sealed record ClientItem(uint Id, string Name, uint Quality, uint IconFileId);

/// <summary>A currency the client's CurrencyTypes table names (honor, valor, a raid token…).</summary>
public sealed record ClientCurrency(uint Id, string Name, string Description, uint IconFileId, uint Quality);

/// <summary>A profession or secondary skill from SkillLine.</summary>
public sealed record ClientSkillLine(uint Id, string Name, string Description, uint IconFileId);

/// <summary>A reputation faction the client names.</summary>
public sealed record ClientFaction(uint Id, string Name, string Description);

/// <summary>A mail template the client names by its subject or, in current retail, by the first body line.</summary>
public sealed record ClientMailTemplate(uint Id, string Name);

/// <summary>
/// Reference data the retail world database does not hold, served from the client's DB2 tables:
/// area trigger volumes, broadcast text, item names, spell names and the tables behind TrinityCore's
/// creature scaling (<see cref="ITcStatData"/>). Null for Wrath, whose database carries these itself.
/// Calls are safe from any thread; tables load on first use.
/// </summary>
public interface IClientLookup : ITcStatData
{
    IReadOnlyList<ClientAreaTrigger> AreaTriggers();

    /// <summary>The corners of a polygon trigger's path (Path.db2 ID), or null when the path has fewer than four nodes.</summary>
    ClientPolygon? Polygon(uint pathId);

    /// <summary>Broadcast text for an ID, or null when the client has none. Text1 is the female form.</summary>
    string? BroadcastText(uint id, bool female = false);

    /// <summary>The highest ID in the client's BroadcastText table, so a new text can be numbered above it.</summary>
    uint MaxBroadcastTextId();

    IReadOnlyList<(uint Id, string Text)> SearchBroadcastText(string term, int limit);

    ClientItem? Item(uint id);

    IReadOnlyList<ClientItem> SearchItems(string term, int limit);

    /// <summary>
    /// The item as the client and the server's formulas describe it: stored fields from ItemSparse and Item,
    /// and the stat values, armor, weapon damage, durability and squished item level computed by
    /// <see cref="RetailItemStats"/>. Null for an unknown item. Name and quality are the client's; the
    /// hotfixes database can override them.
    /// </summary>
    ItemDetails? ItemDetails(uint id);

    string? SpellName(uint id);

    IReadOnlyList<(uint Id, string Name)> SearchSpells(string term, int limit);

    /// <summary>The FileDataID of a spell's icon, or 0 when the client has none.</summary>
    uint SpellIcon(uint id) => 0;

    /// <summary>Reference rows the quest editor can name and pick. Defaults keep test doubles compiling.</summary>
    ClientCurrency? Currency(uint id) => null;
    IReadOnlyList<ClientCurrency> SearchCurrencies(string term, int limit) => [];

    ClientSkillLine? SkillLine(uint id) => null;
    IReadOnlyList<ClientSkillLine> SearchSkillLines(string term, int limit) => [];

    ClientFaction? Faction(uint id) => null;
    IReadOnlyList<ClientFaction> SearchFactions(string term, int limit) => [];

    /// <summary>A creature's faction template (creature_template.faction), named by the faction it belongs to.</summary>
    ClientFaction? FactionTemplate(uint id) => null;
    IReadOnlyList<ClientFaction> SearchFactionTemplates(string term, int limit) => [];

    /// <summary>Playable classes, for the unit class and trainer class dropdowns.</summary>
    IReadOnlyList<(uint Id, string Name)> Classes() => [];

    /// <summary>Creature types (beast, dragonkin, demon…), for the type dropdown.</summary>
    IReadOnlyList<(uint Id, string Name)> CreatureTypes() => [];

    /// <summary>Hunter pet families, for the pet family dropdown.</summary>
    IReadOnlyList<(uint Id, string Name)> CreatureFamilies() => [];

    ClientMailTemplate? MailTemplate(uint id) => null;
    IReadOnlyList<ClientMailTemplate> SearchMailTemplates(string term, int limit) => [];

    /// <summary>The client's animation emotes (Emotes.db2), used by quest gesture pickers.</summary>
    IReadOnlyList<(uint Id, string Name)> Emotes() => [];

    /// <summary>Playable races as (ChrRaces ID, display name); empty when the client has no table.</summary>
    IReadOnlyList<(uint Id, string Name)> Races() => [];

    /// <summary>Quest categories and quest-log sorts, for the quest kind and zone pickers.</summary>
    IReadOnlyList<(uint Id, string Name)> QuestInfos() => [];
    IReadOnlyList<(uint Id, string Name)> QuestSorts() => [];

    /// <summary>Character titles, for the quest reward title picker.</summary>
    IReadOnlyList<(uint Id, string Name)> Titles() => [];
    IReadOnlyList<(uint Id, string Name)> SearchTitles(string term, int limit) => [];

    /// <summary>Whether the client's table for <paramref name="kind"/> has a row with this ID.</summary>
    bool Exists(ClientRecord kind, uint id);
}

/// <summary>
/// <see cref="IClientLookup"/> over <see cref="Db2Tables"/>. The tables object is not thread-safe, so
/// every access to it is serialised here; the rows themselves are read-only once loaded.
/// </summary>
public sealed class RetailClientLookup(Db2Tables tables, Func<uint, bool>? fileExists = null) : IClientLookup
{
    readonly object gate = new();

    IDBCDStorage Table(string name)
    {
        lock (gate)
        {
            return tables.Table(name);
        }
    }

    IReadOnlyList<ClientAreaTrigger>? areaTriggers;

    /// <summary>Built once: the overlay asks again every time the camera crosses a query cell.</summary>
    public IReadOnlyList<ClientAreaTrigger> AreaTriggers()
    {
        lock (gate)
        {
            return areaTriggers ??= ReadAreaTriggers();
        }
    }

    List<ClientAreaTrigger> ReadAreaTriggers()
    {
        var list = new List<ClientAreaTrigger>();
        foreach (var row in tables.Table("AreaTrigger").Values)
        {
            float[] position = row.FieldAs<float[]>("Pos");
            list.Add(new((uint)row.ID, row.FieldAs<uint>("ContinentID"), position[0], position[1], position[2], row.FieldAs<float>("Radius"), row.FieldAs<float>("Box_length"), row.FieldAs<float>("Box_width"), row.FieldAs<float>("Box_height"), row.FieldAs<float>("Box_yaw"), row.FieldAs<byte>("ShapeType"), row.FieldAs<uint>("PhaseID"), row.FieldAs<uint>("PhaseGroupID"), row.FieldAs<uint>("PhaseUseFlags"), row.FieldAs<uint>("ShapeID")));
        }

        return list;
    }

    Dictionary<uint, ClientPolygon>? polygons;

    public ClientPolygon? Polygon(uint pathId)
    {
        lock (gate)
        {
            polygons ??= ReadPolygons();
            return polygons.GetValueOrDefault(pathId);
        }
    }

    /// <summary>PathProperty index 5 (PathPropertyIndex::VolumeHeight), stored in thousandths of a yard.</summary>
    const int VolumeHeight = 5;

    /// <summary>
    /// Every path as DB2Manager builds it: nodes of a known path and location, ordered by Sequence. A
    /// polygon drops its first location (the path origin) and needs four or more; its height is the
    /// VolumeHeight property / 1000 plus the server's 0.02 yd margin.
    /// </summary>
    Dictionary<uint, ClientPolygon> ReadPolygons()
    {
        var paths = tables.Table("Path");
        var locations = tables.Table("Location");
        var nodes = new Dictionary<uint, List<(int Sequence, float X, float Y, float Z)>>();
        foreach (var node in tables.Table("PathNode").Values)
        {
            int path = node.FieldAs<int>("PathID"), location = node.FieldAs<int>("LocationID");
            if (!paths.ContainsKey(path) || !locations.TryGetValue(location, out var place))
            {
                continue;
            }

            float[] position = place.FieldAs<float[]>("Pos");
            if (!nodes.TryGetValue((uint)path, out var list))
            {
                nodes[(uint)path] = list = [];
            }

            list.Add((node.FieldAs<int>("Sequence"), position[0], position[1], position[2]));
        }

        var heights = new Dictionary<uint, float>();
        foreach (var property in tables.Table("PathProperty").Values)
        {
            if (property.FieldAs<byte>("PropertyIndex") == VolumeHeight && paths.ContainsKey(property.FieldAs<int>("PathID")))
            {
                heights[(uint)property.FieldAs<int>("PathID")] = property.FieldAs<int>("Value") * 0.001f + 0.02f;
            }
        }

        var result = new Dictionary<uint, ClientPolygon>();
        foreach (var (path, list) in nodes.Where(n => n.Value.Count >= 4))
        {
            var corners = list.OrderBy(n => n.Sequence).Skip(1).ToArray();
            result[path] = new(corners.Select(n => (n.X, n.Y)).ToArray(), heights.TryGetValue(path, out float height) ? height : null, corners.Average(n => n.Z));
        }

        return result;
    }

    public string? BroadcastText(uint id, bool female = false)
    {
        if (!Table("BroadcastText").TryGetValue((int)id, out var row))
        {
            return null;
        }

        string text = row.Field<string>(female ? "Text1_lang" : "Text_lang") ?? "";
        if (text.Length == 0)
        {
            text = row.Field<string>(female ? "Text_lang" : "Text1_lang") ?? "";
        }

        return text.Length == 0 ? null : text;
    }

    public uint MaxBroadcastTextId() => (uint)Table("BroadcastText").Keys.DefaultIfEmpty(0).Max();

    public IReadOnlyList<(uint Id, string Text)> SearchBroadcastText(string term, int limit)
    {
        var found = new List<(uint, string)>();
        foreach (var row in Table("BroadcastText").Values)
        {
            string text = row.Field<string>("Text_lang") ?? "";
            if (text.Length == 0)
            {
                text = row.Field<string>("Text1_lang") ?? "";
            }

            if (Matches(row.ID, text, term))
            {
                found.Add(((uint)row.ID, text));
                if (found.Count >= limit)
                {
                    break;
                }
            }
        }

        return found;
    }

    public ClientItem? Item(uint id) =>
        Table("ItemSparse").TryGetValue((int)id, out var sparse) ? Describe(sparse) : null;

    public IReadOnlyList<ClientItem> SearchItems(string term, int limit)
    {
        var found = new List<ClientItem>();
        foreach (var sparse in Table("ItemSparse").Values)
        {
            if (Matches(sparse.ID, sparse.Field<string>("Display_lang") ?? "", term))
            {
                found.Add(Describe(sparse));
                if (found.Count >= limit)
                {
                    break;
                }
            }
        }

        return found;
    }

    RetailItemTables? itemTables;

    /// <summary>The build as the server compares patches: major * 10000 + minor * 100 + bugfix (12.1.5.70448 is 120105).</summary>
    int ClientPatch
    {
        get
        {
            string[] part = tables.Build.Split('.');
            return part.Length >= 3 && int.TryParse(part[0], out int major) && int.TryParse(part[1], out int minor) && int.TryParse(part[2], out int fix) ? major * 10000 + minor * 100 + fix : 0;
        }
    }

    public ItemDetails? ItemDetails(uint id)
    {
        if (!Table("ItemSparse").TryGetValue((int)id, out var sparse))
        {
            return null;
        }

        var item = Table("Item").TryGetValue((int)id, out var row) ? row : null;
        var scaling = itemTables ??= new(Table);
        uint itemClass = item?.FieldAs<uint>("ClassID") ?? 0, subclass = item?.FieldAs<uint>("SubclassID") ?? 0;
        uint inventory = sparse.FieldAs<uint>("InventoryType"), quality = sparse.FieldAs<uint>("OverallQualityID"), baseLevel = sparse.FieldAs<uint>("ItemLevel");
        int[] flags = sparse.FieldAs<int[]>("Flags");
        uint level = RetailItemStats.EffectiveItemLevel(baseLevel, sparse.FieldAs<uint>("ItemSquishEraID"), scaling, ClientPatch);

        int[] types = sparse.FieldAs<int[]>("StatModifier_bonusStat"), percent = sparse.FieldAs<int[]>("StatPercentEditor");
        var stats = new List<(uint Type, int Value)>();
        for (int i = 0; i < types.Length; i++)
        {
            // Corruption is a flat amount; every other stat is its share of the slot's budget.
            if (types[i] >= 0)
            {
                stats.Add(((uint)types[i], types[i] is 22 or 23 ? percent[i] : RetailItemStats.StatValue(percent[i], level, quality, inventory, subclass, scaling)));
            }
        }

        uint delay = sparse.FieldAs<uint>("ItemDelay");
        var (min, max) = RetailItemStats.Damage(RetailItemStats.Dps(itemClass, subclass, inventory, quality, level, flags.Length > 1 ? flags[1] : 0, scaling), delay, sparse.FieldAs<float>("DmgVariance"));
        var info = new ItemInfo(id, sparse.Field<string>("Display_lang") ?? "", (int)quality, sparse.FieldAs<long>("BuyPrice"), 0);
        return new ItemDetails(info, itemClass, subclass, inventory, sparse.FieldAs<uint>("Bonding"), flags.Length > 0 ? (uint)flags[0] : 0, level,
            sparse.FieldAs<uint>("RequiredLevel"), sparse.FieldAs<uint>("MaxCount"), sparse.FieldAs<int>("AllowableClass"), sparse.FieldAs<int[]>("AllowableRaces")[0],
            sparse.FieldAs<uint>("ContainerSlots"), stats.ToArray(), max > 0 ? [(min, max, sparse.FieldAs<uint>("DamageType"))] : [], delay,
            RetailItemStats.Armor(itemClass, subclass, inventory, quality, level, scaling), 0, new int[6],
            RetailItemStats.MaxDurability(itemClass, subclass, inventory, quality, baseLevel), [], sparse.Field<string>("Description_lang") ?? "", sparse.FieldAs<long>("SellPrice"));
    }

    ClientItem Describe(DBCDRow sparse)
    {
        uint icon = Table("Item").TryGetValue(sparse.ID, out var item) ? item.FieldAs<uint>("IconFileDataID") : 0;
        return new((uint)sparse.ID, sparse.Field<string>("Display_lang") ?? "", sparse.FieldAs<uint>("OverallQualityID"), icon);
    }

    public string? SpellName(uint id) =>
        Table("SpellName").TryGetValue((int)id, out var row) && row.Field<string>("Name_lang") is { Length: > 0 } name ? name : null;

    public IReadOnlyList<(uint Id, string Name)> SearchSpells(string term, int limit)
    {
        var found = new List<(uint, string)>();
        foreach (var row in Table("SpellName").Values)
        {
            string name = row.Field<string>("Name_lang") ?? "";
            if (Matches(row.ID, name, term))
            {
                found.Add(((uint)row.ID, name));
                if (found.Count >= limit)
                {
                    break;
                }
            }
        }

        return found;
    }

    Dictionary<uint, uint>? spellIcons;

    public uint SpellIcon(uint id)
    {
        lock (gate)
        {
            if (spellIcons == null)
            {
                spellIcons = [];
                foreach (var row in tables.Table("SpellMisc").Values)
                {
                    uint spell = row.FieldAs<uint>("SpellID"), icon = row.FieldAs<uint>("SpellIconFileDataID");
                    if (spell != 0 && icon != 0 && !spellIcons.ContainsKey(spell))
                    {
                        spellIcons[spell] = icon;
                    }
                }
            }

            return spellIcons.GetValueOrDefault(id);
        }
    }

    public ClientCurrency? Currency(uint id) =>
        Table("CurrencyTypes").TryGetValue((int)id, out var row) ? DescribeCurrency(row) : null;

    public IReadOnlyList<ClientCurrency> SearchCurrencies(string term, int limit) => Search("CurrencyTypes", "Name_lang", term, limit, DescribeCurrency);

    static ClientCurrency DescribeCurrency(DBCDRow row) =>
        new((uint)row.ID, row.Field<string>("Name_lang") ?? "", row.Field<string>("Description_lang") ?? "", row.FieldAs<uint>("InventoryIconFileID"), row.FieldAs<uint>("Quality"));

    public ClientSkillLine? SkillLine(uint id) =>
        Table("SkillLine").TryGetValue((int)id, out var row) ? DescribeSkill(row) : null;

    public IReadOnlyList<ClientSkillLine> SearchSkillLines(string term, int limit) => Search("SkillLine", "DisplayName_lang", term, limit, DescribeSkill);

    static ClientSkillLine DescribeSkill(DBCDRow row) =>
        new((uint)row.ID, row.Field<string>("DisplayName_lang") ?? "", row.Field<string>("Description_lang") ?? "", row.FieldAs<uint>("SpellIconFileID"));

    public ClientFaction? Faction(uint id) =>
        Table("Faction").TryGetValue((int)id, out var row) ? DescribeFaction(row) : null;

    public IReadOnlyList<ClientFaction> SearchFactions(string term, int limit) => Search("Faction", "Name_lang", term, limit, DescribeFaction);

    static ClientFaction DescribeFaction(DBCDRow row) =>
        new((uint)row.ID, row.Field<string>("Name_lang") ?? "", row.Field<string>("Description_lang") ?? "");

    public ClientFaction? FactionTemplate(uint id) =>
        Table("FactionTemplate").TryGetValue((int)id, out var row) ? DescribeFactionTemplate(row) : null;

    public IReadOnlyList<ClientFaction> SearchFactionTemplates(string term, int limit)
    {
        term = term.Trim();
        uint.TryParse(term, out uint exact);
        var found = new List<ClientFaction>();
        foreach (var row in Table("FactionTemplate").Values)
        {
            var template = DescribeFactionTemplate(row);
            if (term.Length == 0 || row.ID == exact || template.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(template);
                if (found.Count >= limit)
                {
                    break;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// creature_template.faction points at FactionTemplate, which itself points at a reputation
    /// faction; show the faction's name and keep both IDs in the description.
    /// </summary>
    ClientFaction DescribeFactionTemplate(DBCDRow row)
    {
        uint factionId = row.FieldAs<uint>("Faction");
        var faction = Table("Faction").TryGetValue((int)factionId, out var named) ? named : null;
        string name = faction?.Field<string>("Name_lang") ?? "";
        return new((uint)row.ID, name.Length > 0 ? name : $"Faction {factionId}",
            $"Faction template {row.ID} · faction {factionId}");
    }

    public IReadOnlyList<(uint Id, string Name)> Classes() => All("ChrClasses", "Name_lang");

    public IReadOnlyList<(uint Id, string Name)> CreatureTypes() => All("CreatureType", "Name_lang");

    public IReadOnlyList<(uint Id, string Name)> CreatureFamilies() => All("CreatureFamily", "Name_lang");

    public ClientMailTemplate? MailTemplate(uint id) =>
        Table("MailTemplate").TryGetValue((int)id, out var row) ? DescribeMail(row) : null;

    public IReadOnlyList<ClientMailTemplate> SearchMailTemplates(string term, int limit) => Search("MailTemplate", "Body_lang", term, limit, DescribeMail);

    /// <summary>
    /// The current retail MailTemplate.db2 has no subject column, so use the first line of the body
    /// as the readable name (the editor asks for a name or at least what the mail contains).
    /// </summary>
    static ClientMailTemplate DescribeMail(DBCDRow row)
    {
        string body = row.Field<string>("Body_lang") ?? "";
        string first = body.ReplaceLineEndings("\n").Split('\n', 2)[0].Trim();
        return new((uint)row.ID, first.Length <= 90 ? first : first[..89] + "…");
    }

    /// <summary>
    /// Retail Emotes.db2 names the slash command (for example "/wave"), not the Wrath "Name" field;
    /// show it without the slash so it reads like the DBC names.
    /// </summary>
    public IReadOnlyList<(uint Id, string Name)> Emotes()
    {
        var list = new List<(uint, string)>();
        foreach (var row in Table("Emotes").Values)
        {
            string name = (row.Field<string>("EmoteSlashCommand") ?? "").TrimStart('/').Replace('_', ' ').Trim();
            if (name.Length > 0)
            {
                list.Add(((uint)row.ID, name));
            }
        }

        return list;
    }

    public IReadOnlyList<(uint Id, string Name)> Races() => All("ChrRaces", "Name_lang");

    public IReadOnlyList<(uint Id, string Name)> QuestInfos() => All("QuestInfo", "InfoName_lang");

    public IReadOnlyList<(uint Id, string Name)> QuestSorts() => All("QuestSort", "SortName_lang");

    public IReadOnlyList<(uint Id, string Name)> Titles() => All("CharTitles", "Name_lang");

    public IReadOnlyList<(uint Id, string Name)> SearchTitles(string term, int limit) =>
        Search("CharTitles", "Name_lang", term, limit, row => ((uint)row.ID, row.Field<string>("Name_lang") ?? ""));

    /// <summary>Every row of a table as (ID, name), skipping rows without a name.</summary>
    IReadOnlyList<(uint Id, string Name)> All(string table, string nameField)
    {
        var list = new List<(uint, string)>();
        foreach (var row in Table(table).Values)
        {
            string name = row.Field<string>(nameField) ?? "";
            if (name.Length > 0)
            {
                list.Add(((uint)row.ID, name));
            }
        }

        return list;
    }

    /// <summary>Bounded name/ID search over a DB2 table; used by the editor pickers.</summary>
    IReadOnlyList<T> Search<T>(string table, string nameField, string term, int limit, Func<DBCDRow, T> describe)
    {
        term = term.Trim();
        uint.TryParse(term, out uint exact);
        var found = new List<T>();
        foreach (var row in Table(table).Values)
        {
            string name = row.Field<string>(nameField) ?? "";
            if (term.Length == 0 || row.ID == exact || name.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(describe(row));
                if (found.Count >= limit)
                {
                    break;
                }
            }
        }

        return found;
    }

    public bool Exists(ClientRecord kind, uint id) => kind switch
    {
        ClientRecord.FileData => fileExists?.Invoke(id) ?? true,
        ClientRecord.Item => Item(id) != null,
        ClientRecord.Spell => SpellName(id) != null,
        _ => Table(kind switch
        {
            ClientRecord.Currency => "CurrencyTypes",
            ClientRecord.ExtendedCost => "ItemExtendedCost",
            ClientRecord.PlayerCondition => "PlayerCondition",
            ClientRecord.BonusList => "ItemBonusList",
            ClientRecord.SkillLine => "SkillLine",
            ClientRecord.Language => "Languages",
            ClientRecord.GossipNpcOption => "GossipNPCOption",
            _ => "Difficulty"
        }).ContainsKey((int)id)
    };

    Dictionary<(uint Level, int Expansion), TcExpectedStat>? expectedStats;
    Dictionary<uint, List<TcContentTuningMod>>? tuningMods;

    public TcContentTuning? ContentTuning(uint id) =>
        Table("ContentTuning").TryGetValue((int)id, out var row)
            ? new(id, row.FieldAs<int>("Flags"), row.FieldAs<int>("MinLevelSquish"), row.FieldAs<int>("MaxLevelSquish"), row.FieldAs<int>("MinLevelScalingOffset"),
                row.FieldAs<int>("MaxLevelScalingOffset"), row.FieldAs<int>("AllowedMinOffset"), row.FieldAs<int>("AllowedMaxOffset"))
            : null;

    public TcExpectedStat? ExpectedStat(uint level, int expansion)
    {
        lock (gate)
        {
            if (expectedStats == null)
            {
                expectedStats = [];
                foreach (var row in tables.Table("ExpectedStat").Values)
                {
                    expectedStats[(row.FieldAs<uint>("Lvl"), row.FieldAs<int>("ExpansionID"))] =
                        new(row.FieldAs<float>("CreatureHealth"), row.FieldAs<float>("CreatureAutoAttackDps"), row.FieldAs<float>("CreatureArmor"));
                }
            }

            return expectedStats.GetValueOrDefault((level, expansion));
        }
    }

    public TcExpectedStatMod? ExpectedStatMod(uint id) => Table("ExpectedStatMod").TryGetValue((int)id, out var row) ? StatMod(row) : null;

    static TcExpectedStatMod StatMod(DBCDRow row) => new(row.FieldAs<float>("CreatureHealthMod"), row.FieldAs<float>("CreatureAutoAttackDPSMod"), row.FieldAs<float>("CreatureArmorMod"));

    public IReadOnlyList<TcContentTuningMod> ContentTuningMods(uint contentTuningId)
    {
        lock (gate)
        {
            if (tuningMods == null)
            {
                tuningMods = [];
                var statMods = tables.Table("ExpectedStatMod");
                foreach (var row in tables.Table("ContentTuningXExpected").Values)
                {
                    // TrinityCore only indexes the rows whose ExpectedStatMod exists.
                    if (!statMods.TryGetValue(row.FieldAs<int>("ExpectedStatModID"), out var mod))
                    {
                        continue;
                    }

                    uint tuning = row.FieldAs<uint>("ContentTuningID");
                    if (!tuningMods.TryGetValue(tuning, out var list))
                    {
                        tuningMods[tuning] = list = [];
                    }

                    list.Add(new(row.FieldAs<int>("MinMythicPlusSeasonID"), row.FieldAs<int>("MaxMythicPlusSeasonID"), StatMod(mod)));
                }
            }

            return tuningMods.TryGetValue(contentTuningId, out var found) ? found : [];
        }
    }

    public int? MilestoneSeason(int mythicPlusSeasonId) =>
        Table("MythicPlusSeason").TryGetValue(mythicPlusSeasonId, out var row) ? row.FieldAs<int>("MilestoneSeason") : null;

    Dictionary<(uint Map, int Difficulty), uint>? mapTuning;

    public uint MapDifficultyTuning(uint mapId, int difficultyId)
    {
        lock (gate)
        {
            if (mapTuning == null)
            {
                mapTuning = [];
                foreach (var row in tables.Table("MapDifficulty").Values)
                {
                    mapTuning[(row.FieldAs<uint>("MapID"), row.FieldAs<int>("DifficultyID"))] = row.FieldAs<uint>("ContentTuningID");
                }
            }

            return mapTuning.GetValueOrDefault((mapId, difficultyId));
        }
    }

    public uint AreaTuning(uint areaId)
    {
        var areas = Table("AreaTable");
        // The parent chain is short; the bound only guards a cyclic row.
        for (int depth = 0; depth < 16 && areas.TryGetValue((int)areaId, out var area); depth++)
        {
            if (area.FieldAs<uint>("ContentTuningID") is var tuning and not 0)
            {
                return tuning;
            }

            areaId = area.FieldAs<uint>("ParentAreaID");
        }

        return 0;
    }

    /// <summary>A numeric term matches the ID exactly; otherwise the text must contain the term.</summary>
    static bool Matches(int id, string text, string term) =>
        uint.TryParse(term, out uint number) ? (uint)id == number : text.Contains(term, StringComparison.OrdinalIgnoreCase);
}
