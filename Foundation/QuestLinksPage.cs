namespace ACWorldEdit.Foundation;

/// <summary>The quests a creature offers and accepts. Quest content keeps its separate editor.</summary>
public sealed record QuestLinksPageSpec(ItemListSpec Starts, ItemListSpec Ends)
{
    /// <summary>Set both roles as one command; retain existing rows and their metadata.</summary>
    public RowEdit[] SetRoles(RowSet rows, uint entry, uint quest, bool gives, bool takes)
    {
        var edits = new List<RowEdit>();
        foreach (var (spec, wanted) in new[] { (Starts, gives), (Ends, takes) })
        {
            var existing = spec.Select(rows, entry).FirstOrDefault(r => r.U("quest") == quest);
            if (wanted && existing == null) edits.Add(spec.Add(rows, entry, ("quest", quest)));
            else if (!wanted && existing != null) edits.Add(spec.Remove(existing));
        }
        return edits.ToArray();
    }
}

public static class QuestLinksPage
{
    public static readonly QuestLinksPageSpec AzerothCore = Build(AcTables.QuestStarter, AcTables.QuestEnder);
    public static readonly QuestLinksPageSpec TrinityCore = Build(TcTables.QuestStarter, TcTables.QuestEnder);

    static QuestLinksPageSpec Build(TableSchema starts, TableSchema ends)
    {
        ItemListSpec List(TableSchema table) => new(table, "id", "quest", "quest link", [],
            (entry, _) => Row.Of(("id", entry), ("quest", 0u)));
        return new(List(starts), List(ends));
    }
}
