using ACWorldEdit.Foundation;
using DBCD;

namespace ACWorldEdit.Assets;

/// <summary>An AreaTrigger.db2 row: where the trigger is and how its volume is shaped.</summary>
public sealed record ClientAreaTrigger(uint Id, uint Map, float X, float Y, float Z, float Radius, float BoxLength, float BoxWidth, float BoxHeight, float BoxYaw, byte ShapeType, uint PhaseId, uint PhaseGroupId, uint PhaseUseFlags);

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

    /// <summary>Broadcast text for an ID, or null when the client has none. Text1 is the female form.</summary>
    string? BroadcastText(uint id, bool female = false);

    IReadOnlyList<(uint Id, string Text)> SearchBroadcastText(string term, int limit);

    ClientItem? Item(uint id);

    IReadOnlyList<ClientItem> SearchItems(string term, int limit);

    string? SpellName(uint id);

    IReadOnlyList<(uint Id, string Name)> SearchSpells(string term, int limit);
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
            list.Add(new((uint)row.ID, row.FieldAs<uint>("ContinentID"), position[0], position[1], position[2], row.FieldAs<float>("Radius"), row.FieldAs<float>("Box_length"), row.FieldAs<float>("Box_width"), row.FieldAs<float>("Box_height"), row.FieldAs<float>("Box_yaw"), row.FieldAs<byte>("ShapeType"), row.FieldAs<uint>("PhaseID"), row.FieldAs<uint>("PhaseGroupID"), row.FieldAs<uint>("PhaseUseFlags")));
        }

        return list;
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
