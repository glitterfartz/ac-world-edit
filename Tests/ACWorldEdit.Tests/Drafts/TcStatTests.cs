using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class TcStatTests
{
    sealed class Data : ITcStatData
    {
        public Dictionary<uint, TcContentTuning> Tunings { get; } = [];
        public Dictionary<(uint, int), TcExpectedStat> Stats { get; } = [];
        public Dictionary<uint, TcExpectedStatMod> Mods { get; } = [];
        public Dictionary<uint, List<TcContentTuningMod>> TuningMods { get; } = [];

        public TcContentTuning? ContentTuning(uint id) => Tunings.GetValueOrDefault(id);
        public TcExpectedStat? ExpectedStat(uint level, int expansion) => Stats.GetValueOrDefault((level, expansion));
        public TcExpectedStatMod? ExpectedStatMod(uint id) => Mods.GetValueOrDefault(id);
        public IReadOnlyList<TcContentTuningMod> ContentTuningMods(uint contentTuningId) => TuningMods.GetValueOrDefault(contentTuningId) ?? [];
        public Dictionary<int, int> Seasons { get; } = [];
        public Dictionary<(uint, int), uint> MapTunings { get; } = [];
        public Dictionary<uint, uint> AreaTunings { get; } = [];

        public int? MilestoneSeason(int mythicPlusSeasonId) => Seasons.TryGetValue(mythicPlusSeasonId, out int milestone) ? milestone : null;
        public uint MapDifficultyTuning(uint mapId, int difficultyId) => MapTunings.GetValueOrDefault((mapId, difficultyId));
        public uint AreaTuning(uint areaId) => AreaTunings.GetValueOrDefault(areaId);
    }

    // Content tuning 100 runs from level 1 to the current expansion's cap (type 2 adds 90).
    static Data Tables()
    {
        var data = new Data();
        data.Tunings[100] = new(100, 0, 1, 0, 0, 2, 0, 0);
        data.Stats[(90, 11)] = new(1000, 10, 500);
        data.Mods[4] = new(2, 1, 1);
        data.TuningMods[100] = [new(0, 0, new(1.5f, 2, 1)), new(3, 0, new(100, 100, 100))];
        data.Seasons[3] = 1;
        return data;
    }

    static Row Difficulty(Action<List<(string, object)>>? change = null)
    {
        var values = new List<(string, object)>
        {
            ("Entry", 7u), ("DifficultyID", 0), ("LevelScalingDeltaMin", 0), ("LevelScalingDeltaMax", 0), ("ContentTuningID", 100), ("HealthScalingExpansion", 11),
            ("HealthModifier", 2f), ("ManaModifier", 2f), ("ArmorModifier", 0.5f), ("DamageModifier", 3f)
        };
        change?.Invoke(values);
        return Row.Of(values.ToArray());
    }

    static TcClassLevelStats? Base(uint level, uint unitClass) => level == 90 && unitClass == 1 ? new(200, 30, 40) : null;

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(1, 0, 2, 1)]
    [InlineData(2, 0, 91, 1)]
    [InlineData(3, 0, 81, 1)]
    [InlineData(2, 100, 123, 1)]
    public void TheTuningTopLevelAddsWhatItsCalculationTypeSays(int maxType, int maxLevel, int expectedMax, int expectedMin)
    {
        var (min, max) = TcStatCalculator.TuningLevels(new(1, 0, 1, maxLevel + 1, 0, maxType, 0, 0));
        Assert.Equal(expectedMin, min);
        Assert.Equal(expectedMax, max);
    }

    [Fact]
    public void CreatureLevelsAddTheSmallerToTheLargerDeltaToTheTopLevel()
    {
        var tuning = Tables().Tunings[100];
        Assert.Equal((88, 92), TcStatCalculator.CreatureLevels(tuning, 2, -2));
        // The tuning's top level is capped at 123 and the creature's level at 255.
        Assert.Equal((1, 163), TcStatCalculator.CreatureLevels(new(1, 0, 1, 123, 0, 0, 0, 0), -500, 40));
        Assert.Null(TcStatCalculator.CreatureLevels(null, 0, 0));
    }

    [Fact]
    public void StatsScaleExpectedValuesByTuningClassAndTemplateMultipliers()
    {
        var stats = TcStatCalculator.Compute(Difficulty(), 1, Tables(), Base);
        Assert.Null(stats.Note);
        Assert.Equal((90, 90), (stats.MinLevel, stats.MaxLevel));
        var line = stats.High!;
        // Expected 1000 x tuning 1.5 (the Mythic+ limited 100 is skipped) x warrior 2 x HealthModifier 2.
        Assert.Equal(6000, line.Health);
        Assert.Equal(250, line.Armor);
        // 10 x tuning 2 x DamageModifier 3; the maximum is 1.5 times the minimum.
        Assert.Equal(60, line.DamageMin);
        Assert.Equal(90, line.DamageMax);
        Assert.Equal(400, line.Mana);
        Assert.Equal((30, 40), (line.AttackPower, line.RangedAttackPower));
    }

    [Fact]
    public void AModLimitedToASeasonAppliesOnlyWhileTheActiveSeasonIsInRange()
    {
        // The server evaluates creatures with milestone season 0: a mod that starts at season 1 or later is skipped,
        // a mod that ends at a season above 0 still applies, and one that ends at season 0 does not; an unknown season is ignored.
        var data = Tables();
        data.TuningMods[100] = [new(3, 0, new(100, 100, 100)), new(0, 4, new(2, 1, 1)), new(0, 5, new(100, 1, 1)), new(0, 9, new(3, 1, 1)), new(8, 0, new(5, 1, 1))];
        data.Seasons[4] = 2;
        data.Seasons[5] = 0;
        var line = TcStatCalculator.Compute(Difficulty(), 8, data, Base).High!;
        // 1000 x 2 (ends at season 2) x 3 (unknown end season) x 5 (unknown start season) x HealthModifier 2
        Assert.Equal(60000, line.Health);
    }

    [Fact]
    public void ATemplateWithoutATuningTakesTheMapDifficultysThenTheAreasUnlessItIsACritter()
    {
        var data = Tables();
        data.MapTunings[(0, 0)] = 100;
        data.AreaTunings[12] = 100;
        var row = Difficulty(v => v.Add(("ContentTuningID", 0)));
        var byMap = TcStatCalculator.Compute(row, new TcStatTemplate(1), data, Base, new(0, 0, 0));
        Assert.Equal(100u, byMap.ContentTuningId);
        Assert.Contains("map's difficulty tuning 100", byMap.Note);
        Assert.Equal(6000, byMap.High!.Health);
        data.MapTunings.Clear();
        var byArea = TcStatCalculator.Compute(row, new TcStatTemplate(1), data, Base, new(0, 0, 12));
        Assert.Contains("area's tuning 100", byArea.Note);
        Assert.Equal(6000, byArea.High!.Health);
        Assert.Null(TcStatCalculator.Compute(row, new TcStatTemplate(1, Critter: true), data, Base, new(0, 0, 12)).Low);
        Assert.Null(TcStatCalculator.Compute(row, new TcStatTemplate(1), data, Base, new(0, 0, 99)).Low);
        Assert.Null(TcStatCalculator.Compute(row, new TcStatTemplate(1), data, Base).Low);
    }

    [Fact]
    public void HealthIsMultipliedByTheRateOfTheClassificationAndTruncated()
    {
        var rates = new TcStatRates([1, 2.5f, 1, 1, 1, 1, 1]);
        Assert.Equal(6000, TcStatCalculator.Compute(Difficulty(), new TcStatTemplate(1), Tables(), Base, null, rates).High!.Health);
        Assert.Equal(15000, TcStatCalculator.Compute(Difficulty(), new TcStatTemplate(1, Classification: 1), Tables(), Base, null, rates).High!.Health);
        // An unknown classification uses the Elite rate, like the server.
        Assert.Equal(15000, TcStatCalculator.Compute(Difficulty(), new TcStatTemplate(1, Classification: 42), Tables(), Base, null, rates).High!.Health);
    }

    [Fact]
    public void ASwingAddsAttackPowerAndScalesBySwingTime()
    {
        var template = new TcStatTemplate(1, BaseAttackTime: 1500, RangeAttackTime: 2000, BaseVariance: 2, RangeVariance: 0.5f);
        var line = TcStatCalculator.Compute(Difficulty(), template, Tables(), Base).High!;
        // Weapon damage 20 (10 x tuning 2); attack power 30 / 3.5 x variance 2; DamageModifier 3; 1.5 s.
        Assert.Equal((20 + 30 / 3.5 * 2) * 3 * 1.5, line.Melee.Min, 6);
        Assert.Equal((30 + 30 / 3.5 * 2) * 3 * 1.5, line.Melee.Max, 6);
        // Ranged attack power 40, variance 0.5, 2 s.
        Assert.Equal((20 + 40 / 3.5 * 0.5) * 3 * 2, line.Ranged.Min, 6);
    }

    [Fact]
    public void ALevelWithoutAnExpansionRowFallsBackToTheAnyExpansionRow()
    {
        var data = Tables();
        data.Stats.Remove((90, 11));
        data.Stats[(90, -2)] = new(400, 4, 40);
        var line = TcStatCalculator.Compute(Difficulty(), 8, data, Base).High!;
        Assert.Equal(400 * 1.5 * 2, line.Health);
    }

    [Fact]
    public void ALevelWithNoExpectedStatsComputesOneAndSaysSo()
    {
        var data = Tables();
        data.Stats.Clear();
        var stats = TcStatCalculator.Compute(Difficulty(), 8, data, Base);
        Assert.Contains("no expected stats", stats.Note);
        Assert.Equal(2, stats.High!.Health);
    }

    [Fact]
    public void WithoutATuningTheLevelsAreNotKnown()
    {
        var none = TcStatCalculator.Compute(Difficulty(v => v.Add(("ContentTuningID", 0))), 1, Tables(), Base);
        Assert.Null(none.Low);
        Assert.Contains("map or area", none.Note);
        var missing = TcStatCalculator.Compute(Difficulty(v => v.Add(("ContentTuningID", 999))), 1, Tables(), Base);
        Assert.Null(missing.High);
        Assert.Contains("999", missing.Note);
    }

    [Theory]
    [InlineData(-1, 11)]
    [InlineData(0, 0)]
    [InlineData(11, 11)]
    [InlineData(12, 0)]
    [InlineData(-2, 0)]
    public void TheScalingExpansionFollowsTheServersReading(int stored, int used) => Assert.Equal(used, TcStatCalculator.ScalingExpansion(stored));

    [Theory]
    [InlineData(1u, 4u)]
    [InlineData(2u, 2u)]
    [InlineData(4u, 3u)]
    [InlineData(8u, 1u)]
    public void UnitClassesPickTheirModifierRow(uint unitClass, uint row) => Assert.Equal(row, TcStatCalculator.ClassModRow(unitClass));

    static RowSet Rows(Row row)
    {
        var rows = new RowSet();
        rows.Set(TcTables.CreatureDifficulty, row);
        return rows;
    }

    [Theory]
    [InlineData("HealthScalingExpansion", 12, "health scaling expansion")]
    [InlineData("HealthScalingExpansion", -2, "health scaling expansion")]
    [InlineData("LevelScalingDeltaMin", 40000, "level scaling delta")]
    [InlineData("LevelScalingDeltaMax", -40000, "level scaling delta")]
    public void InvalidScalingValuesAreRefused(string column, int value, string message)
    {
        var issue = Assert.Single(TcStatRules.Check(Rows(Difficulty(v => v.Add((column, value))))));
        Assert.True(issue.Error);
        Assert.Contains(message, issue.Message);
    }

    [Fact]
    public void ADifficultyTheClientDoesNotKnowIsRefused()
    {
        var rows = Rows(Difficulty(v => v.Add(("DifficultyID", 200))));
        var issue = Assert.Single(TcStatRules.Check(rows, (kind, id) => kind == ClientRecord.Difficulty && id == 1));
        Assert.Contains("Difficulty table", issue.Message);
        Assert.Empty(TcStatRules.Check(Rows(Difficulty(v => v.Add(("DifficultyID", 1)))), (kind, id) => kind == ClientRecord.Difficulty && id == 1));
    }

    [Fact]
    public void ValidScalingValuesPass()
    {
        Assert.Empty(TcStatRules.Check(Rows(Difficulty(v => { v.Add(("HealthScalingExpansion", -1)); v.Add(("LevelScalingDeltaMin", -3)); }))));
    }

    [Fact]
    public void ARowSavedBeforeTheLevelDeltaColumnsExistedIsUpgradedWhenTheDraftIsRead()
    {
        string path = Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "stats.json");
        Row Legacy(object loot) => Row.Of(TcTables.CreatureDifficulty.Columns.Where(c => !c.Name.StartsWith("LevelScalingDelta"))
            .Select(c => (c.Name, c.Name switch { "Entry" => (object)7u, "LootID" => loot, _ => 0 })).ToArray());
        var (before, after) = (Legacy(0u), Legacy(5u));
        Assert.Throws<InvalidDataException>(() => before.Validate(TcTables.CreatureDifficulty));

        // An earlier version saved the row images without the new columns.
        using (var history = new DraftHistory<InteractionCommand>(path, "interaction", command => command))
        {
            history.Append(new("tc", "Edit", [new("creature_template_difficulty", TcTables.CreatureDifficulty.KeyOf(before), before, after)]));
        }

        using var reopened = new InteractionDraft(path, TcTables.Instance);
        var net = Assert.Single(reopened.NetEdits()).Edit;
        Assert.Equal("0", net.Before!.T("LevelScalingDeltaMin"));
        Assert.Equal("0", net.After!.T("LevelScalingDeltaMax"));
        Assert.Equal("5", net.After.T("LootID"));
        var scripts = SqlExport.Build("tc", [], [net], new DateTime(2026, 1, 1), null, TcSchema.Instance);
        Assert.Contains("`LootID` = 5", scripts.Apply);
    }
}
