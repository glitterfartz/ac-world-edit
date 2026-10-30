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
    CreaturePage TemplatePage { get; }

    /// <summary>The creature stats page: level, class and scaling fields. The values they produce are computed per dialect.</summary>
    CreaturePage StatsPage { get; }

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

/// <summary>The AzerothCore 3.3.5 world schema. AcQuestModel and AcNpcFlags are AzerothCore-only static data beside it.</summary>
public sealed class AcSchema : IWorldSchema
{
    public static readonly AcSchema Instance = new();
    public WorldDialect Dialect => WorldDialect.AzerothCore;
    public ITableCatalog Tables => AcTables.Instance;
    public ISqlDialect Sql => AcSqlDialect.Instance;
    public CreaturePage TemplatePage => AcCreaturePage.Page;
    public CreaturePage StatsPage => AcStatsPage.Page;
    public ItemListSpec VendorPage => Foundation.VendorPage.AzerothCore;
    public GossipPageSpec GossipPage => Foundation.GossipPage.AzerothCore;
    public TrainerPageSpec TrainerPage => Foundation.TrainerPage.AzerothCore;
    public QuestLinksPageSpec QuestLinksPage => Foundation.QuestLinksPage.AzerothCore;
    public SmartCatalog Smart => SmartCatalog.Ac;
    public ConditionCatalog Conditions => ConditionCatalog.Ac;
}

/// <summary>
/// The TrinityCore master world schema. Its table catalog holds the creature, quest and SmartAI tables, so a
/// row of another table fails rather than being written against AzerothCore's tables.
/// </summary>
public sealed class TcSchema : IWorldSchema
{
    public static readonly TcSchema Instance = new();
    public WorldDialect Dialect => WorldDialect.TrinityCore;
    public ITableCatalog Tables => TcTables.Instance;
    public ISqlDialect Sql => TcSqlDialect.Instance;
    public CreaturePage TemplatePage => TcCreaturePage.Page;
    public CreaturePage StatsPage => TcStatsPage.Page;
    public ItemListSpec VendorPage => Foundation.VendorPage.TrinityCore;
    public GossipPageSpec GossipPage => Foundation.GossipPage.TrinityCore;
    public TrainerPageSpec TrainerPage => Foundation.TrainerPage.TrinityCore;
    public QuestLinksPageSpec QuestLinksPage => Foundation.QuestLinksPage.TrinityCore;
    public SmartCatalog Smart => SmartCatalog.Trinity;
    public ConditionCatalog Conditions => ConditionCatalog.Trinity;
}
