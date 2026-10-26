namespace ACWorldEdit.Foundation;

/// <summary>A creature's values at one level.</summary>
public sealed record TcStatLine(int Level, long Health, double Mana, double Armor, double DamageMin, double DamageMax, int AttackPower, int RangedAttackPower);

/// <summary>
/// What the server computes for a creature_template_difficulty row. <see cref="Low"/> and <see cref="High"/>
/// are null when the row has no ContentTuningID (the server then uses the map's or area's tuning,
/// which is not known here) or when the level cannot be resolved.
/// </summary>
public sealed record TcCreatureStats(uint ContentTuningId, int Expansion, int MinLevel, int MaxLevel, TcStatLine? Low, TcStatLine? High, string? Note);

/// <summary>
/// TrinityCore master's creature scaling, from DB2Manager::GetContentTuningData, EvaluateExpectedStat and
/// Creature::SelectLevel / UpdateLevelDependantStats. A creature's level is the content tuning's top
/// level plus a random delta; its health, armor and damage are the ExpectedStat row for that level and
/// the row's health-scaling expansion, scaled by the content tuning's and the class's modifiers and
/// then by the template's multipliers; its mana and attack power come from creature_classlevelstats.
/// The server's creature rates (Rate.Creature.*) are taken as 1, and Mythic+ seasons are not known, so a
/// modifier limited to start in a season is skipped and one limited to end in a season is kept.
/// </summary>
public static class TcStatCalculator
{
    /// <summary>The expansion the server runs (CURRENT_EXPANSION) and the level cap of the level fields.</summary>
    public const int CurrentExpansion = 11, MaxLevel = 123, StrongMaxLevel = 255;

    /// <summary>GetMaxLevelForExpansion from SharedDefines.h.</summary>
    public static int MaxLevelForExpansion(int expansion) => expansion switch
    {
        0 or 1 or 2 => 30,
        3 or 4 => 35,
        5 => 40,
        6 => 45,
        7 => 50,
        8 => 60,
        9 => 70,
        10 => 80,
        11 => 90,
        _ => 0
    };

    /// <summary>The expansion a row scales health with: -1 means the current one, and an invalid value is read as 0 (ObjectMgr::LoadCreatureTemplateDifficulty).</summary>
    public static int ScalingExpansion(int stored) => stored == -1 ? CurrentExpansion : stored is >= 0 and <= CurrentExpansion ? stored : 0;

    static int Adjustment(int type) => type switch
    {
        1 => 1,
        2 => MaxLevelForExpansion(CurrentExpansion),
        3 => MaxLevelForExpansion(Math.Max(CurrentExpansion - 1, 0)),
        _ => 0
    };

    /// <summary>The content tuning's level range: its base levels plus what the calculation type adds, within 1 to 123.</summary>
    public static (int Min, int Max) TuningLevels(TcContentTuning tuning) =>
        (Math.Clamp(tuning.MinLevel + Adjustment(tuning.MinLevelType), 1, MaxLevel), Math.Clamp(tuning.MaxLevel + Adjustment(tuning.MaxLevelType), 1, MaxLevel));

    /// <summary>The levels a creature of this row is spawned at: the tuning's top level plus the smaller to the larger level delta.</summary>
    public static (int Min, int Max)? CreatureLevels(TcContentTuning? tuning, int deltaMin, int deltaMax)
    {
        if (tuning == null)
        {
            return null;
        }

        int top = TuningLevels(tuning).Max;
        return (Math.Clamp(top + Math.Min(deltaMin, deltaMax), 1, StrongMaxLevel), Math.Clamp(top + Math.Max(deltaMin, deltaMax), 1, StrongMaxLevel));
    }

    /// <summary>The ExpectedStatMod row used for a unit class (1 warrior, 2 paladin, 4 rogue, 8 mage): rows 4, 2, 3 and 1.</summary>
    public static uint? ClassModRow(uint unitClass) => unitClass switch { 1 => 4, 2 => 2, 4 => 3, 8 => 1, _ => null };

    /// <summary>
    /// Compute a difficulty row's values at its lowest and highest level. unitClass is creature_template.unit_class;
    /// classStats looks up creature_classlevelstats by level and class.
    /// </summary>
    public static TcCreatureStats Compute(Row difficulty, uint unitClass, ITcStatData data, Func<uint, uint, TcClassLevelStats?> classStats)
    {
        uint tuningId = (uint)Math.Max(difficulty.I("ContentTuningID"), 0);
        int expansion = ScalingExpansion(difficulty.I("HealthScalingExpansion"));
        if (difficulty.I("ContentTuningID") <= 0)
        {
            return new(0, expansion, 0, 0, null, null, "No content tuning is set, so the server takes the level and scaling from the map or area the creature spawns in.");
        }

        var tuning = data.ContentTuning(tuningId);
        if (tuning == null)
        {
            return new(tuningId, expansion, 0, 0, null, null, $"Content tuning {tuningId} is not in the client's ContentTuning table.");
        }

        var (min, max) = CreatureLevels(tuning, difficulty.I("LevelScalingDeltaMin"), difficulty.I("LevelScalingDeltaMax"))!.Value;
        var mods = data.ContentTuningMods(tuningId).Where(m => m.MinMythicPlusSeasonId == 0).ToArray();
        var classMod = ClassModRow(unitClass) is { } row ? data.ExpectedStatMod(row) : null;
        TcStatLine? At(int level)
        {
            var stat = data.ExpectedStat((uint)level, expansion) ?? data.ExpectedStat((uint)level, -2);
            double Scale(double value, Func<TcExpectedStatMod, float> pick)
            {
                foreach (var mod in mods)
                {
                    value *= pick(mod.Mod);
                }

                return classMod == null ? value : value * pick(classMod);
            }

            // EvaluateExpectedStat answers 1 for a level with no row.
            double health = stat == null ? 1 : Scale(stat.CreatureHealth, m => m.CreatureHealthMod);
            double dps = stat == null ? 1 : Scale(stat.CreatureAutoAttackDps, m => m.CreatureAutoAttackDpsMod);
            double armor = stat == null ? 1 : Scale(stat.CreatureArmor, m => m.CreatureArmorMod);
            var baseStats = classStats((uint)level, unitClass);
            double damage = dps * difficulty.F("DamageModifier");
            return new(level, (long)Math.Ceiling(health * difficulty.F("HealthModifier")), (baseStats?.BaseMana ?? 0) * (double)difficulty.F("ManaModifier"),
                armor * difficulty.F("ArmorModifier"), damage, damage * 1.5, baseStats?.AttackPower ?? 0, baseStats?.RangedAttackPower ?? 0);
        }

        bool known = data.ExpectedStat((uint)max, expansion) != null || data.ExpectedStat((uint)max, -2) != null;
        return new(tuningId, expansion, min, max, At(min), At(max), known ? null : $"The client has no expected stats for level {max}, so the server computes 1 for health, damage and armor.");
    }
}
