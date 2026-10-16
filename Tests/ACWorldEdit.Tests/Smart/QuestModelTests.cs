using ACWorldEdit.Foundation;
using Xunit;

/// <summary>
/// QuestModel.Check against the quest loader rules, one rule per test, over in-memory RowSets
/// (no database, no draft files).
/// </summary>
public class QuestModelTests
{
    const uint Id = 900001;

    sealed class Names : IContentNames
    {
        public string? Spell(uint id) => id == 133 ? "Fireball" : null;
        public string? Creature(uint id) => id == 6 ? "Kobold Vermin" : null;
        public string? GameObject(uint id) => id == 50 ? "Chest" : null;
        public string? Quest(uint id) => id is 7 or Id ? "Known quest" : null;
        public string? Item(uint id) => id == 2589 ? "Linen Cloth" : null;
        public string? Text(uint entry, uint group) => null;
        public bool CreatureSpawnExists(uint guid) => false;
        public string? Emote(uint id) => null;
        public string? TextEmote(uint id) => null;
        public string? FactionTemplate(uint id) => null;
        public string? Faction(uint id) => null;
    }

    static readonly Names Lookup = new();

    /// <summary>
    /// A complete, valid quest with a giver and an ender: the baseline every test perturbs.
    /// </summary>
    static RowSet Valid(Func<Row, Row>? template = null, Func<Row, Row>? addon = null, bool giver = true, bool ender = true, bool addonRow = true)
    {
        var rows = new RowSet();
        var fresh = QuestModel.NewQuest(Id, "Test quest");
        rows.Set(InteractionTables.QuestTemplate, template?.Invoke(fresh[0]) ?? fresh[0]);
        if (addonRow)
        {
            rows.Set(InteractionTables.QuestAddon, addon?.Invoke(fresh[1]) ?? fresh[1]);
        }

        rows.Set(InteractionTables.QuestRequestItems, fresh[2]);
        rows.Set(InteractionTables.QuestOfferReward, fresh[3]);
        if (giver)
        {
            rows.Set(InteractionTables.QuestStarter, Row.Of(("id", 6u), ("quest", Id)));
        }

        if (ender)
        {
            rows.Set(InteractionTables.QuestEnder, Row.Of(("id", 6u), ("quest", Id)));
        }

        return rows;
    }

    static List<InteractionIssue> Issues(RowSet rows, Func<uint, bool>? zones = null) => QuestModel.Check(Id, rows, Lookup, zones).ToList();

    static void AssertIssue(List<InteractionIssue> issues, bool error, string fragment) =>
        Assert.Contains(issues, i => i.Error == error && i.Message.Contains(fragment, StringComparison.Ordinal));

    [Fact]
    public void ValidQuestHasNoIssues()
    {
        Assert.Empty(Issues(Valid()));
    }

    [Fact]
    public void MissingTemplateRowYieldsNothing()
    {
        Assert.Empty(QuestModel.Check(Id, new RowSet(), Lookup, null));
    }

    [Fact]
    public void NewQuestRowsMatchTheirSchemasAndDefaults()
    {
        var fresh = QuestModel.NewQuest(Id, "Hello");
        Assert.Equal(QuestModel.Tables.Length, fresh.Length);
        for (int i = 0; i < fresh.Length; i++)
        {
            fresh[i].Validate(QuestModel.Tables[i]);
            Assert.Equal(Id.ToString(), QuestModel.Tables[i].KeyOf(fresh[i]));
        }

        Assert.Equal("Hello", fresh[0].T("LogTitle"));
        Assert.Equal(2u, fresh[0].U("QuestType"));
        Assert.Equal(1u, fresh[0].U("MinLevel"));
    }

    [Fact]
    public void BlankTitleIsAWarning()
    {
        AssertIssue(Issues(Valid(t => t.With("LogTitle", "   "))), false, "no title");
    }

    [Theory]
    [InlineData(0u, false)]
    [InlineData(1u, false)]
    [InlineData(2u, false)]
    [InlineData(3u, true)]
    public void QuestMethodAboveDisabledIsAnError(uint method, bool error)
    {
        var issues = Issues(Valid(t => t.With("QuestType", method)));
        Assert.Equal(error, issues.Any(i => i.Error && i.Message.Contains("method", StringComparison.Ordinal)));
    }

