namespace ACWorldEdit.Foundation;

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
    public EntityPage TemplatePage => TcCreaturePage.Page;
    public EntityPage StatsPage => TcStatsPage.Page;
    public ItemListSpec VendorPage => Foundation.VendorPage.TrinityCore;
    public GossipPageSpec GossipPage => Foundation.GossipPage.TrinityCore;
    public TrainerPageSpec TrainerPage => Foundation.TrainerPage.TrinityCore;
    public QuestLinksPageSpec QuestLinksPage => Foundation.QuestLinksPage.TrinityCore;
    public SmartCatalog Smart => SmartCatalog.Trinity;
    public ConditionCatalog Conditions => ConditionCatalog.Trinity;
}
