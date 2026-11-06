using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>
/// The scaling columns of the picked creature_template_difficulty row, for the TrinityCore stats page:
/// what sets the creature's level and how its health, mana, armor and damage scale. They are the same
/// columns the Template page shows; the stats page puts them beside the values they produce.
/// </summary>
public static class TcStatsPage
{
    static string Shown(string value) => float.Parse(value, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture);

    public static readonly EntityPage Page = Build();

    static EntityPage Build()
    {
        var d = TcTables.CreatureDifficulty;
        FieldSpec Whole(string group, string column, string label, string tip) => new(group, d, column, label, tip, FieldKind.Whole, RowBinding.Difficulty);
        FieldSpec Decimal(string column, string label, string tip) => new("multipliers", d, column, label, tip, FieldKind.Decimal, RowBinding.Difficulty, Display: Shown);
        FieldSpec[] fields =
        [
            Whole("levels", "ContentTuningID", "Content tuning", "ContentTuning.db2 ID. Its top level, plus the deltas below, sets the creature's level, and its modifiers scale health, damage and armor. 0 takes the tuning from the map or area."),
            Whole("levels", "LevelScalingDeltaMin", "Level delta min", "The lowest level added to the content tuning's top level; it can be negative. The creature's level is rolled between this and the maximum."),
            Whole("levels", "LevelScalingDeltaMax", "Level delta max", "The highest level added to the content tuning's top level. The creature's level is rolled between the minimum and this value."),
            Whole("levels", "HealthScalingExpansion", "Health scaling expansion", "The expansion whose expected-stat table supplies health and damage. -1 means the current expansion."),
            Decimal("HealthModifier", "Health multiplier", "Multiplies the scaled maximum health. 1 keeps the table value; bosses and elites often use values above 1."),
            Decimal("ManaModifier", "Mana multiplier", "Multiplies the base mana for the creature's level and class. It has no effect on a class without mana."),
            Decimal("ArmorModifier", "Armor multiplier", "Multiplies the scaled armor, which reduces the physical damage the creature takes."),
            Decimal("DamageModifier", "Damage multiplier", "Multiplies the scaled weapon damage for melee and ranged attacks. 1 keeps the table value.")
        ];
        return new([new("levels", "Level and scaling"), new("multipliers", "Stat multipliers", "Each multiplies the value the client's expected-stat tables give for the creature's level and class. 1 keeps it.")],
            fields, TcCreaturePage.Page.Difficulty);
    }
}
