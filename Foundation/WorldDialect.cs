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

/// <summary>
/// Everything the editors need to know about one dialect's world schema. Later phases add the
/// quest, creature, NPC and SmartAI descriptors here.
/// </summary>
public interface IWorldSchema
{
    WorldDialect Dialect { get; }
    ITableCatalog Tables { get; }

    /// <summary>The export statements for spawn moves, graveyards and waypoints.</summary>
    ISqlDialect Sql { get; }
}

/// <summary>The schema object of each dialect.</summary>
public static class WorldSchemas
{
    public static IWorldSchema For(WorldDialect dialect) => dialect switch
    {
        WorldDialect.AzerothCore => AcSchema.Instance,
        WorldDialect.TrinityCore => TcSchema.Instance,
        _ => throw new InvalidDataException($"The {dialect} world database is not supported yet.")
    };
}

/// <summary>The AzerothCore 3.3.5 world schema. SmartCatalog and QuestModel remain its static data.</summary>
public sealed class AcSchema : IWorldSchema
{
    public static readonly AcSchema Instance = new();
    public WorldDialect Dialect => WorldDialect.AzerothCore;
    public ITableCatalog Tables => AcTables.Instance;
    public ISqlDialect Sql => AcSqlDialect.Instance;
}

/// <summary>
/// The TrinityCore master world schema. Its table catalog holds the creature tables only, so a quest
/// or SmartAI row fails rather than being written against AzerothCore's tables.
/// </summary>
public sealed class TcSchema : IWorldSchema
{
    public static readonly TcSchema Instance = new();
    public WorldDialect Dialect => WorldDialect.TrinityCore;
    public ITableCatalog Tables => TcTables.Instance;
    public ISqlDialect Sql => TcSqlDialect.Instance;
}
