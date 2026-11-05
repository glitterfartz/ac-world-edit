namespace ACWorldEdit.Foundation;

/// <summary>
/// The AzerothCore interaction tables, with primary keys from the AzerothCore base schema. Text
/// limits follow the column types (varchar/text); 0 means unbounded text.
/// </summary>
public sealed partial class AcTables : ITableCatalog
{
    public static readonly AcTables Instance = new();

    public static readonly TableSchema[] Tables;

    static AcTables()
    {
        Tables = [CreatureTemplate, GossipMenu, NpcText, GossipOption, QuestStarter, QuestEnder, Vendor, DefaultTrainer, Trainer, TrainerSpell, SpellClick, CreatureAI, CreatureStats, CreatureDetails, QuestTemplate, QuestAddon, QuestRequestItems, QuestOfferReward, SmartScripts, CreatureText, AreaTriggerScripts, SpawnCreature, SpawnGameObject, SpawnCreatureAddon, SpawnGameObjectAddon, CreatureTemplateAddon, Conditions];
    }

    IReadOnlyList<TableSchema> ITableCatalog.All => Tables;
    TableSchema ITableCatalog.Find(string name) => Tables.FirstOrDefault(t => t.Name == name) ?? throw new InvalidDataException($"Table {name} is not editable.");
}