    [Fact]
    public void MinLevelAboveQuestLevelWarnsOnlyWhenQuestLevelIsSet()
    {
        AssertIssue(Issues(Valid(t => t.With("QuestLevel", 10).With("MinLevel", 12u))), false, "above the quest level");
        Assert.Empty(Issues(Valid(t => t.With("QuestLevel", 10).With("MinLevel", 10u))));
        // QuestLevel 0 or -1 (scales with the player) never trips the comparison.
        Assert.Empty(Issues(Valid(t => t.With("QuestLevel", -1).With("MinLevel", 70u))));
        Assert.Empty(Issues(Valid(t => t.With("QuestLevel", 0).With("MinLevel", 70u))));
    }

    [Fact]
    public void QuestSortZoneMustExistWhenValidatorSupplied()
    {
        var rows = Valid(t => t.With("QuestSortID", 12));
        AssertIssue(Issues(rows, z => false), true, "Zone 12 does not exist");
        Assert.Empty(Issues(rows, z => z == 12));
        Assert.Empty(Issues(rows, null));
        // Negative sort IDs are quest sorts (class/profession), not zones.
        Assert.Empty(Issues(Valid(t => t.With("QuestSortID", -81)), z => false));
    }

    [Fact]
    public void ObjectiveCreatureAndObjectMustExist()
    {
        AssertIssue(Issues(Valid(t => t.With("RequiredNpcOrGo1", 999).With("RequiredNpcOrGoCount1", 1u))), true, "Objective 1: creature 999");
        AssertIssue(Issues(Valid(t => t.With("RequiredNpcOrGo2", -999).With("RequiredNpcOrGoCount2", 1u))), true, "Objective 2: object 999");
        Assert.Empty(Issues(Valid(t => t.With("RequiredNpcOrGo1", 6).With("RequiredNpcOrGoCount1", 8u).With("RequiredNpcOrGo4", -50).With("RequiredNpcOrGoCount4", 1u))));
    }

    [Fact]
    public void ObjectiveCountRules()
    {
        AssertIssue(Issues(Valid(t => t.With("RequiredNpcOrGo3", 6))), true, "Objective 3 needs a count");
        AssertIssue(Issues(Valid(t => t.With("RequiredNpcOrGoCount3", 4u))), false, "Objective 3 has a count but no creature");
    }

    [Theory]
    [InlineData("RequiredItemId", "RequiredItemCount", 6, "Required item", true)]
    [InlineData("RewardItem", "RewardAmount", 4, "Reward item", true)]
    [InlineData("RewardChoiceItemID", "RewardChoiceItemQuantity", 6, "Choice reward", true)]
    [InlineData("ItemDrop", "ItemDropQuantity", 4, "Quest drop", false)]
    public void ItemSlotsReportMissingItemsWithGroupSeverity(string item, string count, int slots, string label, bool missingIsError)
    {
        string last = slots.ToString();
        var issues = Issues(Valid(t => t.With(item + last, 12345u).With(count + last, 1u)));
        AssertIssue(issues, missingIsError, $"{label} {last}: item 12345");
        Assert.Empty(Issues(Valid(t => t.With(item + "1", 2589u).With(count + "1", 2u))));
    }

    [Theory]
    [InlineData("RequiredItemId", "RequiredItemCount", "Required item", true)]
    [InlineData("RewardItem", "RewardAmount", "Reward item", true)]
    [InlineData("RewardChoiceItemID", "RewardChoiceItemQuantity", "Choice reward", true)]
    [InlineData("ItemDrop", "ItemDropQuantity", "Quest drop", false)]
    public void ItemSlotAmountRules(string item, string count, string label, bool zeroAmountIsIssue)
    {
        var zero = Issues(Valid(t => t.With(item + "1", 2589u)));
        Assert.Equal(zeroAmountIsIssue, zero.Any(i => i.Error && i.Message.Contains($"{label} 1 needs an amount", StringComparison.Ordinal)));
        AssertIssue(Issues(Valid(t => t.With(count + "1", 3u))), false, $"{label} 1 has an amount but no item");
    }

