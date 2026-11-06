using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>
/// The TrinityCore master creature template page. Identity, interaction and movement fields bind to
/// creature_template; combat, loot and the per-difficulty flags bind to the creature_template_difficulty
/// row picked on the page. The unit, type, static and extra flag columns have no named bits yet (the
/// names must come from TrinityCore's UnitDefines.h / SharedDefines.h, not the Wrath lists), so they
/// edit as raw values.
/// </summary>
public static class TcCreaturePage
{
    /// <summary>TrinityCore Classification (trinitycore.info, creature_template).</summary>
    static readonly (int, string)[] Classifications =
        [(0, "Normal"), (1, "Elite"), (2, "Rare Elite"), (3, "Obsolete"), (4, "Rare"), (5, "Trivial"), (6, "Minus Mob")];

    static readonly FlagBit[] NpcServices = TcCreatureView.NpcFlagBits.Select(f => new FlagBit(f.Bit, f.Name, f.Tip)).ToArray();

    static readonly (int, string)[] DamageSchools = [(0, "Physical"), (1, "Holy"), (2, "Fire"), (3, "Nature"), (4, "Frost"), (5, "Shadow"), (6, "Arcane")];

    /// <summary>UnitFlags (UnitDefines.h, master).</summary>
    public static readonly FlagBit[] UnitFlags =
    [
        new(0x1, "Server controlled", "Movement is driven by the server rather than the client."),
        new(0x2, "Not attackable", "Players cannot attack it."),
        new(0x4, "Remove client control", "Legacy switch that disables client movement control."),
        new(0x8, "Player controlled", "Controlled by a player (used with immune-to-NPC instead of immune-to-player)."),
        new(0x10, "Can be renamed", "Allows the unit to be renamed."),
        new(0x20, "Preparation", "Skips spell reagents while the unit is preparing."),
        new(0x80, "Not attackable 1", "Combined with player-controlled this makes the unit non-PvP-attackable."),
        new(0x100, "Immune to players", "Players cannot attack or assist it."),
        new(0x200, "Immune to NPCs", "Non-player creatures cannot attack or assist it."),
        new(0x400, "Looting", "Plays the looting animation."),
        new(0x800, "Pet in combat", "A pet is chasing a target, or one of its minions is in combat."),
        new(0x1000, "PvP enabling", "Legacy PvP flag; the client now reads the PvP byte."),
        new(0x2000, "Force nameplate", "Always shows the nameplate."),
        new(0x4000, "Cannot swim", "Will not enter water."),
        new(0x8000, "Can swim", "Shows the swim animation in water."),
        new(0x10000, "Not attackable 2", "Removes the attackable icon without fully disabling interaction."),
        new(0x20000, "Pacified", "Will not attack, but can still cast spells."),
        new(0x40000, "Stunned", "Permanently stunned."),
        new(0x80000, "In combat", "Currently fighting."),
        new(0x100000, "On taxi", "Flying a taxi route; most casts are disabled."),
        new(0x200000, "Disarmed", "Cannot use melee attacks."),
        new(0x400000, "Confused", "Runs around confused."),
        new(0x800000, "Fleeing", "Runs away in fear."),
        new(0x1000000, "Possessed", "Controlled directly by a player through possess or a vehicle."),
        new(0x2000000, "Uninteractible", "Players cannot interact with it."),
        new(0x4000000, "Skinnable", "Shows the skinning sparkle when dead."),
        new(0x8000000, "Mounted", "Displayed as riding a mount."),
        new(0x20000000, "No chat emotes", "Does not play emotes parsed from chat text."),
        new(0x40000000, "Sheathed", "Shows with weapons put away."),
        new(0x80000000, "Immune", "Takes no damage.")
    ];

