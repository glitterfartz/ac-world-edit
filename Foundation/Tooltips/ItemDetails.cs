namespace ACWorldEdit.Foundation;

/// <summary>
/// The item_template columns that a tooltip shows. Resistances are holy, fire, nature, frost, shadow,
/// arcane; sockets hold a colour mask each (0 none, 1 meta, 2 red, 4 yellow, 8 blue).
/// </summary>
public sealed record ItemDetails(
    ItemInfo Info,
    uint Class,
    uint Subclass,
    uint InventoryType,
    uint Bonding,
    uint Flags,
    uint ItemLevel,
    uint RequiredLevel,
    uint MaxCount,
    int AllowableClass,
    int AllowableRace,
    uint ContainerSlots,
    (uint Type, int Value)[] Stats,
    (float Min, float Max, uint Type)[] Damage,
    uint Delay,
    uint Armor,
    uint Block,
    int[] Resistances,
    uint MaxDurability,
    uint[] Sockets,
    string Description,
    long SellPrice);
