using Godot;

namespace ACWorldEdit.Assets;

/// <summary>
/// Client AreaTable.dbc: resolves an MCNK AreaId to its zone name and, for a subzone, its parent
/// zone name (e.g. a city district's parent is the city). Schema/field offsets were confirmed
/// against real client rows (Elwynn Forest=12, Stormwind City=1519, Durotar=14, Orgrimmar=1637).
/// </summary>
public sealed class AreaLookup
{
    readonly DbcTable table;
    AreaLookup(DbcTable table) => this.table = table;

    public static AreaLookup? TryLoad(AssetStore store)
    {
        try
        {
            return new(new DbcTable(store.Read(@"DBFilesClient/AreaTable.dbc")));
        }
        catch (Exception e)
        {
            GD.PushWarning("AreaTable unavailable: " + e.Message);
            return null;
        }
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