    /// <summary>UnitFlags2 (UnitDefines.h, master).</summary>
    public static readonly FlagBit[] UnitFlags2 =
    [
        new(0x1, "Feigns death", "Plays the feign-death pose while alive."),
        new(0x2, "Hides body", "Hides the model, showing only player equipment."),
        new(0x4, "Ignores reputation", "Reputation does not change how it treats players."),
        new(0x8, "Understands all languages", "Can talk to both factions."),
        new(0x10, "Mirror image", "Copies the appearance of its summoner."),
        new(0x20, "Does not fade in", "Appears instantly instead of fading in."),
        new(0x40, "Force movement", "Movement is forced by the server."),
        new(0x80, "Off-hand disarmed", "Cannot use its off-hand weapon."),
        new(0x100, "No predicted stats", "The player has disabled predicted stats."),
        new(0x200, "Change talents anywhere", "Allows talent changes outside a rest area."),
        new(0x400, "Ranged disarmed", "Cannot use its ranged weapon."),
        new(0x800, "Regenerates power", "Mana and other power slowly regenerate."),
        new(0x1000, "Party-only interaction", "Only the player's party or raid may interact with it."),
        new(0x2000, "Blocks spell click", "Players cannot use its spell-click action."),
        new(0x4000, "Hostile interaction", "Hostile players can still interact with it."),
        new(0x8000, "Cannot turn", "Keeps its facing fixed."),
        new(0x20000, "Special death animation", "Plays its special death animation."),
        new(0x40000, "Cheat spells", "Allows casting spells marked as cheat spells."),
        new(0x80000, "Suppress highlight", "No highlight when targeted or hovered."),
        new(0x100000, "Treat as raid unit", "Helpful spells treat it as a raid member."),
        new(0x200000, "Large area of interest", "Uses a large area-of-interest radius."),
        new(0x400000, "Gigantic area of interest", "Uses a gigantic area-of-interest radius."),
        new(0x800000, "No actions", "Disables client-side actions."),
        new(0x1000000, "Swim only with target", "The AI only swims when its target swims."),
        new(0x2000000, "No NPC combat log", "Does not write combat-log entries when engaged with NPCs."),
        new(0x4000000, "Untargetable by client", "The client cannot target it."),
        new(0x8000000, "Ignores minimum range", "Attackers ignore minimum ranges."),
        new(0x10000000, "Uninteractible if hostile", "Hostile players cannot interact with it."),
        new(0x40000000, "Infinite area of interest", "Uses an infinite area-of-interest radius.")
    ];

    /// <summary>UnitFlags3 (UnitDefines.h, master).</summary>
    public static readonly FlagBit[] UnitFlags3 =
    [
        new(0x2, "Unconscious on death", "Shows \"Unconscious\" instead of \"Dead\"."),
        new(0x4, "Mounted combat", "Remains mounted when entering combat."),
        new(0x8, "Garrison pet", "Displays a favorite player battle pet."),
        new(0x10, "UI can get position", "Lua position functions work on it."),
        new(0x20, "AI obstacle", "Other creatures path around it."),
        new(0x40, "Alternative language", "Uses the alternative default language."),
        new(0x80, "Suppress all NPC feedback", "Skips click sounds for every visible NPC."),
        new(0x100, "Ignore combat", "Behaves as if ignored by combat."),
        new(0x200, "Suppress NPC feedback", "Skips its click sounds."),
        new(0x2000, "Fake dead", "Shows as dead."),
        new(0x4000, "Fast facing", "Does not change facing on interaction and turns faster in combat."),
        new(0x8000, "Untargetable from UI", "Lua attack and target functions cannot select it."),
        new(0x20000, "Already skinned", "Has already been skinned."),
        new(0x40000, "Suppress all NPC sounds", "Skips interaction sounds for every visible NPC."),
        new(0x80000, "Suppress NPC sounds", "Skips its interaction sounds."),
        new(0x100000, "Interact in combat", "Allows vendor, gossip and quest interactions in combat."),
        new(0x400000, "Do not fade out", "Does not fade out when it despawns."),
        new(0x1000000, "Force hide nameplate", "Always hides the nameplate.")
    ];

