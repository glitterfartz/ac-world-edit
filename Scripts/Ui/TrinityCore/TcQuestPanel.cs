using ACWorldEdit.Foundation;

namespace ACWorldEdit;

public partial class TcQuestPanel : QuestPanel
{
    protected override TableSchema Template => TcTables.QuestTemplate;
    protected override TableSchema Addon => TcTables.QuestAddon;
    protected override TableSchema Request => TcTables.QuestRequestItems;
    protected override TableSchema Offer => TcTables.QuestOfferReward;
    protected override TableSchema Starter => TcTables.QuestStarter;
    protected override TableSchema Ender => TcTables.QuestEnder;
    protected override TableSchema[] Tables => TcQuestRules.Tables;
    protected override Row[] NewQuestRows(uint id, string title) => TcQuestRules.NewQuest(id, title);
    protected override IEnumerable<InteractionIssue> Check(uint id, RowSet rows, IContentNames names) => [];
    protected override string Subtitle(Row q) => $"Quest {Key}";
    protected override void RenderOverviewPage(Row q) { }
    protected override void RenderObjectivesPage(Row q) { }
    protected override void RenderRewardsPage(Row q) { }
    protected override void RenderPreviewPage(Row q) { }
}
