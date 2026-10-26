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

    public static readonly CreaturePage Page = Build();

    static CreaturePage Build()
    {
        var d = TcTables.CreatureDifficulty;
        FieldSpec Whole(string group, string column, string label, string tip) => new(group, d, column, label, tip, FieldKind.Whole, RowBinding.Difficulty);
        FieldSpec Decimal(string column, string label, string tip) => new("multipliers", d, column, label, tip, FieldKind.Decimal, RowBinding.Difficulty, Display: Shown);
        FieldSpec[] fields =
        [
            Whole("levels", "ContentTuningID", "Content tuning", "ContentTuning ID. Its top level, plus the deltas below, sets the creature's level, and its modifiers scale health, damage and armor. 0 takes the tuning from the map or area."),
            Whole("levels", "LevelScalingDeltaMin", "Level delta min", "Lowest level added to the content tuning's top level (can be negative)."),
            Whole("levels", "LevelScalingDeltaMax", "Level delta max", "Highest level added to the content tuning's top level. The creature's level is rolled between the two."),
            Whole("levels", "HealthScalingExpansion", "Health scaling expansion", "Expansion whose expected-stat table is used. -1 means the current expansion."),
            Decimal("HealthModifier", "Health multiplier", "Multiplies the scaled health."),
            Decimal("ManaModifier", "Mana multiplier", "Multiplies the base mana of the level and class."),
            Decimal("ArmorModifier", "Armor multiplier", "Multiplies the scaled armor."),
            Decimal("DamageModifier", "Damage multiplier", "Multiplies the scaled weapon damage.")
        ];
        return new([new("levels", "Level and scaling"), new("multipliers", "Stat multipliers", "Each multiplies the value the client's expected-stat tables give for the creature's level and class. 1 keeps it.")],
            fields, TcCreaturePage.Page.Difficulty);
    }
}