    /// <summary>CreatureTypeFlags (SharedDefines.h, master).</summary>
    public static readonly FlagBit[] TypeFlags =
    [
        new(0x1, "Tameable", "Hunters can tame it. It must also be a beast with a family."),
        new(0x2, "Visible to ghosts", "Dead players can see and interact with it."),
        new(0x4, "Boss", "Shows level ?? in its portrait and is immune to knockback."),
        new(0x8, "No wound animation", "Does not flinch when it parries."),
        new(0x10, "Hide faction tooltip", "Does not show its faction in the tooltip."),
        new(0x20, "More audible", "Plays its sounds more loudly."),
        new(0x40, "Spell attackable", "Can be targeted by spells that normally need an attackable unit."),
        new(0x80, "Interactable while dead", "Players can interact with the corpse."),
        new(0x100, "Herbalism loot", "Corpse can be gathered with Herbalism."),
        new(0x200, "Mining loot", "Corpse can be gathered with Mining."),
        new(0x400, "No death message", "Its death is not written to the combat log."),
        new(0x800, "Mounted combat", "Stays mounted when it enters combat."),
        new(0x1000, "Can assist", "May help nearby players who are fighting."),
        new(0x2000, "No pet bar", "Does not show a pet action bar."),
        new(0x4000, "Mask UID", "Hides its unit ID from the client."),
        new(0x8000, "Engineering loot", "Corpse can be gathered with Engineering."),
        new(0x10000, "Exotic pet", "Only Beast Mastery hunters can tame it."),
        new(0x20000, "Use model collision", "Uses the model's collision size."),
        new(0x40000, "Interact in combat", "Allows interaction while in combat."),
        new(0x80000, "Collide with missiles", "Projectiles can collide with it."),
        new(0x100000, "Hide nameplate", "Does not show a nameplate."),
        new(0x200000, "No mounted animation", "Does not play mounted animations."),
        new(0x400000, "Link all", "Links all nearby creatures."),
        new(0x800000, "Interact only with creator", "Only its creator can interact with it."),
        new(0x1000000, "No unit event sounds", "Does not play unit-event sounds."),
        new(0x2000000, "No shadow blob", "Does not draw a shadow blob."),
        new(0x4000000, "Treat as raid unit", "Helpful spells treat it as a raid member."),
        new(0x8000000, "Force gossip", "Displays a single gossip option even if the flags would not."),
        new(0x10000000, "Never sheathe", "Keeps its weapons drawn."),
        new(0x20000000, "No target on interaction", "Does not target players who interact with it."),
        new(0x40000000, "Do not render name", "Does not render its object name."),
        new(0x80000000, "Quest boss", "Marks it as a quest boss.")
    ];

    /// <summary>CreatureTypeFlags2 (SharedDefines.h, master).</summary>
    public static readonly FlagBit[] TypeFlags2 =
    [
        new(0x1, "Predictive power regen", "The client predicts power regeneration."),
        new(0x2, "Hide level in tooltip", "Hides the level from the tooltip."),
        new(0x4, "Hide health bar", "Hides the health bar under the tooltip."),
        new(0x8, "No chat bubbles", "Never shows emote or chat text in a bubble."),
        new(0x10, "No death thud", "Does not play the death thud sound."),
        new(0x20, "No left-click interaction", "Left-clicking it does nothing.")
    ];

