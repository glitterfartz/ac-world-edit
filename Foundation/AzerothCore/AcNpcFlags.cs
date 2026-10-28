namespace ACWorldEdit.Foundation;

/// <summary>
/// npcflag bits from UnitDefines.h and the server's gossip option/icon enums (GossipDef.h).
/// </summary>
public static class AcNpcFlags
{
    public const uint Gossip = 0x1, QuestGiver = 0x2, Trainer = 0x10, TrainerClass = 0x20, TrainerProfession = 0x40, Vendor = 0x80, VendorAmmo = 0x100, VendorFood = 0x200, VendorPoison = 0x400, VendorReagent = 0x800, Repair = 0x1000, FlightMaster = 0x2000, SpiritHealer = 0x4000, SpiritGuide = 0x8000, Innkeeper = 0x10000, Banker = 0x20000, Petitioner = 0x40000, TabardDesigner = 0x80000, Battlemaster = 0x100000, Auctioneer = 0x200000, StableMaster = 0x400000, GuildBanker = 0x800000, SpellClick = 0x1000000, PlayerVehicle = 0x2000000, Mailbox = 0x4000000;
    public const uint VendorMask = Vendor | VendorAmmo | VendorFood | VendorPoison | VendorReagent;

    public static readonly (uint Bit, string Name)[] All =
    [
        (Gossip, "Gossip"), (QuestGiver, "Quest giver"), (Trainer, "Trainer"), (TrainerClass, "Class trainer"), (TrainerProfession, "Profession trainer"),
        (Vendor, "Vendor"), (VendorAmmo, "Ammo vendor"), (VendorFood, "Food vendor"), (VendorPoison, "Poison vendor"), (VendorReagent, "Reagent vendor"),
        (Repair, "Repair"), (FlightMaster, "Flight master"), (SpiritHealer, "Spirit healer"), (SpiritGuide, "Spirit guide"), (Innkeeper, "Innkeeper"),
        (Banker, "Banker"), (Petitioner, "Petitioner"), (TabardDesigner, "Tabard designer"), (Battlemaster, "Battlemaster"), (Auctioneer, "Auctioneer"),
        (StableMaster, "Stable master"), (GuildBanker, "Guild banker"), (SpellClick, "Spell click"), (PlayerVehicle, "Player vehicle"), (Mailbox, "Mailbox")
    ];

    public static IEnumerable<string> Names(uint flags) => All.Where(f => (flags & f.Bit) != 0).Select(f => f.Name);

    /// <summary>
    /// GOSSIP_OPTION_* names; values at or above Count are rejected by the server.
    /// </summary>
    public static readonly string[] OptionTypes = ["None", "Gossip", "Quest giver", "Vendor", "Taxi", "Trainer", "Spirit healer", "Spirit guide", "Innkeeper", "Banker", "Petitioner", "Tabard designer", "Battlefield", "Auctioneer", "Stable", "Armorer", "Unlearn talents", "Unlearn pet talents", "Learn dual spec", "Outdoor PvP", "Dual spec info"];
    /// <summary>
    /// GOSSIP_ICON_* names; 14 and 15 are invalid and 21+ are replaced by the server.
    /// </summary>
    public static readonly string[] OptionIcons = ["Chat", "Vendor", "Taxi", "Trainer", "Interact", "Interact 2", "Money bag", "Talk", "Tabard", "Battle", "Dot", "Chat 11", "Chat 12", "Chat 13", "Invalid 14", "Invalid 15", "Chat 16", "Chat 17", "Chat 18", "Chat 19", "Chat 20"];
}
