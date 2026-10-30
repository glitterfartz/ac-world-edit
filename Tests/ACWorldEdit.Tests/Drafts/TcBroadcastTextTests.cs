using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class TcBroadcastTextTests
{
    static RowSet Staged(uint id = 400000, string text = "Welcome, friend.")
    {
        var rows = new RowSet();
        foreach (var edit in TcBroadcastText.Create(id, text, 112000))
        {
            rows.Set(edit.Table == TcTables.BroadcastText.Name ? TcTables.BroadcastText : TcTables.HotfixData, edit.After!);
        }

        return rows;
    }

    [Fact]
    public void ANewTextIsACustomBroadcastRowAndAValidHotfixRecord()
    {
        var edits = TcBroadcastText.Create(400000, "Welcome, friend.", 112000);
        Assert.Equal(2, edits.Length);
        var broadcast = edits[0].After!;
        broadcast.Validate(TcTables.BroadcastText);
        Assert.Equal((400000u, -1, "Welcome, friend."), (broadcast.U("ID"), broadcast.I("VerifiedBuild"), broadcast.T("Text")));
        var hotfix = edits[1].After!;
        hotfix.Validate(TcTables.HotfixData);
        Assert.Equal((112000, 35137211u, 400000, 1u), (hotfix.I("Id"), hotfix.U("TableHash"), hotfix.I("RecordId"), hotfix.U("Status")));
    }

    [Theory]
    [InlineData(0u, "text")]
    [InlineData(5u, "  ")]
    public void ATextNeedsAnIdAndSomeText(uint id, string text) => Assert.Throws<InvalidDataException>(() => TcBroadcastText.Create(id, text, 1));

    [Fact]
    public void TheExportWritesBothRowsToTheHotfixesDatabaseWithPreflightsAndReverts()
    {
        var edits = TcBroadcastText.Create(400000, "It's a \"new\" day.", 112000);
        var sql = SqlExport.Build("tc", [], edits, DateTime.UnixEpoch, schema: TcSchema.Instance);
        Assert.Contains("INSERT INTO `hotfixes`.`broadcast_text`", sql.Apply);
        Assert.Contains("INSERT INTO `hotfixes`.`hotfix_data`", sql.Apply);
        Assert.Contains("FROM `hotfixes`.`broadcast_text` WHERE", sql.Apply);
        Assert.Contains("DELETE FROM `hotfixes`.`broadcast_text`", sql.Revert);
        Assert.Contains("DELETE FROM `hotfixes`.`hotfix_data`", sql.Revert);
        Assert.DoesNotContain("`hotfixes.broadcast_text`", sql.Apply);
    }

    [Fact]
    public void AStagedTextIsReadBeforeTheDatabaseAndTheViewShowsIt()
    {
        Assert.Equal(("Welcome, friend.", "Welcome, friend."), TcBroadcastText.Staged(Staged(), 400000));
        Assert.Null(TcBroadcastText.Staged(Staged(), 400001));

        var rows = Staged();
        rows.Set(TcTables.CreatureTemplate, Row.Of(TcTables.CreatureTemplate.Columns.Select(c => (c.Name, c.Name == "entry" ? (object)7u : c.Kind == ColumnKind.Text ? "" : 0)).ToArray()));
        rows.Set(TcTables.CreatureGossip, Row.Of(("CreatureID", 7u), ("MenuID", 100u)));
        rows.Set(TcTables.GossipMenu, Row.Of(("MenuID", 100u), ("TextID", 5u)));
        rows.Set(TcTables.NpcText, Row.Of([("ID", (object)5u), .. Enumerable.Range(0, 8).SelectMany(i => new (string, object)[] { ($"BroadcastTextID{i}", i == 0 ? 400000u : 0u), ($"Probability{i}", i == 0 ? 1f : 0f) })]));
        var view = TcCreatureView.Build(7, rows, new InteractionLookupData(null));
        Assert.Equal("Welcome, friend.", view.Menus.Single().Texts.Single().Variants.Single().Text);
    }
}
