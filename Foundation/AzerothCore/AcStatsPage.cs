using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>
/// The level, class and scaling columns of an AzerothCore creature, for the stats page: what sets the
/// level range and which class and expansion tier of creature_classlevelstats supplies the base values, and
/// the multipliers on them. Same shape as <see cref="TcStatsPage"/>, so one renderer draws either.
/// </summary>
public static class AcStatsPage
{
    /// <summary>The unit classes the class stats table has rows for (unit_class 1, 2, 4 and 8).</summary>
    public static readonly (int Value, string Name)[] Classes = [(1, "Warrior"), (2, "Paladin"), (4, "Rogue"), (8, "Mage")];

    /// <summary>creature_template.exp: the column of the class stats table used for health and damage.</summary>
    public static readonly string[] Tiers = ["Classic", "Burning Crusade", "Wrath of the Lich King"];

    public const uint MaxLevel = 255;

    static string Shown(string value) => float.Parse(value, CultureInfo.InvariantCulture).ToString("0.##", CultureInfo.InvariantCulture);

    public static readonly EntityPage Page = Build();

    static EntityPage Build()
    {
        var s = AcTables.CreatureStats;
        FieldSpec Whole(string column, string label, string tip) => new("levels", s, column, label, tip, FieldKind.Whole, Max: MaxLevel);
        FieldSpec Decimal(string column, string label, string tip) => new("multipliers", s, column, label, tip, FieldKind.Decimal, Display: Shown);
        FieldSpec[] fields =
        [
            new("levels", s, "unit_class", "Class", "Decides which row of the class stats table supplies base health, mana, armor and attributes. Only Warrior (no mana), Paladin (health and mana), Rogue (no mana, more damage) and Mage (mana, less health) have rows.", FieldKind.Choice, Options: Classes),
            Whole("minlevel", "Lowest level", "The level range is rolled when the creature spawns. Each spawn gets a random level from the lowest to the highest value. Set both equal for a fixed level."),
            Whole("maxlevel", "Highest level", "Upper end of the level range. Level 63 is the top of Classic-era scaling and 80 the cap in Wrath; values above 80 only work if the class stats table has rows for them."),
            new("levels", s, "exp", "Expansion tier", "Picks which health and damage column of the class stats table is used, so the same level can be tuned for Classic, Burning Crusade or Wrath content.", FieldKind.Choice, Options: Tiers.Select((name, i) => (i, name)).ToArray()),
            Decimal("HealthModifier", "Health multiplier", "Scales the creature's maximum health. Bosses and elites usually use values well above 1."),
            Decimal("ManaModifier", "Mana multiplier", "Scales the creature's maximum mana. It has no effect on classes that have no mana."),
            Decimal("ArmorModifier", "Armor multiplier", "Scales the creature's armor, which reduces the physical damage it takes."),
            Decimal("DamageModifier", "Damage multiplier", "Scales the damage of the creature's melee and ranged attacks.")
        ];
        return new([new("levels", "Class and level"), new("multipliers", "Stat multipliers", "Each multiplies the base value for the creature's level and class. 1 keeps the table value, 2 doubles it.")], fields);
    }

    /// <summary>The stats row's rules: levels from 1 to 255 with the lowest not above the highest, a tier of 0 to 2, multipliers of zero or more.</summary>
    public static IEnumerable<InteractionIssue> Check(RowSet rows, uint entry)
    {
        if (rows.Get(AcTables.CreatureStats, entry.ToString(CultureInfo.InvariantCulture)) is not { } stats)
        {
            yield break;
        }

        uint min = stats.U("minlevel"), max = stats.U("maxlevel");
        if (min is < 1 or > MaxLevel || max is < 1 or > MaxLevel)
        {
            yield return new(true, $"Creature {entry}: levels must be between 1 and {MaxLevel}.");
        }
        else if (min > max)
        {
            yield return new(true, $"Creature {entry}: the lowest level cannot exceed the highest.");
        }

        if (stats.U("exp") > 2)
        {
            yield return new(true, $"Creature {entry}: the expansion tier must be 0 (Classic), 1 (Burning Crusade) or 2 (Wrath).");
        }

        foreach (string column in new[] { "HealthModifier", "ManaModifier", "ArmorModifier", "DamageModifier" })
        {
            if (!float.IsFinite(stats.F(column)) || stats.F(column) < 0)
            {
                yield return new(true, $"Creature {entry}: a multiplier must be zero or more ({column}).");
            }
        }
    }
}
