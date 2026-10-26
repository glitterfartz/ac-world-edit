namespace ACWorldEdit.Foundation;

/// <summary>
/// A ContentTuning.db2 row as TrinityCore reads it (ContentTuningEntry): the base levels with the
/// calculation type added on top, and the deltas to the target levels. Names are TrinityCore's; the
/// WoWDBDefs columns are MinLevelSquish, MaxLevelSquish, Min/MaxLevelScalingOffset and
/// AllowedMin/MaxOffset, in that order.
/// </summary>
public sealed record TcContentTuning(uint Id, int Flags, int MinLevel, int MaxLevel, int MinLevelType, int MaxLevelType, int TargetLevelDelta, int TargetLevelMaxDelta);

/// <summary>The creature columns of one ExpectedStat.db2 row (a level of one expansion).</summary>
public sealed record TcExpectedStat(float CreatureHealth, float CreatureAutoAttackDps, float CreatureArmor);

/// <summary>The creature multipliers of an ExpectedStatMod.db2 row.</summary>
public sealed record TcExpectedStatMod(float CreatureHealthMod, float CreatureAutoAttackDpsMod, float CreatureArmorMod);

/// <summary>A ContentTuningXExpected.db2 row: a stat modifier that applies to a content tuning, optionally within Mythic+ seasons.</summary>
public sealed record TcContentTuningMod(int MinMythicPlusSeasonId, int MaxMythicPlusSeasonId, TcExpectedStatMod Mod);

/// <summary>The client tables behind TrinityCore's creature scaling. Served by the retail client's DB2 files.</summary>
public interface ITcStatData
{
    TcContentTuning? ContentTuning(uint id);

    /// <summary>The ExpectedStat row for a level and expansion, or null.</summary>
    TcExpectedStat? ExpectedStat(uint level, int expansion);

    /// <summary>The ExpectedStatMod row with this ID (the class modifiers are rows 1 to 4), or null.</summary>
    TcExpectedStatMod? ExpectedStatMod(uint id);

    IReadOnlyList<TcContentTuningMod> ContentTuningMods(uint contentTuningId);
}

/// <summary>One creature_classlevelstats row of the TrinityCore world database.</summary>
public sealed record TcClassLevelStats(uint BaseMana, int AttackPower, int RangedAttackPower);
