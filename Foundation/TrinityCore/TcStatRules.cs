namespace ACWorldEdit.Foundation;

/// <summary>Checks on the scaling columns of creature_template_difficulty, from ObjectMgr::LoadCreatureTemplateDifficulty and the table's column types.</summary>
public static class TcStatRules
{
    public static IEnumerable<InteractionIssue> Check(RowSet rows)
    {
        foreach (var row in rows.All(TcTables.CreatureDifficulty))
        {
            string key = $"Creature {row.U("Entry")}, difficulty {row.I("DifficultyID")}";
            if (row.I("HealthScalingExpansion") is < -1 or > TcStatCalculator.CurrentExpansion)
                yield return new(true, $"{key}: health scaling expansion must be -1 (current) or 0 to {TcStatCalculator.CurrentExpansion}.");
            foreach (string column in new[] { "LevelScalingDeltaMin", "LevelScalingDeltaMax" })
            {
                if (row.I(column) is < short.MinValue or > short.MaxValue)
                    yield return new(true, $"{key}: level scaling delta must be between {short.MinValue} and {short.MaxValue}.");
            }

            if (row.I("DifficultyID") is < 0 or > byte.MaxValue)
                yield return new(true, $"{key}: difficulty must be between 0 and {byte.MaxValue}.");
        }
    }
}
