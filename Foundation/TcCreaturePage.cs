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

    public static readonly CreaturePage Page = Build();

    static CreaturePage Build()
    {
        var t = TcTables.CreatureTemplate;
        var d = TcTables.CreatureDifficulty;
        FieldSpec Text(string group, string column, string label, string tip, bool allowEmpty = false, Func<string, string>? display = null) =>
            new(group, t, column, label, tip, FieldKind.Text, AllowEmpty: allowEmpty, Display: display);
        FieldSpec Whole(string group, string column, string label, string tip) => new(group, t, column, label, tip, FieldKind.Whole);
        FieldSpec Decimal(string group, string column, string label, string tip) => new(group, t, column, label, tip, FieldKind.Decimal, Display: Shown);
        FieldSpec Flags(string column, string label, string tip) => new("flags", t, column, label, tip, FieldKind.Flags, Flags: []);
        FieldSpec DWhole(string group, string column, string label, string tip, Func<string, string>? display = null) =>
            new(group, d, column, label, tip, FieldKind.Whole, RowBinding.Difficulty, Display: display);
        FieldSpec DDecimal(string group, string column, string label, string tip) =>
            new(group, d, column, label, tip, FieldKind.Decimal, RowBinding.Difficulty, Display: Shown);
        FieldSpec DFlags(string column, string label, string tip) => new("flags", d, column, label, tip, FieldKind.Flags, RowBinding.Difficulty, Flags: []);

        var fields = new List<FieldSpec>
        {
            Text("identity", "name", "Name", "The name players see above the creature."),
            Text("identity", "subname", "Subname", "The text under the name. Leave blank for none.", true, s => s.Length > 0 ? s : "None"),
            Text("identity", "IconName", "Cursor icon", "Name of the cursor icon shown when hovering over the creature.", true, s => s.Length > 0 ? s : "Default"),
            new("identity", t, "Classification", "Classification", "Normal, Elite, Rare Elite and so on. Mostly visual.", FieldKind.Choice, Options: Classifications),
            Whole("identity", "faction", "Faction", "Faction template ID. Decides who the creature is friendly or hostile to."),
            Whole("identity", "unit_class", "Class", "Unit class (1 warrior, 2 paladin, 4 rogue, 8 mage)."),
            Whole("identity", "type", "Creature type", "The creature's kind (creature_template.type)."),
            Whole("identity", "family", "Family", "Hunter pet family."),
            Whole("identity", "trainer_class", "Trainer class", "The class this creature trains, for class trainers."),

            new("interaction", t, "npcflag", "NPC flags", "What players can do by right-clicking. The low 32 bits are NPCFlags, the high 32 bits NPCFlags2.", FieldKind.Flags, Flags: NpcServices),

            DWhole("combat", "ContentTuningID", "Content tuning", "ContentTuning ID that scales level and health."),
            DWhole("combat", "HealthScalingExpansion", "Health scaling expansion", "Expansion whose health scaling applies."),
            DDecimal("combat", "HealthModifier", "Health modifier", "Multiplies the scaled health."),
            DDecimal("combat", "ManaModifier", "Mana modifier", "Multiplies the scaled mana."),
            DDecimal("combat", "ArmorModifier", "Armor modifier", "Multiplies the scaled armor."),
            DDecimal("combat", "DamageModifier", "Damage modifier", "Multiplies the scaled damage."),
            DWhole("combat", "CreatureDifficultyID", "Creature difficulty", "CreatureDifficulty.db2 ID."),

            Decimal("movement", "speed_walk", "Walk speed", "Speed when walking."),
            Decimal("movement", "speed_run", "Run speed", "Speed when running."),
            Decimal("movement", "scale", "Scale", "Size multiplier."),
            Whole("movement", "MovementType", "Movement type", "0 stands still, 1 wanders randomly, 2 follows waypoints."),

            DWhole("loot", "LootID", "Loot", "ID of the loot table rolled when the creature dies."),
            DWhole("loot", "PickPocketLootID", "Pickpocket loot", "ID of the loot table a rogue can steal with Pickpocket."),
            DWhole("loot", "SkinLootID", "Skinning loot", "ID of the loot table gathered by skinning."),
            DWhole("loot", "GoldMin", "Gold min (copper)", "Lowest amount of money it can drop, in copper.", Gold),
            DWhole("loot", "GoldMax", "Gold max (copper)", "Highest amount of money it can drop, in copper.", Gold),

            Whole("links", "VehicleId", "Vehicle", "ID of the vehicle seat layout."),
            Whole("links", "RequiredExpansion", "Required expansion", "Expansion a player needs to interact."),
            Text("links", "AIName", "AI", "Core AI (for example SmartAI). Blank for none.", true, s => s.Length > 0 ? s : "None"),
            Text("links", "ScriptName", "C++ script", "Name of a compiled server script that drives the creature.", true, s => s.Length > 0 ? s : "None"),

            Flags("unit_flags", "Unit flags", "Basic state and combat switches."),
            Flags("unit_flags2", "Unit flags 2", "Additional state switches."),
            Flags("unit_flags3", "Unit flags 3", "Further state switches."),
            Flags("flags_extra", "Extra flags", "Special rules the server applies to it."),
            DFlags("TypeFlags", "Type flags", "Tags on the kind of creature."),
            DFlags("TypeFlags2", "Type flags 2", "More creature type tags."),
            DFlags("TypeFlags3", "Type flags 3", "More creature type tags.")
        };

        for (int i = 1; i <= 8; i++)
        {
            fields.Add(DFlags($"StaticFlags{i}", $"Static flags {i}", "Static creature flags."));
        }

        return new(CreaturePage.StandardGroups(flagsHint: "The raw value keeps every bit; named bits come once they are checked against TrinityCore."), fields.ToArray(),
            new DifficultySpec(d, "Entry", "DifficultyID", DifficultyName, NewDifficulty, [0, 1, 2, 14, 15, 16, 23], byte.MaxValue));
    }

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
