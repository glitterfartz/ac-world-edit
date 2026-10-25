using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>
/// The AzerothCore 3.3.5 creature template page: creature_details holds nearly every column, with
/// creature_template for the title and npcflag. Choice lists and flag names are the Wrath client's.
/// </summary>
public static class AcCreaturePage
{
    static readonly (int, string)[] Ranks = [(0, "Normal"), (1, "Elite"), (2, "Rare Elite"), (3, "Boss"), (4, "Rare")];
    static readonly (int, string)[] DamageSchools = [(0, "Physical"), (1, "Holy"), (2, "Fire"), (3, "Nature"), (4, "Frost"), (5, "Shadow"), (6, "Arcane")];
    static readonly (int, string)[] MovementTypes = [(0, "Stands still"), (1, "Wanders randomly"), (2, "Follows waypoints")];
    static readonly (int, string)[] Types =
    [
        (0, "None"), (1, "Beast"), (2, "Dragonkin"), (3, "Demon"), (4, "Elemental"), (5, "Giant"), (6, "Undead"), (7, "Humanoid"),
        (8, "Critter"), (9, "Mechanical"), (10, "Not specified"), (11, "Totem"), (12, "Non-combat pet"), (13, "Gas cloud")
    ];
    static readonly (int, string)[] Families =
    [
        (0, "None"), (1, "Wolf"), (2, "Cat"), (3, "Spider"), (4, "Bear"), (5, "Boar"), (6, "Crocolisk"), (7, "Carrion bird"), (8, "Crab"),
        (9, "Gorilla"), (11, "Raptor"), (12, "Tallstrider"), (15, "Felhunter"), (16, "Voidwalker"), (17, "Succubus"), (19, "Doomguard"),
        (20, "Scorpid"), (21, "Turtle"), (23, "Imp"), (24, "Bat"), (25, "Hyena"), (26, "Bird of prey"), (27, "Wind serpent"),
        (28, "Remote control"), (29, "Felguard"), (30, "Dragonhawk"), (31, "Ravager"), (32, "Warp stalker"), (33, "Sporebat"),
        (34, "Nether ray"), (35, "Serpent"), (37, "Moth"), (38, "Chimaera"), (39, "Devilsaur"), (40, "Ghoul"), (41, "Silithid"),
        (42, "Worm"), (43, "Rhino"), (44, "Wasp"), (45, "Core hound"), (46, "Spirit beast")
    ];

    static readonly FlagBit[] UnitFlags =
    [
        new(0x2, "Cannot be attacked", "Players cannot target it for attack at all."),
        new(0x4, "Cannot move", "The creature is rooted in place."),
        new(0x100, "Ignores players", "Neither fights nor helps players. Used for scripted or neutral creatures."),
        new(0x200, "Ignores creatures", "Neither fights nor helps other non-player creatures."),
        new(0x1000, "PvP flagged", "Treated as flagged for player-versus-player combat."),
        new(0x2000, "Silenced", "Cannot cast spells."),
        new(0x4000, "Cannot swim", "Will not enter water."),
        new(0x20000, "Pacified", "Will not attack, but can still cast spells."),
        new(0x40000, "Stunned", "Permanently stunned."),
        new(0x200000, "Disarmed", "Cannot use melee attacks."),
        new(0x2000000, "Cannot be selected", "Players cannot click or target it. Typical for invisible helper creatures."),
        new(0x4000000, "Skinnable", "Shows the skinning sparkle when dead."),
        new(0x8000000, "Mounted", "Displayed as riding a mount."),
        new(0x40000000, "Weapons sheathed", "Shows with weapons put away."),
        new(0x80000000, "Immune to all damage", "Takes no damage from anything.")
    ];