    [Fact]
    public void StartItemSpellsAndFollowUpMustExist()
    {
        AssertIssue(Issues(Valid(t => t.With("StartItem", 1u), a => a)), true, "Starting item 1");
        Assert.Empty(Issues(Valid(t => t.With("StartItem", 2589u))));
        AssertIssue(Issues(Valid(t => t.With("RewardSpell", 1))), true, "Reward spell 1");
        AssertIssue(Issues(Valid(t => t.With("RewardDisplaySpell", 1))), true, "Displayed spell 1");
        Assert.Empty(Issues(Valid(t => t.With("RewardSpell", 133).With("RewardDisplaySpell", 133))));
        AssertIssue(Issues(Valid(t => t.With("RewardNextQuest", 5u))), true, "Follow-up quest 5");
        Assert.Empty(Issues(Valid(t => t.With("RewardNextQuest", 7u))));
    }

    [Fact]
    public void AddonChainSpellAndLevelRules()
    {
        AssertIssue(Issues(Valid(addon: a => a.With("PrevQuestID", -5))), true, "Previous quest 5");
        Assert.Empty(Issues(Valid(addon: a => a.With("PrevQuestID", -7))));
        Assert.Empty(Issues(Valid(addon: a => a.With("PrevQuestID", 7))));
        AssertIssue(Issues(Valid(addon: a => a.With("NextQuestID", 5u))), true, "Next quest 5");
        AssertIssue(Issues(Valid(addon: a => a.With("SourceSpellID", 5u))), true, "Source spell 5");
        Assert.Empty(Issues(Valid(addon: a => a.With("SourceSpellID", 133u))));
        AssertIssue(Issues(Valid(t => t.With("QuestLevel", 0).With("MinLevel", 20u), a => a.With("MaxLevel", 10u))), false, "Maximum level is below");
        Assert.Empty(Issues(Valid(t => t.With("QuestLevel", 0).With("MinLevel", 20u), a => a.With("MaxLevel", 0u))));
    }

    [Fact]
    public void MissingAddonRowWarnsAndSkipsAddonRules()
    {
        var issues = Issues(Valid(addonRow: false));
        AssertIssue(issues, false, "quest_template_addon row is missing");
        Assert.Single(issues);
    }

    [Fact]
    public void RaceMaskWithUnknownBitsWarns()
    {
        Assert.Empty(Issues(Valid(t => t.With("AllowableRaces", QuestModel.AllianceRaces | QuestModel.HordeRaces))));
        Assert.Empty(Issues(Valid(t => t.With("AllowableRaces", 0u))));
        AssertIssue(Issues(Valid(t => t.With("AllowableRaces", 256u))), false, "unknown bits 0x100");
    }

    [Fact]
    public void AllianceAndHordeRaceMasksAreDisjointAndInTheRaceList()
    {
        Assert.Equal(0u, QuestModel.AllianceRaces & QuestModel.HordeRaces);
        uint listed = QuestModel.Races.Aggregate(0u, (mask, race) => mask | race.Bit);
        Assert.Equal(listed, QuestModel.AllianceRaces | QuestModel.HordeRaces);
    }

    [Fact]
    public void GiverRuleHonoursStartItemAndTheAutoAcceptFlag()
    {
        AssertIssue(Issues(Valid(giver: false)), false, "starts this quest");
        Assert.Empty(Issues(Valid(t => t.With("StartItem", 2589u), giver: false)));
        Assert.Empty(Issues(Valid(t => t.With("Flags", 0x400u), giver: false)));
        // A giver row for a different quest does not count.
        var other = Valid(giver: false);
        other.Set(InteractionTables.QuestStarter, Row.Of(("id", 6u), ("quest", Id + 1)));
        AssertIssue(Issues(other), false, "starts this quest");
    }

    [Fact]
    public void EnderRuleHonoursTheNoTurnInFlag()
    {
        AssertIssue(Issues(Valid(ender: false)), false, "completes this quest");
        Assert.Empty(Issues(Valid(t => t.With("Flags", 0x10000u), ender: false)));
    }

    [Fact]
    public void FlagTablesHaveUniqueSingleBits()
    {
        foreach (var table in new[] { QuestModel.Flags, QuestModel.SpecialFlags, QuestModel.Races, QuestModel.Classes })
        {
            Assert.All(table, f => Assert.Equal(1, System.Numerics.BitOperations.PopCount(f.Bit)));
            Assert.Equal(table.Length, table.Select(f => f.Bit).Distinct().Count());
        }

        Assert.Equal(QuestModel.Methods.Length, QuestModel.Methods.Select(m => m.Value).Distinct().Count());
        Assert.Equal(QuestModel.Types.Length, QuestModel.Types.Select(m => m.Value).Distinct().Count());
    }
}
