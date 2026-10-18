using Godot;

namespace ACWorldEdit.Assets;

/// <summary>
/// Archive loading for AreaLookup (the lookup logic is in Scripts/Assets/World/AreaLookup.cs).
/// A missing or malformed table disables zone names rather than failing the viewer.
/// </summary>
public sealed partial class AreaLookup
{
    public static AreaLookup? TryLoad(IClientFiles store)
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
}
