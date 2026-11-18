using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;

namespace ACWorldEdit.Tests.Db;

/// <summary>
/// Live, read-only player-choice checks against ACWORLD_TC_DB_INFO: TDB choice 240 with its three
/// responses, rewards and source-36 conditions, plus a staged header edit reaching the effective
/// view. Only SELECTs inside read-only transactions; nothing is written.
/// </summary>
[Trait("Category", "Db")]
public class CinematicsDbTests
{
    static IDBSource Open() => DBSource.Open(Environments.TrinityConnection()!);

    [TcDbFact]
    public async Task SearchFindsAChoiceByItsQuestionOrItsId()
    {
        var source = Open();
        var byText = await source.SearchPlayerChoicesAsync("Which weapon", CancellationToken.None);
        Assert.Contains(byText, hit => hit.Id == 240 && hit.Name.Contains("Which weapon"));

        var byId = await source.SearchPlayerChoicesAsync("240", CancellationToken.None);
        Assert.Equal(240u, byId[0].Id);
    }

    [TcDbFact]
    public async Task SnapshotReadsTheResponsesInCardOrderWithTheirRewards()
    {
        var source = Open();
        var snapshot = await source.ReadPlayerChoiceAsync(240, [], rows => rows, null, CancellationToken.None);

        var choice = Assert.IsType<Row>(snapshot.Rows.Get(TcTables.PlayerChoice, "240"));
        Assert.Equal("Which weapon should we pursue first?", choice.T("Question"));
        Assert.True(choice.IsNull("Duration"));
        Assert.True(snapshot.MaxChoiceId >= 240);

        var responses = TcCinematicsRules.Responses(snapshot.Rows, 240).ToList();
        Assert.Equal(new uint[] { 504, 505, 506 }, responses.Select(r => r.U("ResponseId")));
        Assert.Equal(new uint[] { 0, 1, 2 }, responses.Select(r => r.U("Index")));
        Assert.Equal("Beast Mastery", responses[0].T("Header"));

        foreach (var response in responses)
        {
            uint id = response.U("ResponseId");
            Assert.NotNull(snapshot.Rows.Get(TcTables.PlayerChoiceResponseReward, $"240|{id}"));
            var item = Assert.Single(TcCinematicsRules.RewardItems(snapshot.Rows, 240, id));
            Assert.NotEqual(0u, item.U("ItemId"));
            Assert.Equal(1, item.I("Quantity"));
        }
    }

    [TcDbFact]
    public async Task SourceThirtySixConditionsListTheGatedResponsesOfTheChoice()
    {
        var source = Open();
        var conditions = await source.ReadCinematicConditionsAsync(36, 240, null, CancellationToken.None);

        Assert.Equal(new[] { 504, 505, 506 }, conditions.Select(r => r.I("SourceEntry")).Order().ToArray());
        Assert.All(conditions, row => Assert.Equal(36, row.I("SourceTypeOrReferenceId")));
        Assert.All(conditions, row => Assert.Equal(240u, row.U("SourceGroup")));
    }

    [TcDbFact]
    public async Task AStagedHeaderEditAppearsInTheEffectiveRows()
    {
        var source = Open();
        var draftKey = (TableSchema: TcTables.PlayerChoiceResponse, Key: "240|504");

        var loaded = await source.ReadPlayerChoiceAsync(240, [draftKey], rows => rows, null, CancellationToken.None);
        var staged = loaded.Rows.Get(TcTables.PlayerChoiceResponse, draftKey.Key)!.With("Header", "Staged header");
        var edited = await source.ReadPlayerChoiceAsync(240, [draftKey], rows =>
        {
            rows.Set(TcTables.PlayerChoiceResponse, staged);
            return rows;
        }, null, CancellationToken.None);

        Assert.Equal("Staged header", edited.Rows.Get(TcTables.PlayerChoiceResponse, draftKey.Key)!.T("Header"));
    }
}
