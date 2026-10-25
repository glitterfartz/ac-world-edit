using System.Globalization;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;
using MySqlConnector;

namespace ACWorldEdit.Data;

/// <summary>
/// Read-only database snapshot of one creature entry's interaction rows plus the lookup data the
/// view and validation need. Rows are captured with InteractionTables' column whitelist only.
/// </summary>
public sealed class InteractionSnapshot(uint entry, ulong guid, string name, string scriptName, string aiName, int spawnCount, RowSet rows, ScriptHook[] scripts, Dictionary<int, int> conditions, InteractionLookupData lookup, string sourceKey)
{
    public uint Entry => entry;
    public ulong Guid => guid;
    public string Name => name;
    public string ScriptName => scriptName;
    public string AIName => aiName;
    public int SpawnCount => spawnCount;
    public RowSet Rows => rows;
    public ScriptHook[] Scripts => scripts;
    public IReadOnlyDictionary<int, int> Conditions => conditions;
    public InteractionLookupData Lookup => lookup;
    public string SourceKey => sourceKey;
}

/// <summary>
/// One creature_classlevelstats row. Health and damage have one value per expansion tier (the
/// creature_template.exp column picks it).
/// </summary>
public sealed record ClassLevelStats(uint Level, uint Class, uint[] BaseHealth, uint BaseMana, uint BaseArmor, uint AttackPower, uint RangedAttackPower, float[] DamageBase, int Strength, int Agility, int Stamina, int Intellect, int Spirit);

/// <summary>
/// IInteractionLookup backed by data read in the same snapshot transaction plus the client catalog.
/// </summary>
public sealed class InteractionLookupData(InteractionCatalog? catalog) : IInteractionLookup
{
    public readonly Dictionary<uint, ItemInfo> Items = [];
    /// <summary>Tooltip fields of the same items.</summary>
    public readonly Dictionary<uint, ItemDetails> ItemDetails = [];
    public readonly Dictionary<uint, QuestInfo> Quests = [];
    public readonly Dictionary<uint, (string, string)> Broadcasts = [];
    public readonly HashSet<uint> Pois = [], Menus = [], Texts = [], Trainers = [];
    public readonly Dictionary<uint, List<Row>> ReferenceVendors = [];
    public readonly Dictionary<uint, int> MenuUserCounts = [], TextUserCounts = [], TrainerUserCounts = [];
    public uint MaxMenuId, MaxTextId, MaxTrainerId;
    /// <summary>creature_classlevelstats keyed by (class, level).</summary>
    public readonly Dictionary<(uint Class, uint Level), ClassLevelStats> ClassLevelStats = [];
    public InteractionCatalog? Catalog => catalog;

    public ItemInfo? Item(uint id) => Items.GetValueOrDefault(id);
    public QuestInfo? Quest(uint id) => Quests.GetValueOrDefault(id);
    public string? SpellName(uint id) => catalog?.SpellName(id);
    public bool ExtendedCostExists(uint id) => catalog?.ExtendedCostExists(id) ?? true;
    public (string Male, string Female)? BroadcastText(uint id) => Broadcasts.TryGetValue(id, out var text) ? text : null;
    public bool PoiExists(uint id) => Pois.Contains(id);
    public bool MenuExists(uint menuId) => Menus.Contains(menuId);
    public bool TextExists(uint textId) => Texts.Contains(textId);
    public bool TrainerExists(uint id) => Trainers.Contains(id);
    public IReadOnlyList<Row> ReferenceVendor(uint entry) => ReferenceVendors.TryGetValue(entry, out var rows) ? rows : [];
    public int MenuUsers(uint menuId) => MenuUserCounts.GetValueOrDefault(menuId);
    public int TextUsers(uint textId) => TextUserCounts.GetValueOrDefault(textId);
    public int TrainerUsers(uint trainerId) => TrainerUserCounts.GetValueOrDefault(trainerId);
}
