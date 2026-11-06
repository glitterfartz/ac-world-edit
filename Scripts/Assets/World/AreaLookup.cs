namespace ACWorldEdit.Assets;

/// <summary>
/// Client area names: AreaTable.dbc for Wrath and the retail AreaTable.db2 for TrinityCore. Resolves
/// an area to its zone name and, for a subzone, its parent zone name (e.g. a city district's parent
/// is the city). The archive-loading half (TryLoadWrath) lives in the viewer's partial because it
/// logs through Godot.
/// </summary>
public sealed partial class AreaLookup
{
    readonly DbcTable? table;
    readonly Db2Tables? retail;

    AreaLookup(DbcTable table) => this.table = table;

    AreaLookup(Db2Tables tables) => retail = tables;

    /// <summary>
    /// Wrap an already parsed AreaTable (tests build synthetic tables).
    /// </summary>
    public static AreaLookup FromTable(DbcTable table) => new(table);

    /// <summary>Read area names from the retail client's AreaTable.db2.</summary>
    public static AreaLookup FromRetail(Db2Tables tables) => new(tables);

    /// <summary>
    /// True when the ID is an AreaTable row; graveyard_zone.GhostZone must be one (the server
    /// skips links to unknown areas).
    /// </summary>
    public bool Contains(uint areaId) => table?.Contains(areaId) ?? retail?.Table("AreaTable").ContainsKey((int)areaId) == true;

    /// <summary>
    /// Map (ContinentID) an area belongs to, or null for unknown IDs.
    /// </summary>
    public uint? MapOf(uint areaId) => table != null
        ? table.Contains(areaId) ? table.U(areaId, 1) : null
        : retail?.Table("AreaTable").TryGetValue((int)areaId, out var row) == true ? row.FieldAs<uint>("ContinentID") : null;

    /// <summary>
    /// Areas whose name contains the term (or whose ID equals it), preferring the given map, then
    /// zones (no parent) before subzones. Bounded for list display.
    /// </summary>
    public IReadOnlyList<(uint Id, string Name, string? Parent, uint Map)> Search(string term, uint? preferredMap, int limit = 30)
    {
        term = term.Trim();
        uint.TryParse(term, out uint exact);
        var matches = new List<(uint Id, string Name, string? Parent, uint Map)>();
        foreach (uint id in Ids())
        {
            var (name, parent) = Describe(id);
            if (term.Length == 0 || id == exact || name.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add((id, name, parent, MapOf(id) ?? 0));
            }
        }

        return matches
            .OrderBy(m => m.Id == exact && exact != 0 ? 0 : 1)
            .ThenBy(m => preferredMap.HasValue && m.Map == preferredMap.Value ? 0 : 1)
            .ThenBy(m => m.Parent == null ? 0 : 1)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    IEnumerable<uint> Ids() => table?.Ids ?? retail?.Table("AreaTable").Keys.Select(id => (uint)id) ?? [];

    /// <summary>
    /// Resolve a zone name and, when the area is a named subzone, its parent zone name. Unknown IDs
    /// report a numeric placeholder rather than failing closed, since this is a display label only.
    /// </summary>
    public (string Zone, string? Parent) Describe(uint areaId)
    {
        if (table != null)
        {
            if (!table.Contains(areaId))
            {
                return ($"Area {areaId}", null);
            }

            string name = table.Str(areaId, 11);
            uint parentId = table.U(areaId, 2);
            string? parent = parentId != 0 && table.Contains(parentId) ? table.Str(parentId, 11) : null;
            return (name.Length == 0 ? $"Area {areaId}" : name, parent is { Length: 0 } ? null : parent);
        }

        var areas = retail?.Table("AreaTable");
        if (areas == null || !areas.TryGetValue((int)areaId, out var row))
        {
            return ($"Area {areaId}", null);
        }

        string areaName = row.Field<string>("AreaName_lang") ?? "";
        uint parentArea = row.FieldAs<uint>("ParentAreaID");
        string? parentName = parentArea != 0 && areas.TryGetValue((int)parentArea, out var parentRow)
            ? parentRow.Field<string>("AreaName_lang")
            : null;
        return (areaName.Length == 0 ? $"Area {areaId}" : areaName, parentName is { Length: 0 } ? null : parentName);
    }
}
