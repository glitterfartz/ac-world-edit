namespace ACWorldEdit.Foundation;

/// <summary>
/// The names an item tooltip uses, which differ by client: item classes, weapon and armor kinds, playable
/// classes and races, and how a stat reads. <see cref="Wrath"/> is the 3.3.5 client; <see cref="Retail"/>
/// the retail client the TrinityCore master database serves.
/// </summary>
public sealed record ItemVocabulary(
    string[] Classes, string[] WeaponKinds, string[] ArmorKinds,
    string[] ClassNames, int AllClasses, string[] RaceNames, int AllRaces,
    IReadOnlyDictionary<uint, string> Plain, IReadOnlyDictionary<uint, string> Equip, string EquipPrefix)
{
    /// <summary>3.3.5: health, mana and the attributes read "+N Name" in white; every other stat is a green "Equip:" line.</summary>
    public static readonly ItemVocabulary Wrath = new(
        ["Consumable", "Container", "Weapon", "Gem", "Armor", "Reagent", "Projectile", "Trade Goods", "Generic", "Recipe", "Money", "Quiver", "Quest", "Key", "Permanent", "Miscellaneous", "Glyph"],
        ["Axe", "Two-Handed Axe", "Bow", "Gun", "Mace", "Two-Handed Mace", "Polearm", "Sword", "Two-Handed Sword", "Obsolete", "Staff", "Exotic", "Exotic", "Fist Weapon", "Miscellaneous", "Dagger", "Thrown", "Spear", "Crossbow", "Wand", "Fishing Pole"],
        ["Miscellaneous", "Cloth", "Leather", "Mail", "Plate", "Buckler", "Shield", "Libram", "Idol", "Totem", "Sigil"],
        ["Warrior", "Paladin", "Hunter", "Rogue", "Priest", "Death Knight", "Shaman", "Mage", "Warlock", "", "Druid"], 0x5FF,
        ["Human", "Orc", "Dwarf", "Night Elf", "Undead", "Tauren", "Gnome", "Troll", "", "Blood Elf", "Draenei"], 0x672,
        new Dictionary<uint, string>
        {
            [0] = "Mana", [1] = "Health", [3] = "Agility", [4] = "Strength", [5] = "Intellect", [6] = "Spirit", [7] = "Stamina"
        },
        new Dictionary<uint, string>
        {
            [12] = "Increases defense rating by {0}.", [13] = "Increases your dodge rating by {0}.", [14] = "Increases your parry rating by {0}.",
            [15] = "Increases your shield block rating by {0}.", [16] = "Improves melee hit rating by {0}.", [17] = "Improves ranged hit rating by {0}.",
            [18] = "Improves spell hit rating by {0}.", [19] = "Improves melee critical strike rating by {0}.", [20] = "Improves ranged critical strike rating by {0}.",
            [21] = "Improves spell critical strike rating by {0}.", [22] = "Improves melee hit avoidance rating by {0}.", [23] = "Improves ranged hit avoidance rating by {0}.",
            [24] = "Improves spell hit avoidance rating by {0}.", [25] = "Improves melee critical avoidance rating by {0}.", [26] = "Improves ranged critical avoidance rating by {0}.",
            [27] = "Improves spell critical avoidance rating by {0}.", [28] = "Improves melee haste rating by {0}.", [29] = "Improves ranged haste rating by {0}.",
            [30] = "Improves spell haste rating by {0}.", [31] = "Improves hit rating by {0}.", [32] = "Improves critical strike rating by {0}.",
            [33] = "Improves hit avoidance rating by {0}.", [34] = "Improves critical avoidance rating by {0}.", [35] = "Improves your resilience rating by {0}.",
            [36] = "Improves haste rating by {0}.", [37] = "Increases your expertise rating by {0}.", [38] = "Increases attack power by {0}.",
            [39] = "Increases ranged attack power by {0}.", [40] = "Increases attack power by {0} in Cat, Bear, Dire Bear, and Moonkin forms only.",
            [41] = "Increases healing done by magical spells and effects by up to {0}.", [42] = "Increases damage done by magical spells and effects by up to {0}.",
            [43] = "Restores {0} mana per 5 sec.", [44] = "Your attacks ignore {0} of your opponent's armor.", [45] = "Increases spell power by {0}.",
            [46] = "Restores {0} health per 5 sec.", [47] = "Increases your spell penetration by {0}.", [48] = "Increases the block value of your shield by {0}."
        },
        "Equip: ");

    /// <summary>
    /// Retail: attributes and health read "+N Name" in white and every secondary stat "+N Name" in green
    /// (ItemModType, TrinityCore ItemTemplate.h). Race restrictions are not listed: retail has far more
    /// races than the single mask the tooltip decodes.
    /// </summary>
    public static readonly ItemVocabulary Retail = new(
        ["Consumable", "Container", "Weapon", "Gem", "Armor", "Reagent", "Projectile", "Trade Goods", "Item Enhancement", "Recipe", "Money", "Quiver", "Quest", "Key", "Permanent", "Miscellaneous", "Glyph", "Battle Pets", "WoW Token", "Profession"],
        ["Axe", "Two-Handed Axe", "Bow", "Gun", "Mace", "Two-Handed Mace", "Polearm", "Sword", "Two-Handed Sword", "Warglaive", "Staff", "Bear Claws", "Cat Claws", "Fist Weapon", "Miscellaneous", "Dagger", "Thrown", "Spear", "Crossbow", "Wand", "Fishing Pole"],
        ["Miscellaneous", "Cloth", "Leather", "Mail", "Plate", "Cosmetic", "Shield", "Libram", "Idol", "Totem", "Sigil", "Relic"],
        ["Warrior", "Paladin", "Hunter", "Rogue", "Priest", "Death Knight", "Shaman", "Mage", "Warlock", "Monk", "Druid", "Demon Hunter", "Evoker"], 0x1FFF,
        [], 0,
        new Dictionary<uint, string>
        {
            [0] = "Mana", [1] = "Health", [3] = "Agility", [4] = "Strength", [5] = "Intellect", [6] = "Spirit", [7] = "Stamina",
            [71] = "Agility, Strength or Intellect", [72] = "Agility or Strength", [73] = "Agility or Intellect", [74] = "Strength or Intellect"
        },
        new Dictionary<uint, string>
        {
            [12] = "{0} Defense", [13] = "{0} Dodge", [14] = "{0} Parry", [15] = "{0} Block", [16] = "{0} Melee Hit", [17] = "{0} Ranged Hit", [18] = "{0} Spell Hit",
            [19] = "{0} Melee Critical Strike", [20] = "{0} Ranged Critical Strike", [21] = "{0} Spell Critical Strike",
            [28] = "{0} Melee Haste", [29] = "{0} Ranged Haste", [30] = "{0} Spell Haste", [31] = "{0} Hit", [32] = "{0} Critical Strike", [35] = "{0} Resilience",
            [36] = "{0} Haste", [37] = "{0} Expertise", [38] = "{0} Attack Power", [39] = "{0} Ranged Attack Power", [40] = "{0} Versatility",
            [41] = "{0} Healing", [42] = "{0} Spell Damage", [43] = "{0} Mana per 5 sec", [44] = "{0} Armor Penetration", [45] = "{0} Spell Power",
            [46] = "{0} Health per 5 sec", [47] = "{0} Spell Penetration", [48] = "{0} Block Value", [49] = "{0} Mastery", [50] = "{0} Bonus Armor",
            [57] = "{0} PvP Power", [61] = "{0} Speed", [62] = "{0} Leech", [63] = "{0} Avoidance", [64] = "{0} Indestructible"
        },
        "+");
}
