namespace ACWorldEdit.Foundation;

/// <summary>
/// npcflag bits from UnitDefines.h and the server's gossip option/icon enums (GossipDef.h).
/// </summary>
public static class AcNpcFlags
{
    public const uint Gossip = 0x1, QuestGiver = 0x2, Trainer = 0x10, TrainerClass = 0x20, TrainerProfession = 0x40, Vendor = 0x80, VendorAmmo = 0x100, VendorFood = 0x200, VendorPoison = 0x400, VendorReagent = 0x800, Repair = 0x1000, FlightMaster = 0x2000, SpiritHealer = 0x4000, SpiritGuide = 0x8000, Innkeeper = 0x10000, Banker = 0x20000, Petitioner = 0x40000, TabardDesigner = 0x80000, Battlemaster = 0x100000, Auctioneer = 0x200000, StableMaster = 0x400000, GuildBanker = 0x800000, SpellClick = 0x1000000, PlayerVehicle = 0x2000000, Mailbox = 0x4000000;
    public const uint VendorMask = Vendor | VendorAmmo | VendorFood | VendorPoison | VendorReagent;

    public static readonly (uint Bit, string Name, string Tip)[] All =
    [
        (Gossip, "Gossip", "Players can right-click to open a gossip dialog with this NPC."),
        (QuestGiver, "Quest giver", "Offers quests and accepts quest turn-ins."),
        (Trainer, "Trainer", "A class trainer: trains spells and abilities for the player's class."),
        (TrainerClass, "Class trainer", "Class trainer variant that also opens profession trainer checks."),
        (TrainerProfession, "Profession trainer", "Trains gathering and crafting professions."),
        (Vendor, "Vendor", "Sells items without a specialty."),
        (VendorAmmo, "Ammo vendor", "Sells arrows and bullets."),
        (VendorFood, "Food vendor", "Sells food and drink."),
        (VendorPoison, "Poison vendor", "Sells rogue poisons."),
        (VendorReagent, "Reagent vendor", "Sells spell and profession reagents."),
        (Repair, "Repair", "Repairs armor and weapons."),
        (FlightMaster, "Flight master", "Offers flight paths and taxi routes."),
        (SpiritHealer, "Spirit healer", "Revives dead players, applying resurrection sickness."),
        (SpiritGuide, "Spirit guide", "Battleground spirit guide that returns players to the field."),
        (Innkeeper, "Innkeeper", "Sets the player's hearthstone bind point and often logs out."),
        (Banker, "Banker", "Opens the player's personal bank."),
        (Petitioner, "Petitioner", "Collects signatures for guild charters."),
        (TabardDesigner, "Tabard designer", "Designs a guild tabard."),
        (Battlemaster, "Battlemaster", "Lets players queue for battlegrounds."),
        (Auctioneer, "Auctioneer", "Runs an auction house."),
        (StableMaster, "Stable master", "Stables hunter pets."),
        (GuildBanker, "Guild banker", "Opens the guild bank."),
        (SpellClick, "Spell click", "Casts a special effect when right-clicked, used for mounting and phased appearances."),
        (PlayerVehicle, "Player vehicle", "Right-clicking mounts the player in the vehicle seat."),
        (Mailbox, "Mailbox", "Acts as a mailbox when right-clicked.")
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
