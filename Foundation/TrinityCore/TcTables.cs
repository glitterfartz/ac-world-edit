namespace ACWorldEdit.Foundation;

/// <summary>
/// The TrinityCore master creature tables the creature editor reads, with primary keys and column
/// types from TDB 1210.26091. The template page edits creature_template and creature_template_difficulty;
/// the other tables are still read-only in the editor. Waypoint tables are not listed and stay uneditable. Text
/// limits follow the column types (varchar); 0 means unbounded (mediumtext).
/// </summary>
public sealed partial class TcTables : ITableCatalog
{
    public static readonly TcTables Instance = new();

    public static readonly TableSchema[] Tables;

    static TcTables()
    {
        Tables = [CreatureTemplate, CreatureDifficulty, CreatureTemplateModel, CreatureEquipTemplate, CreatureImmunities, CreatureGossip, GossipMenu, NpcText, GossipOption, CreatureTrainer, Trainer, TrainerSpell, Vendor, QuestTemplate, QuestAddon, QuestRequestItems, QuestOfferReward, QuestObjectives, QuestStarter, QuestEnder, SpellClick, CreatureAI, SmartScripts, CreatureText, AreaTriggerScripts, AreaTriggerAI, SpawnCreature, SpawnGameObject, SpawnCreatureAddon, SpawnGameObjectAddon, CreatureTemplateAddon, BroadcastText, HotfixData, Conditions, GameObjectTemplate, GameObjectTemplateAddon, GameObjectQuestStarter, GameObjectQuestEnder, GameObjectQuestItem];
    }

    IReadOnlyList<TableSchema> ITableCatalog.All => Tables;
    TableSchema ITableCatalog.Find(string name) => Tables.FirstOrDefault(t => t.Name == name) ?? throw new InvalidDataException($"Table {name} is not editable for the TrinityCore world database yet.");
}