    static readonly FlagBit[] UnitFlags2 =
    [
        new(0x1, "Feigns death", "Plays the feign-death pose while alive."),
        new(0x2, "Hides model", "Hides the creature's model."),
        new(0x4, "Ignores reputation", "Reputation does not change how it treats players."),
        new(0x8, "Understands all languages", "Can talk to both factions."),
        new(0x10, "Mirror image", "Copies the appearance of its summoner."),
        new(0x20, "Appears instantly", "Does not fade in when spawned."),
        new(0x80, "Off-hand disarmed", "Cannot use its off-hand weapon."),
        new(0x400, "Ranged disarmed", "Cannot use its ranged weapon."),
        new(0x800, "Regenerates power", "Mana and other power slowly regenerate."),
        new(0x1000, "Party-only interaction", "Only the player's party or raid may interact with it."),
        new(0x2000, "Blocks spell click", "Players cannot use its spell-click action."),
        new(0x4000, "Enemies can interact", "Hostile players can still use it."),
        new(0x8000, "Cannot turn", "Keeps its facing fixed."),
        new(0x20000, "Special death animation", "Plays its special death animation.")
    ];

    static readonly FlagBit[] TypeFlags =
    [
        new(0x1, "Tameable", "Hunters can tame it. It must also be a beast with a family."),
        new(0x2, "Visible to ghosts", "Dead players can see and interact with it."),
        new(0x4, "Boss", "Shows level ?? in its portrait and is immune to knockback."),
        new(0x8, "No wound animation", "Does not flinch when it parries."),
        new(0x10, "Hide faction tooltip", "Does not show its faction in the tooltip."),
        new(0x40, "Spell attackable", "Can be targeted by spells that normally need an attackable unit."),
        new(0x80, "Interactable while dead", "Players can interact with the corpse."),
        new(0x100, "Herbalism loot", "Corpse can be gathered with Herbalism."),
        new(0x200, "Mining loot", "Corpse can be gathered with Mining."),
        new(0x400, "No death message", "Its death is not written to the combat log."),
        new(0x800, "Mounted combat", "Stays mounted when it enters combat."),
        new(0x1000, "Can assist players", "May help nearby players who are fighting."),
        new(0x8000, "Engineering loot", "Corpse can be gathered with Engineering."),
        new(0x10000, "Exotic pet", "Only Beast Mastery hunters can tame it."),
        new(0x100000, "Hide nameplate", "Does not show a name above its head."),
        new(0x8000000, "Show one gossip option", "Displays a single gossip option even if the flags would not."),
        new(0x10000000, "Never sheathe weapons", "Keeps its weapons drawn.")
    ];

    static readonly FlagBit[] ExtraFlags =
    [
        new(0x1, "Binds instance on kill", "Killing it locks the killer and their group to the instance."),
        new(0x2, "Civilian", "Never initiates combat, regardless of faction."),
        new(0x4, "Cannot parry", "Attacks against it cannot be parried."),
        new(0x10, "Cannot block", "Attacks against it cannot be blocked."),
        new(0x20, "No crushing blows", "Its attacks cannot be crushing blows."),
        new(0x40, "Gives no experience", "Killing it grants no XP."),
        new(0x80, "Trigger", "Invisible helper creature used by scripts."),
        new(0x100, "Immune to taunt", "Taunt effects do nothing to it."),
        new(0x400, "Ghost visibility", "Only visible to dead players."),
        new(0x800, "Uses off-hand attack", "Attacks with a second weapon."),
        new(0x1000, "Vendor will not buy", "Players cannot sell items to it."),
        new(0x2000, "Cannot enter combat", "Never fights."),
        new(0x8000, "Guard", "Behaves as a city or town guard."),
        new(0x10000, "Ignores feign death", "Is not fooled by Feign Death."),
        new(0x20000, "Cannot critically strike", "Its attacks never crit."),
        new(0x40000, "No skill gains", "Fighting it does not train weapon skills."),
        new(0x200000, "No damage needed for credit", "Kill credit does not require the player to have damaged it."),
        new(0x800000, "Cannot be dodged", "Attacks against it cannot be dodged."),
        new(0x2000000, "Does not call for help", "Does not summon nearby allies when it aggroes."),
        new(0x4000000, "Ignores calls for help", "Does not join fights started by nearby allies."),
        new(0x20000000, "Ignores pathfinding", "Moves in straight lines instead of following the navigation mesh."),
        new(0x40000000, "Immune to knockback", "Cannot be knocked back.")
    ];

    static readonly FlagBit[] NpcServices = NpcFlags.All.Select(f => new FlagBit(f.Bit, f.Name)).ToArray();

