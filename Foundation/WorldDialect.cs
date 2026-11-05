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

    /// <summary>The creature template page: shared groups, this dialect's fields.</summary>
    EntityPage TemplatePage { get; }

    /// <summary>The creature stats page: level, class and scaling fields. The values they produce are computed per dialect.</summary>
    EntityPage StatsPage { get; }

    /// <summary>The creature's vendor rows and per-item fields.</summary>
    ItemListSpec VendorPage { get; }

    /// <summary>Gossip menus, option fields and greeting storage.</summary>
    GossipPageSpec GossipPage { get; }

    TrainerPageSpec TrainerPage { get; }
    QuestLinksPageSpec QuestLinksPage { get; }

    /// <summary>SmartAI: event, action and target types, smart_scripts columns and the loader rules.</summary>
    SmartCatalog Smart { get; }

    /// <summary>The `conditions` table's condition types and source types, with their sentences and key labels.</summary>
    ConditionCatalog Conditions { get; }
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
