using ACWorldEdit.Foundation;
using Xunit;

/// <summary>
/// TcQuestRules over in-memory rows: one rule per test, against a quest that is valid until
/// perturbed. Names and client references are stubs.
/// </summary>
public class TcQuestTests
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
        public bool CreatureSpawnExists(ulong guid) => false;
        public string? Emote(uint id) => null;
        public string? TextEmote(uint id) => null;
        public string? FactionTemplate(uint id) => null;
        public string? Faction(uint id) => null;
    }

    static readonly Names Lookup = new();
    static readonly Func<ClientRecord, uint, bool> Client = (kind, id) => kind == ClientRecord.Currency && id == 1;

    static RowSet Valid(Func<Row, Row>? template = null, params Row[] objectives)
    {
        var rows = new RowSet();
        var fresh = TcQuestRules.NewQuest(Id, "Test quest");
        rows.Set(TcTables.QuestTemplate, template?.Invoke(fresh[0]) ?? fresh[0]);
        rows.Set(TcTables.QuestAddon, fresh[1]);
        rows.Set(TcTables.QuestRequestItems, fresh[2]);
        rows.Set(TcTables.QuestOfferReward, fresh[3]);
        rows.Set(TcTables.QuestStarter, Row.Of(("id", 6u), ("quest", Id)));
        rows.Set(TcTables.QuestEnder, Row.Of(("id", 6u), ("quest", Id)));
        foreach (var objective in objectives)
        {
            rows.Set(TcTables.QuestObjectives, objective);
        }

        return rows;
    }

    static Row Objective(uint id, uint type, int target, int amount = 1, int storage = 0) =>
        TcQuestRules.NewObjective(id, Id, storage, 0).With("Type", type).With("ObjectID", target).With("Amount", amount);

    static string[] Issues(RowSet rows, bool errors) =>
        TcQuestRules.Check(Id, rows, Lookup, zone => zone == 12, Client).Where(i => i.Error == errors).Select(i => i.Message).ToArray();

    [Fact]
    public void AQuestCanBeStartedAndCompletedByGameObjects()
    {
        var rows = Valid();
        rows.Remove(TcTables.QuestStarter, $"{6}|{Id}");
        rows.Remove(TcTables.QuestEnder, $"{6}|{Id}");
        rows.Set(TcTables.GameObjectQuestStarter, Row.Of(("id", 50u), ("quest", Id)));
        rows.Set(TcTables.GameObjectQuestEnder, Row.Of(("id", 50u), ("quest", Id)));
        var issues = TcQuestRules.Check(Id, rows, Lookup, zone => zone == 12, Client).ToArray();
        Assert.DoesNotContain(issues, issue => issue.Message.Contains("starts this quest", StringComparison.Ordinal) || issue.Message.Contains("completes this quest", StringComparison.Ordinal));
    }

    [Fact]
    public void NewQuestIsValidAndHasEveryTable()
    {
        var rows = Valid(null, Objective(1, TcQuestRules.Monster, 6));
        Assert.Empty(Issues(rows, true));
        Assert.Empty(Issues(rows, false));
        Assert.Equal(TcQuestRules.Tables.Length, TcQuestRules.NewQuest(Id, "x").Length);
    }

    [Theory]
    [InlineData(TcQuestRules.Monster, 7, "creature 7")]
    [InlineData(TcQuestRules.TalkTo, 7, "creature 7")]
    [InlineData(TcQuestRules.GameObject, 7, "object 7")]
    [InlineData(TcQuestRules.Item, 7, "item 7")]
    [InlineData(TcQuestRules.LearnSpell, 7, "spell 7")]
    [InlineData(TcQuestRules.Currency, 2, "currency 2")]
    public void ObjectiveReferencesMustExist(uint type, int target, string expected) =>
        Assert.Contains(Issues(Valid(null, Objective(1, type, target)), true), m => m.Contains(expected));

    [Theory]
    [InlineData(TcQuestRules.Monster, 6)]
    [InlineData(TcQuestRules.GameObject, 50)]
    [InlineData(TcQuestRules.Item, 2589)]
    [InlineData(TcQuestRules.LearnSpell, 133)]
    [InlineData(TcQuestRules.ObtainCurrency, 1)]
    [InlineData(TcQuestRules.AreaTrigger, 999)]
    public void KnownTargetsPass(uint type, int target) =>
        Assert.Empty(Issues(Valid(null, Objective(1, type, target)), true));

    [Fact]
    public void AnObjectiveWithoutATargetIsOnlyAWarning()
    {
        var rows = Valid(null, Objective(1, TcQuestRules.Monster, 0));
        Assert.Empty(Issues(rows, true));
        Assert.Contains(Issues(rows, false), m => m.Contains("no target"));
    }

    [Fact]
    public void CountedObjectivesNeedAnAmountButAreaTriggersDoNot()
    {
        Assert.Contains(Issues(Valid(null, Objective(1, TcQuestRules.Monster, 6, 0)), true), m => m.Contains("amount"));
        Assert.Empty(Issues(Valid(null, Objective(1, TcQuestRules.AreaTrigger, 999, 0)), true));
    }

    [Fact]
    public void ObjectivesMayNotShareAStorageSlot()
    {
        var rows = Valid(null, Objective(1, TcQuestRules.Monster, 6, 1, 0), Objective(2, TcQuestRules.GameObject, 50, 1, 0));
        Assert.Contains(Issues(rows, true), m => m.Contains("storage slot 0"));
        Assert.Empty(Issues(Valid(null, Objective(1, TcQuestRules.Monster, 6, 1, 0), Objective(2, TcQuestRules.GameObject, 50, 1, 1)), true));
    }

    [Fact]
    public void ObjectivesOfOtherQuestsAreIgnored()
    {
        var other = TcQuestRules.NewObjective(5, 123, 0, 0).With("Type", TcQuestRules.Monster).With("ObjectID", 99999).With("Amount", 1);
        Assert.Empty(Issues(Valid(null, other), true));
    }

    [Fact]
    public void RewardsAndReferencesAreChecked()
    {
        Assert.Contains(Issues(Valid(q => q.With("RewardItem1", 9u).With("RewardAmount1", 1u)), true), m => m.Contains("item 9"));
        Assert.Contains(Issues(Valid(q => q.With("RewardItem1", 2589u)), true), m => m.Contains("amount"));
        Assert.Contains(Issues(Valid(q => q.With("RewardCurrencyID1", 9u).With("RewardCurrencyQty1", 5u)), true), m => m.Contains("currency 9"));
        Assert.Contains(Issues(Valid(q => q.With("RewardSpell", 9u)), true), m => m.Contains("spell 9"));
        Assert.Contains(Issues(Valid(q => q.With("RewardNextQuest", 9u)), true), m => m.Contains("quest 9"));
        Assert.Contains(Issues(Valid(q => q.With("QuestSortID", 99)), true), m => m.Contains("Zone 99"));
        Assert.Empty(Issues(Valid(q => q.With("QuestSortID", 12).With("RewardItem1", 2589u).With("RewardAmount1", 2u)), true));
    }

    [Fact]
    public void QuestEditsExportAsRowsOfTheTrinityCoreTables()
    {
        var when = new DateTime(2026, 1, 1);
        var objective = Objective(77, TcQuestRules.Monster, 6);
        var created = new RowEdit("quest_objectives", "77", null, objective);
        var sql = SqlExport.Build("tc", [], [created], when, null, TcSchema.Instance).Apply;
        // `Order` is a reserved word, so every identifier is quoted; the new row carries every whitelisted column.
        Assert.Contains("INSERT INTO `quest_objectives`", sql);
        Assert.Contains("`Order`", sql);
        Assert.Contains("`StorageIndex`", sql);

        var edit = new RowEdit("quest_objectives", "77", objective, objective.With("Amount", 8));
        Assert.Contains("UPDATE `quest_objectives` SET `Amount` = 8 WHERE `ID` = 77 AND `Amount` = 1;", SqlExport.Build("tc", [], [edit], when, null, TcSchema.Instance).Apply);

        var template = TcQuestRules.NewQuest(Id, "Old title")[0];
        var rename = new RowEdit("quest_template", Id.ToString(), template, template.With("LogTitle", "New title").With("AllowableRaces", TcQuestRules.HordeRaces));
        var renamed = SqlExport.Build("tc", [], [rename], when, null, TcSchema.Instance).Apply;
        Assert.Contains("`LogTitle` = 'New title'", renamed);
        Assert.Contains($"`AllowableRaces` = {TcQuestRules.HordeRaces}", renamed);
    }

    [Fact]
    public void MissingGiversAreWarnings()
    {
        var rows = Valid();
        rows.Remove(TcTables.QuestStarter, $"6|{Id}");
        rows.Remove(TcTables.QuestEnder, $"6|{Id}");
        var warnings = Issues(rows, false);
        Assert.Contains(warnings, m => m.Contains("starts"));
        Assert.Contains(warnings, m => m.Contains("completes"));
    }
}
