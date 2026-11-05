namespace ACWorldEdit.Foundation;

/// <summary>The AzerothCore 3.3.5 world schema. AcQuestModel and AcNpcFlags are AzerothCore-only static data beside it.</summary>
public sealed class AcSchema : IWorldSchema
{
    public static readonly AcSchema Instance = new();
    public WorldDialect Dialect => WorldDialect.AzerothCore;
    public ITableCatalog Tables => AcTables.Instance;
    public ISqlDialect Sql => AcSqlDialect.Instance;
    public EntityPage TemplatePage => AcCreaturePage.Page;
    public EntityPage StatsPage => AcStatsPage.Page;
    public ItemListSpec VendorPage => Foundation.VendorPage.AzerothCore;
    public GossipPageSpec GossipPage => Foundation.GossipPage.AzerothCore;
    public TrainerPageSpec TrainerPage => Foundation.TrainerPage.AzerothCore;
    public QuestLinksPageSpec QuestLinksPage => Foundation.QuestLinksPage.AzerothCore;
    public SmartCatalog Smart => SmartCatalog.Ac;
    public ConditionCatalog Conditions => ConditionCatalog.Ac;
}
