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

    IReadOnlyList<(uint Id, string Text)> SearchBroadcastText(string term, int limit);

    ClientItem? Item(uint id);

    IReadOnlyList<ClientItem> SearchItems(string term, int limit);

    string? SpellName(uint id);

    IReadOnlyList<(uint Id, string Name)> SearchSpells(string term, int limit);

    /// <summary>Whether the client's table for <paramref name="kind"/> has a row with this ID.</summary>
    bool Exists(ClientRecord kind, uint id);
}

/// <summary>
/// <see cref="IClientLookup"/> over <see cref="Db2Tables"/>. The tables object is not thread-safe, so
/// every access to it is serialised here; the rows themselves are read-only once loaded.
/// </summary>
public sealed class RetailClientLookup(Db2Tables tables) : IClientLookup
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

    public bool Exists(ClientRecord kind, uint id) => kind switch
    {
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

    /// <summary>A numeric term matches the ID exactly; otherwise the text must contain the term.</summary>
    static bool Matches(int id, string text, string term) =>
        uint.TryParse(term, out uint number) ? (uint)id == number : text.Contains(term, StringComparison.OrdinalIgnoreCase);
}
