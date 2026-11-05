namespace ACWorldEdit.Foundation;

/// <summary>
/// Which emulator's world database schema is read and exported to. The dialect follows the client
/// kind: Wrath 3.3.5 uses AzerothCore, retail uses TrinityCore.
/// </summary>
public enum WorldDialect
{
    AzerothCore,
    TrinityCore
}

/// <summary>The editable tables of one dialect.</summary>
public interface ITableCatalog
{
    IReadOnlyList<TableSchema> All { get; }

    /// <summary>The table with this draft name; throws <see cref="InvalidDataException"/> when it is not editable.</summary>
    TableSchema Find(string name);
}