    static string Money(string copper)
    {
        uint value = uint.Parse(copper, CultureInfo.InvariantCulture);
        return value == 0 ? "None" : $"{value / 10000}g {value / 100 % 100}s {value % 100}c";
    }

    static string Shown(string value) => float.Parse(value, CultureInfo.InvariantCulture).ToString("0.##", CultureInfo.InvariantCulture);

    public static readonly CreaturePage Page = Build();

    static CreaturePage Build()
    {
        var d = AcTables.CreatureDetails;
        var t = AcTables.CreatureTemplate;
        FieldSpec Text(string group, TableSchema table, string column, string label, string tip, bool allowEmpty = false, Func<string, string>? display = null) =>
            new(group, table, column, label, tip, FieldKind.Text, AllowEmpty: allowEmpty, Display: display);
        FieldSpec Whole(string group, string column, string label, string tip, ulong max = 0, Func<string, string>? display = null) =>
            new(group, d, column, label, tip, FieldKind.Whole, Max: max, Display: display);
        FieldSpec Decimal(string group, string column, string label, string tip, Func<string, string>? display = null) =>
            new(group, d, column, label, tip, FieldKind.Decimal, Display: display ?? Shown);
        FieldSpec Choice(string group, string column, string label, string tip, (int, string)[] options) =>
            new(group, d, column, label, tip, FieldKind.Choice, Options: options);
        FieldSpec Check(string group, string column, string label, string tip) => new(group, d, column, label, tip, FieldKind.Check);
        FieldSpec Flags(string column, string label, string tip, FlagBit[] bits) => new("flags", d, column, label, tip, FieldKind.Flags, Flags: bits);

        FieldSpec[] fields =
        [
            Text("identity", d, "name", "Name", "The name players see above the creature."),
            Text("identity", t, "subname", "Title", "The text in angle brackets under the name, such as <Weapon Merchant>. Leave blank for none.", true, s => s.Length > 0 ? $"<{s}>" : "None"),
            Choice("identity", "type", "Creature type", "The creature's kind. Some spells, abilities and quest objectives only work on certain types, such as Banish on demons.", Types),
            Choice("identity", "family", "Pet family", "Which hunter pet family the creature belongs to. Only matters for tameable beasts.", Families),
            Choice("identity", "rank", "Classification", "Normal, Elite, Rare Elite, Boss or Rare. Decides the dragon frame around its portrait and some combat rules.", Ranks),
            Whole("identity", "faction", "Faction", "Faction template ID (from FactionTemplate.dbc). Decides who the creature is friendly or hostile to; 35 is friendly to everyone, 14 is hostile to everyone.", 65535),
            Check("identity", "RacialLeader", "Racial leader", "Marks the creature as the leader of its race, which affects some player titles and reputation rewards."),
            Text("identity", d, "IconName", "Mouse cursor icon", "Name of the cursor icon shown when hovering over the creature, such as Speak, Taxi, Buy or Directions. Blank uses the default.", true, s => s.Length > 0 ? s : "Default"),

            new("interaction", t, "npcflag", "NPC services", "What players can do by right-clicking: talk, buy, train, take quests and so on. Each ticked service must be backed by the matching records on the other pages.", FieldKind.Flags, Flags: NpcServices, Raw: false),

            Choice("combat", "dmgschool", "Damage type", "The school of magic the creature's melee attacks deal. Physical damage is reduced by armor, other schools by resistance.", DamageSchools),
            Whole("combat", "BaseAttackTime", "Melee attack interval", "Milliseconds between melee swings. Lower is faster; 2000 is the usual default.", display: s => $"{s} ms"),
            Whole("combat", "RangeAttackTime", "Ranged attack interval", "Milliseconds between ranged attacks.", display: s => $"{s} ms"),
            Decimal("combat", "BaseVariance", "Melee damage spread", "How much melee hits vary. 1 uses the normal damage spread; 0 makes every hit the same size."),
            Decimal("combat", "RangeVariance", "Ranged damage spread", "How much ranged hits vary, like the melee damage spread."),
            Decimal("combat", "detection_range", "Aggro range", "Distance in yards at which the creature notices players and attacks. Higher level differences shrink or grow it.", s => Shown(s) + " yd"),
            Check("combat", "RegenHealth", "Regenerates health", "Whether it heals itself when out of combat. Turn off for creatures that must stay wounded."),
            Decimal("combat", "ExperienceModifier", "Experience multiplier", "Multiplies the experience a player earns for killing it. 1 is normal, 0 gives none."),
            Whole("combat", "CreatureImmunitiesId", "Immunities set", "ID of a set of immunities to spells, mechanics and schools. Zero means none. Negative values are not used."),

            Choice("movement", "MovementType", "Movement behaviour", "What it does when it is not fighting.", MovementTypes),
            Decimal("movement", "speed_walk", "Walk speed", "Speed when walking."),
            Decimal("movement", "speed_run", "Run speed", "Speed when running, such as while chasing a target."),
            Decimal("movement", "speed_swim", "Swim speed", "Speed while swimming."),
            Decimal("movement", "speed_flight", "Flight speed", "Speed while flying."),
            Decimal("movement", "HoverHeight", "Hover height", "How far above the ground the creature hovers, in yards. 1 is the normal standing height."),
            Whole("movement", "movementId", "Movement style ID", "Selects a movement style from the server's creature movement table, such as always walking. Zero uses the default."),

            Whole("loot", "lootid", "Loot table", "ID of the loot table rolled when the creature dies. Zero drops nothing. It matches the 'entry' of its loot rows."),
            Whole("loot", "pickpocketloot", "Pickpocket table", "ID of the loot table a rogue can steal with Pickpocket. Zero means it cannot be pickpocketed."),
            Whole("loot", "skinloot", "Skinning table", "ID of the loot table gathered by skinning, mining or herbalism. Zero means it cannot be skinned."),
            Whole("loot", "mingold", "Minimum money", "Lowest amount of money it can drop, in copper (100 copper = 1 silver, 100 silver = 1 gold).", display: Money),
            Whole("loot", "maxgold", "Maximum money", "Highest amount of money it can drop, in copper.", display: Money),

            Whole("links", "KillCredit1", "Kill credit 1", "A second creature entry that also counts as killed when this one dies, for quests that name another creature. Zero for none."),
            Whole("links", "KillCredit2", "Kill credit 2", "A third creature entry that also counts as killed. Zero for none."),
            Whole("links", "difficulty_entry_1", "Heroic version", "Entry used for this creature in heroic 10-player or 25-player normal dungeons and raids. Zero for none."),
            Whole("links", "difficulty_entry_2", "25-player heroic version", "Entry used in 25-player heroic raids. Zero for none."),
            Whole("links", "difficulty_entry_3", "Third difficulty version", "Entry used for a further difficulty setting. Zero for none."),
            Whole("links", "VehicleId", "Vehicle ID", "ID of the vehicle seat layout if players can ride it. Zero if it is not a vehicle."),
            Whole("links", "PetSpellDataId", "Pet spell set", "ID of the set of spells it offers on a hunter's or warlock's pet bar. Zero for none."),
            Text("links", d, "ScriptName", "C++ script", "Name of a compiled server script that drives the creature. It can replace gossip, quests and SmartAI entirely, so leave blank unless the script exists.", true, s => s.Length > 0 ? s : "None"),

            Flags("unit_flags", "State flags", "Basic state and combat switches, such as being unattackable or unselectable.", UnitFlags),
            Flags("unit_flags2", "More state flags", "Additional state switches, such as hiding the model or ignoring reputation.", UnitFlags2),
            Flags("type_flags", "Creature type flags", "Tags on the kind of creature: tameable, boss portrait, gatherable corpse and so on.", TypeFlags),
            Flags("flags_extra", "Server rules", "Special rules the server applies to it, such as no parry, civilian or guard.", ExtraFlags),
            Flags("dynamicflags", "Dynamic flags", "Runtime display state such as lootable or tapped. The server sets these while the game runs, so they are normally left at 0.", [])
        ];

        return new(CreaturePage.StandardGroups("Speeds are multiples of the normal rate: 1 is a player's walking pace, 1.14286 is the usual run.", "Tick the behaviours that apply. The raw value keeps any bits the list does not name."), fields);
    }
}
