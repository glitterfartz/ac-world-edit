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

    static readonly FlagBit[] NpcServices = TcCreatureView.NpcFlagBits.Select(f => new FlagBit(f.Bit, f.Name)).ToArray();

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

    static FieldSpec Flags(string column, string label, string tip) => new("flags", TcTables.CreatureTemplate, column, label, tip, FieldKind.Flags, Flags: []);

    static FieldSpec DWhole(string group, string column, string label, string tip, Func<string, string>? display = null) =>
        new(group, TcTables.CreatureDifficulty, column, label, tip, FieldKind.Whole, RowBinding.Difficulty, Display: display);

    static FieldSpec DDecimal(string group, string column, string label, string tip) =>
        new(group, TcTables.CreatureDifficulty, column, label, tip, FieldKind.Decimal, RowBinding.Difficulty, Display: Shown);

    static FieldSpec DFlags(string column, string label, string tip) => new("flags", TcTables.CreatureDifficulty, column, label, tip, FieldKind.Flags, RowBinding.Difficulty, Flags: []);

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
            fields.Add(DFlags($"StaticFlags{i}", $"Static flags {i}", $"A per-difficulty bit mask of static creature flags (StaticFlags{i}). The editor has no names for these bits yet, so the number is edited directly."));
        }

        return new(EntityPage.CreatureGroups(flagsHint: "Tick the behaviours that apply. The raw value keeps every bit, including bits the list does not name."), fields.ToArray(),
            new DifficultySpec(TcTables.CreatureDifficulty, "Entry", "DifficultyID", DifficultyName, NewDifficulty, [0, 1, 2, 14, 15, 16, 23], byte.MaxValue));
    }

    /// <summary>Name, subname, cursor icon, classification, faction, class, type, family and trainer class.</summary>
    static FieldSpec[] IdentityFields() =>
    [
        Text("identity", "name", "Name", "The name shown above the creature in the world and in the content browser."),
        Text("identity", "subname", "Subname", "The text in angle brackets under the name, such as <Weapon Merchant>. Leave blank for no title.", true, s => s.Length > 0 ? s : "None"),
        Text("identity", "IconName", "Cursor icon", "The mouse cursor shown when hovering over the creature, such as Speak, Buy or Attack. Blank uses the default cursor for its services.", true, s => s.Length > 0 ? s : "Default"),
        new("identity", TcTables.CreatureTemplate, "Classification", "Classification", "Normal, Elite, Rare Elite, Rare and so on. It draws the dragon frame around the portrait and affects some combat and loot rules.", FieldKind.Choice, Options: Classifications),
        Whole("identity", "faction", "Faction", "FactionTemplate.db2 ID that decides which players and creatures this one is friendly or hostile to. 35 is friendly to everyone; 14 is hostile to everyone."),
        Whole("identity", "unit_class", "Class", "The unit class whose base health, mana and damage rows supply its stats. Only 1 warrior, 2 paladin, 4 rogue and 8 mage have rows."),
        Whole("identity", "type", "Creature type", "The creature's kind, such as beast, dragonkin, demon or undead. Some spells, abilities and quest objectives only work on certain types."),
        Whole("identity", "family", "Family", "The hunter pet family it belongs to, such as wolf or cat. Only matters for tameable beasts."),
        Whole("identity", "trainer_class", "Trainer class", "The class this creature trains when it is a class trainer. 0 means it is not a class trainer.")
    ];

    /// <summary>The 64-bit npcflag: low half NPCFlags, high half NPCFlags2.</summary>
    static FieldSpec[] InteractionFields() =>
    [
        new("interaction", TcTables.CreatureTemplate, "npcflag", "NPC flags", "What players can do by right-clicking: talk, buy, train, take quests and so on. The low 32 bits are NPCFlags and the high 32 bits are NPCFlags2; each ticked service needs the matching records on the other pages.", FieldKind.Flags, Flags: NpcServices)
    ];

    /// <summary>Attack timers and spreads, then the per-difficulty scaling and modifiers.</summary>
    static FieldSpec[] CombatFields() =>
    [
        Whole("combat", "BaseAttackTime", "Melee attack interval", "Milliseconds between melee swings. Lower is faster; 2000 is the usual default."),
        Whole("combat", "RangeAttackTime", "Ranged attack interval", "Milliseconds between ranged attacks. Lower is faster; 2000 is the usual default."),
        Decimal("combat", "BaseVariance", "Melee damage spread", "Multiplies the attack-power part of a melee hit (attack power / 3.5). 1 is the normal spread; 0 removes that part."),
        Decimal("combat", "RangeVariance", "Ranged damage spread", "Multiplies the attack-power part of a ranged hit, like the melee damage spread. 1 is normal; 0 removes it."),
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
        Whole("movement", "MovementType", "Movement type", "What it does when not fighting: 0 stands still, 1 wanders randomly around its spawn, 2 follows a waypoint path.")
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

    /// <summary>The unit, extra and per-difficulty type flags; StaticFlags 1-8 are added after these.</summary>
    static FieldSpec[] FlagFields() =>
    [
        Flags("unit_flags", "Unit flags", "Basic state and combat switches such as unattackable, unselectable, pacified, stunned, disarmed, mounted and immune to all damage."),
        Flags("unit_flags2", "Unit flags 2", "Additional state switches such as feigning death, hiding the model, ignoring reputation, blocking spell-click and fixing its facing."),
        Flags("unit_flags3", "Unit flags 3", "Further state switches used by newer content. The raw value keeps every bit; the editor has no names for these bits yet."),
        Flags("flags_extra", "Extra flags", "Server rules such as civilian, guard, trigger, no experience, no parry or block, immune to taunt or knockback, and ignoring pathfinding."),
        DFlags("TypeFlags", "Type flags", "Tags for the kind of creature: tameable, boss portrait, skinnable or gatherable corpse, hide nameplate and so on."),
        DFlags("TypeFlags2", "Type flags 2", "More creature type tags used by newer content. The raw value keeps every bit."),
        DFlags("TypeFlags3", "Type flags 3", "Further creature type tags used by newer content. The raw value keeps every bit.")
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