    /// <summary>CreatureFlagsExtra (CreatureData.h, master).</summary>
    public static readonly FlagBit[] ExtraFlags =
    [
        new(0x1, "Binds instance on kill", "Killing it locks the killer and their group to the instance."),
        new(0x2, "Civilian", "Never initiates combat, regardless of faction."),
        new(0x4, "Cannot parry", "Attacks against it cannot be parried."),
        new(0x8, "No parry haste", "Parries do not speed up its next attack."),
        new(0x10, "Cannot block", "Attacks against it cannot be blocked."),
        new(0x20, "No crushing blows", "Its attacks cannot be crushing blows."),
        new(0x40, "Gives no experience", "Killing it grants no XP."),
        new(0x80, "Trigger", "Invisible helper creature used by scripts."),
        new(0x100, "Immune to taunt", "Taunt effects do nothing to it."),
        new(0x400, "Ghost visibility", "Only visible to dead players."),
        new(0x800, "Uses off-hand attack", "Attacks with a second weapon."),
        new(0x1000, "Vendor will not buy", "Players cannot sell items to it."),
        new(0x2000, "Cannot enter combat", "Never fights."),
        new(0x4000, "World event", "Marks it as a world-event creature."),
        new(0x8000, "Guard", "Behaves as a city or town guard."),
        new(0x10000, "Ignores feign death", "Is not fooled by Feign Death."),
        new(0x20000, "Cannot critically strike", "Its attacks never crit."),
        new(0x40000, "No skill gains", "Fighting it does not train weapon skills."),
        new(0x80000, "Taunt diminishing returns", "Taunt is subject to diminishing returns."),
        new(0x100000, "All diminishing returns", "Subject to every player diminishing return."),
        new(0x200000, "No damage needed for credit", "Kill credit does not require the player to have damaged it."),
        new(0x10000000, "Dungeon boss", "Marks it as a dungeon boss."),
        new(0x20000000, "Ignores pathfinding", "Moves in straight lines instead of following the navigation mesh."),
        new(0x40000000, "Immune to knockback", "Cannot be knocked back.")
    ];

    static string Shown(string value) => float.Parse(value, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture);

    static string Gold(string copper)
    {
        uint value = uint.Parse(copper, CultureInfo.InvariantCulture);
        return value == 0 ? "None" : $"{value / 10000}g {value / 100 % 100}s {value % 100}c";
    }

    /// <summary>The difficulty ID with the names the scene sidebar uses for the common ones.</summary>
    public static string DifficultyName(int id) => id switch
    {
        0 => "Open world (0)",
        1 => "Normal dungeon (1)",
        2 => "Heroic dungeon (2)",
        14 => "Normal raid (14)",
        15 => "Heroic raid (15)",
        16 => "Mythic raid (16)",
        23 => "Mythic dungeon (23)",
        _ => $"Difficulty {id}"
    };

    public static readonly EntityPage Page = Build();

    static FieldSpec Text(string group, string column, string label, string tip, bool allowEmpty = false, Func<string, string>? display = null) =>
        new(group, TcTables.CreatureTemplate, column, label, tip, FieldKind.Text, AllowEmpty: allowEmpty, Display: display);

    static FieldSpec Whole(string group, string column, string label, string tip) => new(group, TcTables.CreatureTemplate, column, label, tip, FieldKind.Whole);

    static FieldSpec Decimal(string group, string column, string label, string tip) => new(group, TcTables.CreatureTemplate, column, label, tip, FieldKind.Decimal, Display: Shown);

    static FieldSpec Flags(string group, string column, string label, string tip, FlagBit[] bits) => new(group, TcTables.CreatureTemplate, column, label, tip, FieldKind.Flags, Flags: bits);

    static FieldSpec DWhole(string group, string column, string label, string tip, Func<string, string>? display = null) =>
        new(group, TcTables.CreatureDifficulty, column, label, tip, FieldKind.Whole, RowBinding.Difficulty, Display: display);

    static FieldSpec DDecimal(string group, string column, string label, string tip) =>
        new(group, TcTables.CreatureDifficulty, column, label, tip, FieldKind.Decimal, RowBinding.Difficulty, Display: Shown);

    static FieldSpec DFlags(string group, string column, string label, string tip, FlagBit[] bits) => new(group, TcTables.CreatureDifficulty, column, label, tip, FieldKind.Flags, RowBinding.Difficulty, Flags: bits);

