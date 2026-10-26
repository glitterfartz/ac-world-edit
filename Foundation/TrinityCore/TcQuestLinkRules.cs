namespace ACWorldEdit.Foundation;

/// <summary>TC quest giver / ender link checks from ObjectMgr::LoadCreatureQuestStarters and LoadCreatureQuestEnders.</summary>
public static class TcQuestLinkRules
{
    public static IEnumerable<InteractionIssue> Check(RowSet rows, Func<uint, bool>? questExists = null)
    {
        foreach (var (table, role) in new[] { (TcTables.QuestStarter, "starter"), (TcTables.QuestEnder, "ender") })
        {
            foreach (var row in rows.All(table))
            {
                string key = table.KeyOf(row);
                if (row.U("quest") == 0)
                    yield return new(true, $"Quest {role} {key}: enter a quest ID.");
                else if (questExists != null && !questExists(row.U("quest")))
                    yield return new(true, $"Quest {role} {key}: quest does not exist in quest_template.");
            }
        }
    }
}
