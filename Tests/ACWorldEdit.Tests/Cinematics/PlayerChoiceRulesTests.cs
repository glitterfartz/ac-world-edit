using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Cinematics;

/// <summary>
/// TcCinematicsRules over in-memory player-choice rows: complete new rows, the next free IDs and
/// positions, the child rows a removal takes with it, and one loader check per test.
/// </summary>
public class PlayerChoiceRulesTests
{
    sealed class Names : IContentNames
    {
        public string? Spell(uint id) => null;
        public string? Creature(uint id) => null;
        public string? GameObject(uint id) => null;
        public string? Quest(uint id) => id == 7 ? "Known quest" : null;
        public string? Item(uint id) => id == 2589 ? "Linen Cloth" : null;
        public string? Text(uint entry, uint group) => null;
        public bool CreatureSpawnExists(ulong guid) => false;
        public string? Emote(uint id) => null;
        public string? TextEmote(uint id) => null;
        public string? FactionTemplate(uint id) => null;
        public string? Faction(uint id) => null;
    }

    static readonly Names Lookup = new();

    static RowSet Set(params (TableSchema Table, Row Row)[] rows)
    {
        var set = new RowSet();
        foreach (var (table, row) in rows)
        {
            row.Validate(table);
            set.Set(table, row);
        }

        return set;
    }

    static List<InteractionIssue> Check(RowSet rows, uint choice = 240) => TcCinematicsRules.Check(choice, rows, Lookup).ToList();

    static Row Response(uint id, uint index, string answer = "Axes", string header = "Axes") =>
        TcCinematicsRules.NewResponse(240, id, index, answer).With("Header", header);

    [Fact]
    public void NewChoiceFillsEveryColumnAndLeavesTheOptionalOnesNull()
    {
        var row = TcCinematicsRules.NewChoice(240);
        row.Validate(TcTables.PlayerChoice);

        Assert.Equal(240, row.I("ChoiceId"));
        Assert.Equal("", row.T("PendingChoiceText"));
        Assert.True(row.IsNull("Question"));
        Assert.True(row.IsNull("Duration"));
        Assert.True(row.IsNull("MaxResponses"));
        Assert.True(row.IsNull("ScriptName"));
        Assert.Equal(TcTables.PlayerChoice.Columns.Length, row.Values.Count);
    }

    [Fact]
    public void NewResponseUsesTheAnswerAsItsButtonTextAndHeader()
    {
        var row = TcCinematicsRules.NewResponse(240, 504, 1, "Swords");
        row.Validate(TcTables.PlayerChoiceResponse);

        Assert.Equal("Swords", row.T("Answer"));
        Assert.Equal("Swords", row.T("Header"));
        Assert.Equal(1u, row.U("Index"));
        Assert.True(row.IsNull("RewardQuestID"));
    }

    [Fact]
    public void NextIdsAndPositionsFollowTheHighestOneRatherThanTheRowCount()
    {
        var rows = Set(
            (TcTables.PlayerChoiceResponse, Response(504, 0)),
            (TcTables.PlayerChoiceResponse, Response(506, 2)));

        Assert.Equal(507u, TcCinematicsRules.NextResponseId(rows, 240));
        Assert.Equal(3u, TcCinematicsRules.NextIndex(rows, 240));
        Assert.Equal(1u, TcCinematicsRules.NextResponseId(new RowSet(), 240));
    }

    [Fact]
    public void RemoveResponseDeletesTheResponseRewardAndItemsTogether()
    {
        var rows = Set(
            (TcTables.PlayerChoiceResponse, Response(504, 0)),
            (TcTables.PlayerChoiceResponse, Response(505, 1)),
            (TcTables.PlayerChoiceResponseReward, TcCinematicsRules.NewReward(240, 504)),
            (TcTables.PlayerChoiceResponseReward, TcCinematicsRules.NewReward(240, 505)),
            (TcTables.PlayerChoiceResponseRewardItem, TcCinematicsRules.NewRewardItem(240, 504, 0, 2589, 5)),
            (TcTables.PlayerChoiceResponseRewardItem, TcCinematicsRules.NewRewardItem(240, 505, 0, 2589, 1)));

        var edits = TcCinematicsRules.RemoveResponse(Response(504, 0), rows);

        Assert.Equal(3, edits.Length);
        Assert.All(edits, edit => Assert.Null(edit.After));
        Assert.Contains(edits, edit => edit.Table == "playerchoice_response" && edit.Key == "240|504");
        Assert.Contains(edits, edit => edit.Table == "playerchoice_response_reward" && edit.Key == "240|504");
        Assert.Contains(edits, edit => edit.Table == "playerchoice_response_reward_item" && edit.Key == "240|504");
        Assert.DoesNotContain(edits, edit => edit.Key == "240|505");
    }