    static EntityPage Build()
    {
        var fields = new List<FieldSpec>();
        fields.AddRange(IdentityFields());
        fields.AddRange(InteractionFields());
        fields.AddRange(CombatFields());
        fields.AddRange(MovementFields());
        fields.AddRange(LootFields());
        fields.AddRange(LinksFields());
        fields.AddRange(FlagFields());
        for (int i = 1; i <= 8; i++)
        {
            fields.Add(DFlags("flags", $"StaticFlags{i}", $"Static flags {i}", $"A per-difficulty bit mask of static creature flags (StaticFlags{i}). The editor has no names for these bits yet, so the number is edited directly.", []));
        }

        return new(EntityPage.CreatureGroups(flagsHint: "Tick the behaviours that apply. The raw value keeps every bit, including bits the list does not name."), fields.ToArray(),
            new DifficultySpec(TcTables.CreatureDifficulty, "Entry", "DifficultyID", DifficultyName, NewDifficulty, [0, 1, 2, 14, 15, 16, 23], byte.MaxValue));
    }

    static FieldSpec Choice(string group, string column, string label, string tip, (int, string)[] options) =>
        new(group, TcTables.CreatureTemplate, column, label, tip, FieldKind.Choice, Options: options);

    static FieldSpec Check(string group, string column, string label, string tip) =>
        new(group, TcTables.CreatureTemplate, column, label, tip, FieldKind.Check);

    /// <summary>Name, subname, cursor icon, classification, faction, class, type, family and trainer class.</summary>
    static FieldSpec[] IdentityFields() =>
    [
        Text("identity", "name", "Name", "The name shown above the creature in the world and in the content browser."),
        Text("identity", "subname", "Subname", "The text in angle brackets under the name, such as <Weapon Merchant>. Leave blank for no title.", true, s => s.Length > 0 ? s : "None"),
        Text("identity", "IconName", "Cursor icon", "The mouse cursor shown when hovering over the creature, such as Speak, Buy or Attack. Blank uses the default cursor for its services.", true, s => s.Length > 0 ? s : "Default"),
        new("identity", TcTables.CreatureTemplate, "Classification", "Classification", "Normal, Elite, Rare Elite, Rare and so on. It draws the dragon frame around the portrait and affects some combat and loot rules.", FieldKind.Choice, Options: Classifications),
        Whole("identity", "faction", "Faction", "The faction template that decides which players and creatures this one is friendly or hostile to. The picker names it after the reputation faction it belongs to."),
        Whole("identity", "unit_class", "Class", "The class whose class-level stats supply its health, mana and damage. Warrior, paladin, rogue and mage have stat rows."),
        Whole("identity", "type", "Creature type", "The creature's kind, such as beast, dragonkin, demon or undead. Some spells, abilities and quest objectives only work on certain types."),
        Whole("identity", "family", "Pet Family", "The hunter pet family it belongs to, such as wolf or cat. Setting one also marks it tameable; clearing it removes that flag."),
        Whole("identity", "trainer_class", "Trainer class", "The class this creature trains when it is a class trainer. Setting one also turns on the class-trainer NPC flag."),
        Check("identity", "RacialLeader", "Racial leader", "Marks the creature as the leader of its race, which affects some player titles and reputation rewards.")
    ];

