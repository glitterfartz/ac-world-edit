using ACWorldEdit.Foundation;

namespace ACWorldEdit.Data;

/// <summary>
/// Everything needed to reach one world database: the connection, the optional hotfixes database
/// name (TrinityCore only) and the schema dialect, which follows the client kind.
/// </summary>
public sealed record WorldConnection(DatabaseSettings World, string? Hotfixes, WorldDialect Dialect)
{
    public static readonly WorldConnection Empty = new(DatabaseSettings.Empty, null, WorldDialect.AzerothCore);
}
