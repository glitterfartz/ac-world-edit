namespace ACWorldEdit.Foundation;

/// <summary>The GameObject quest links (starters/enders) and the quest items it can hold.</summary>
public sealed record GameObjectQuestPageSpec(QuestLinksPageSpec Links, ItemListSpec Items)
{
    public RowEdit[] SetRoles(RowSet rows, uint entry, uint quest, bool gives, bool takes) =>
        Links.SetRoles(rows, entry, quest, gives, takes);
}

public static class GameObjectQuestPage
{
    public static readonly GameObjectQuestPageSpec AzerothCore = Build(AcTables.GameObjectQuestStarter, AcTables.GameObjectQuestEnder, AcTables.GameObjectQuestItem);
    public static readonly GameObjectQuestPageSpec TrinityCore = Build(TcTables.GameObjectQuestStarter, TcTables.GameObjectQuestEnder, TcTables.GameObjectQuestItem);

    public static GameObjectQuestPageSpec For(WorldDialect dialect) => dialect == WorldDialect.TrinityCore ? TrinityCore : AzerothCore;

    static GameObjectQuestPageSpec Build(TableSchema starts, TableSchema ends, TableSchema items)
    {
        ItemListSpec List(TableSchema table) => new(table, "id", "quest", "quest link", [],
            (entry, _) => Row.Of(("id", entry), ("quest", 0u)));
        var itemList = new ItemListSpec(items, "GameObjectEntry", "Idx", "quest item", [],
            (entry, _) => Row.Of(("GameObjectEntry", entry), ("Idx", 0u), ("ItemId", 0u)));
        return new(new QuestLinksPageSpec(List(starts), List(ends)), itemList);
    }
}