    /// <summary>The 64-bit npcflag and the unit and type flag columns beside it.</summary>
    static FieldSpec[] InteractionFields() =>
    [
        new("interaction", TcTables.CreatureTemplate, "npcflag", "NPC flags", "What players can do by right-clicking: talk, buy, train, take quests and so on. The low 32 bits are NPCFlags and the high 32 bits are NPCFlags2; each ticked service needs the matching records on the other pages.", FieldKind.Flags, Flags: NpcServices),
        Flags("interaction", "unit_flags", "Unit flags", "Basic state and combat switches such as unattackable, unselectable, pacified, stunned, disarmed, mounted and immune to all damage.", UnitFlags),
        Flags("interaction", "unit_flags2", "Unit flags 2", "Additional state switches such as feigning death, hiding the model, ignoring reputation, blocking spell-click and fixing its facing.", UnitFlags2),
        Flags("interaction", "unit_flags3", "Unit flags 3", "Further state switches used by newer content, such as mounted combat, fake death and interaction while in combat.", UnitFlags3),
        DFlags("interaction", "TypeFlags", "Type flags", "Tags for the kind of creature: tameable, boss portrait, skinnable or gatherable corpse, hide nameplate and so on.", TypeFlags),
        DFlags("interaction", "TypeFlags2", "Type flags 2", "Further creature type tags such as hiding the health bar, no chat bubbles and no death thud.", TypeFlags2),
        DFlags("interaction", "TypeFlags3", "Type flags 3", "The newest creature type tags. The editor has no names for these bits yet, so the raw value is edited.", [])
    ];

    /// <summary>Attack timers and spreads, then the per-difficulty scaling and modifiers.</summary>
    static FieldSpec[] CombatFields() =>
    [
        Choice("combat", "dmgschool", "Damage type", "The school of magic the creature's melee attacks deal. Physical damage is reduced by armor, other schools by resistance.", DamageSchools),
        Whole("combat", "BaseAttackTime", "Melee attack interval", "Milliseconds between melee swings. Lower is faster; 2000 is the usual default."),
        Whole("combat", "RangeAttackTime", "Ranged attack interval", "Milliseconds between ranged attacks. Lower is faster; 2000 is the usual default."),
        Decimal("combat", "BaseVariance", "Melee damage spread", "Multiplies the attack-power part of a melee hit (attack power / 3.5). 1 is the normal spread; 0 removes that part."),
        Decimal("combat", "RangeVariance", "Ranged damage spread", "Multiplies the attack-power part of a ranged hit, like the melee damage spread. 1 is normal; 0 removes it."),
        Decimal("combat", "ExperienceModifier", "Experience multiplier", "Multiplies the experience a player earns for killing it. 1 is normal and 0 gives none."),
        Check("combat", "RegenHealth", "Regenerates health", "Whether it heals itself when out of combat. Turn off for creatures that must stay wounded."),
        Whole("combat", "CreatureImmunitiesId", "Immunities set", "The named immunity set that makes it immune to schools, mechanics, dispel types or effects. Zero means none."),
        DWhole("combat", "ContentTuningID", "Content tuning", "ContentTuning.db2 ID that sets the creature's level range and scales its health, damage and armor. 0 uses the map's or area's tuning."),
        DWhole("combat", "LevelScalingDeltaMin", "Level delta min", "Lowest level added to the content tuning's top level; it can be negative. The creature's level is rolled between this and the maximum."),
        DWhole("combat", "LevelScalingDeltaMax", "Level delta max", "Highest level added to the content tuning's top level. The creature's level is rolled between the minimum and this value."),
        DWhole("combat", "HealthScalingExpansion", "Health scaling expansion", "The expansion whose expected-stat table is used for health and damage. -1 means the current expansion."),
        DDecimal("combat", "HealthModifier", "Health modifier", "Multiplies the scaled maximum health. 1 keeps the table value; bosses and elites often use values above 1."),
        DDecimal("combat", "ManaModifier", "Mana modifier", "Multiplies the scaled maximum mana. It has no effect on a class without mana."),
        DDecimal("combat", "ArmorModifier", "Armor modifier", "Multiplies the scaled armor, which reduces the physical damage the creature takes."),
        DDecimal("combat", "DamageModifier", "Damage modifier", "Multiplies the scaled melee and ranged damage. 1 keeps the table value."),
        DWhole("combat", "CreatureDifficultyID", "Creature difficulty", "CreatureDifficulty.db2 ID that supplies difficulty-specific health, damage and other overrides. 0 means none.")
    ];

