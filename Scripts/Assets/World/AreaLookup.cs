namespace ACWorldEdit.Assets;

/// <summary>
/// Client AreaTable.dbc: resolves an MCNK AreaId to its zone name and, for a subzone, its parent
/// zone name (e.g. a city district's parent is the city). Schema/field offsets were confirmed
/// against real client rows (Elwynn Forest=12, Stormwind City=1519, Durotar=14, Orgrimmar=1637).
/// The archive-loading half (TryLoad) lives in the viewer's partial because it logs through Godot.
/// </summary>
public sealed partial class AreaLookup
{
    readonly DbcTable table;
    AreaLookup(DbcTable table) => this.table = table;

    /// <summary>
    /// Wrap an already parsed AreaTable (tests build synthetic tables).
    /// </summary>
    public static AreaLookup FromTable(DbcTable table) => new(table);

    /// <summary>
    /// True when the ID is an AreaTable row; graveyard_zone.GhostZone must be one (the server
    /// skips links to unknown areas).
    /// </summary>
    public bool Contains(uint areaId) => table.Contains(areaId);

    /// <summary>
    /// Map (ContinentID, field 1) an area belongs to, or null for unknown IDs.
    /// </summary>
    public uint? MapOf(uint areaId) => table.Contains(areaId) ? table.U(areaId, 1) : null;

    /// <summary>
    /// Areas whose name contains the term (or whose ID equals it), preferring the given map, then
    /// zones (no parent) before subzones. Bounded for list display.
    /// </summary>
    public IReadOnlyList<(uint Id, string Name, string? Parent, uint Map)> Search(string term, uint? preferredMap, int limit = 30)
    {
        term = term.Trim();
        uint.TryParse(term, out uint exact);
        var matches = new List<(uint Id, string Name, string? Parent, uint Map)>();
        foreach (uint id in table.Ids)
        {
            var (name, parent) = Describe(id);
            if (term.Length == 0 || id == exact || name.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add((id, name, parent, table.U(id, 1)));
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

    /// <summary>
    /// Resolve a zone name and, when the area is a named subzone, its parent zone name. Unknown IDs
    /// report a numeric placeholder rather than failing closed, since this is a display label only.
    /// </summary>
    public (string Zone, string? Parent) Describe(uint areaId)
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
}