    [Fact]
    public void CheckAcceptsACardWithDistinctPositionsAndKnownRewards()
    {
        var choice = TcCinematicsRules.NewChoice(240).With("Question", "Which weapon?");
        var rows = Set(
            (TcTables.PlayerChoice, choice),
            (TcTables.PlayerChoiceResponse, Response(504, 0).With("RewardQuestID", 7u)),
            (TcTables.PlayerChoiceResponse, Response(505, 1)),
            (TcTables.PlayerChoiceResponseReward, TcCinematicsRules.NewReward(240, 504)),
            (TcTables.PlayerChoiceResponseRewardItem, TcCinematicsRules.NewRewardItem(240, 504, 0, 2589, 5)));

        Assert.Empty(Check(rows));
    }

    [Fact]
    public void CheckRejectsTwoResponsesAtTheSamePosition()
    {
        var rows = Set(
            (TcTables.PlayerChoice, TcCinematicsRules.NewChoice(240).With("Question", "Which weapon?")),
            (TcTables.PlayerChoiceResponse, Response(504, 0)),
            (TcTables.PlayerChoiceResponse, Response(505, 0)));

        Assert.Contains(Check(rows), issue => issue.Error && issue.Message.Contains("share position 0"));
    }

    [Fact]
    public void CheckRejectsAnEmptyAnswerAndHeader()
    {
        var rows = Set(
            (TcTables.PlayerChoice, TcCinematicsRules.NewChoice(240).With("Question", "Which weapon?")),
            (TcTables.PlayerChoiceResponse, Response(504, 0, "", "")));

        var issues = Check(rows);
        Assert.Contains(issues, issue => issue.Error && issue.Message.Contains("answer (button text) is empty"));
        Assert.Contains(issues, issue => issue.Error && issue.Message.Contains("header is empty"));
    }

    [Fact]
    public void CheckRejectsUnknownRewardQuestsAndItems()
    {
        var rows = Set(
            (TcTables.PlayerChoice, TcCinematicsRules.NewChoice(240).With("Question", "Which weapon?")),
            (TcTables.PlayerChoiceResponse, Response(504, 0).With("RewardQuestID", 99u)),
            (TcTables.PlayerChoiceResponseRewardItem, TcCinematicsRules.NewRewardItem(240, 504, 0, 999999, 1)));

        var issues = Check(rows);
        Assert.Contains(issues, issue => issue.Error && issue.Message.Contains("reward quest 99"));
        Assert.Contains(issues, issue => issue.Error && issue.Message.Contains("reward item 999999 does not exist"));
    }

    [Fact]
    public void CheckWarnsAboutARewardRowWithNoResponse()
    {
        var rows = Set(
            (TcTables.PlayerChoice, TcCinematicsRules.NewChoice(240).With("Question", "Which weapon?")),
            (TcTables.PlayerChoiceResponse, Response(504, 0)),
            (TcTables.PlayerChoiceResponseReward, TcCinematicsRules.NewReward(240, 999)));

        Assert.Contains(Check(rows), issue => !issue.Error && issue.Message.Contains("response 999 has no response"));
    }

    [Fact]
    public void CheckWarnsWhenMaxResponsesHidesSomeResponses()
    {
        var rows = Set(
            (TcTables.PlayerChoice, TcCinematicsRules.NewChoice(240).With("Question", "Which weapon?").With("MaxResponses", 1u)),
            (TcTables.PlayerChoiceResponse, Response(504, 0)),
            (TcTables.PlayerChoiceResponse, Response(505, 1)));

        Assert.Contains(Check(rows), issue => !issue.Error && issue.Message.Contains("Only the first 1 of 2"));
    }
}