    /// <summary>Speeds, scale and the movement type.</summary>
    static FieldSpec[] MovementFields() =>
    [
        Decimal("movement", "speed_walk", "Walk speed", "Walk speed as a multiple of the normal rate. 1 is normal walking speed; the creature uses it while patrolling or wandering."),
        Decimal("movement", "speed_run", "Run speed", "Run speed as a multiple of the normal rate. 1 is normal; higher values make it chase faster."),
        Decimal("movement", "scale", "Scale", "Size multiplier for the model. 1 is the model's normal size; this does not change its hitbox or interaction range."),
        Whole("movement", "MovementType", "Movement type", "What it does when not fighting: 0 stands still, 1 wanders randomly around its spawn, 2 follows a waypoint path."),
        Whole("movement", "movementId", "Movement style", "Selects a movement style from the server's creature movement table, such as always walking or never swimming. 0 uses the default style.")
    ];

    /// <summary>The per-difficulty loot table IDs and the money range.</summary>
    static FieldSpec[] LootFields() =>
    [
        DWhole("loot", "LootID", "Loot", "The loot table rolled when the creature dies. It matches the entry of its creature_loot_template rows; 0 drops nothing."),
        DWhole("loot", "PickPocketLootID", "Pickpocket loot", "The loot table a rogue can steal with Pickpocket. 0 means it cannot be pickpocketed."),
        DWhole("loot", "SkinLootID", "Skinning loot", "The loot table gathered by skinning. 0 means it cannot be skinned."),
        DWhole("loot", "GoldMin", "Gold min (copper)", "The lowest amount of money it can drop, in copper (100 copper = 1 silver, 100 silver = 1 gold). 0 means no money.", Gold),
        DWhole("loot", "GoldMax", "Gold max (copper)", "The highest amount of money it can drop, in copper (100 copper = 1 silver, 100 silver = 1 gold). 0 means no money.", Gold)
    ];

    /// <summary>The vehicle, required expansion, AI name and C++ script.</summary>
    static FieldSpec[] LinksFields() =>
    [
        Whole("links", "VehicleId", "Vehicle", "The vehicle seat layout that lets players ride it. 0 means it is not a vehicle."),
        Whole("links", "RequiredExpansion", "Required expansion", "The expansion a player must own to see and interact with it. 0 means no expansion requirement."),
        Text("links", "AIName", "AI", "The server AI that runs it, for example SmartAI. Blank uses the default AI; SmartAI rules only run when this is exactly \"SmartAI\".", true, s => s.Length > 0 ? s : "None"),
        Text("links", "ScriptName", "C++ script", "Name of a compiled C++ script that drives the creature. A script replaces the normal AI, gossip, quests and SmartAI, so leave it blank unless the script exists on the server.", true, s => s.Length > 0 ? s : "None")
    ];

    /// <summary>The extra flags; StaticFlags 1-8 are added after these.</summary>
    static FieldSpec[] FlagFields() =>
    [
        Flags("flags", "flags_extra", "Extra flags", "Server rules such as civilian, guard, trigger, no experience, no parry or block, immune to taunt or knockback, and ignoring pathfinding.", ExtraFlags)
    ];

    /// <summary>A creature_template_difficulty row with the trinitycore.info defaults: modifiers 1, everything else 0.</summary>
    static Row NewDifficulty(uint entry, int id)
    {
        var row = new Row(TcTables.CreatureDifficulty.Columns.Select(c => new KeyValuePair<string, string>(c.Name, "0")));
        foreach (string modifier in new[] { "HealthModifier", "ManaModifier", "ArmorModifier", "DamageModifier" })
        {
            row = row.With(modifier, 1f);
        }

        return row.With("Entry", entry).With("DifficultyID", id);
    }
}
