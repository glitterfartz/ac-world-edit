namespace ACWorldEdit.Foundation;

/// <summary>TC gossip column and npc_text loader rules. Text weights are relative, not percentages.</summary>
public static class TcGossipRules
{
    public static IEnumerable<InteractionIssue> Check(RowSet rows)
    {
        foreach (var row in rows.All(TcTables.GossipOption))
        {
            string key = TcTables.GossipOption.KeyOf(row);
            if (row.U("OptionNpc") >= TcCreatureView.OptionNpcNames.Length)
                yield return new(true, $"Gossip option {key}: unknown service.");
            if (row.U("BoxCoded") > 1)
                yield return new(true, $"Gossip option {key}: ask for code must be zero or one.");
            if (row.UL("BoxMoney") > uint.MaxValue)
                yield return new(true, $"Gossip option {key}: confirmation cost must fit an unsigned int.");
        }
        foreach (var row in rows.All(TcTables.NpcText))
        {
            uint id = row.U("ID");
            if (id == 0) yield return new(true, "Greeting ID 0 is reserved.");
            double total = 0;
            for (int i = 0; i < 8; i++)
            {
                float weight = row.F($"Probability{i}");
                if (!float.IsFinite(weight) || weight < 0)
                    yield return new(true, $"Greeting {id} slot {i}: weight must be finite and nonnegative.");
                else if (weight > 0 && row.U($"BroadcastTextID{i}") == 0)
                    yield return new(true, $"Greeting {id} slot {i}: a positive weight needs a broadcast text ID.");
                else total += weight;
            }
            if (total <= 0) yield return new(true, $"Greeting {id}: at least one broadcast text needs a positive weight.");
        }
    }

    /// <summary>Save a broadcast and its weight together, allowing unused slots to be cleared.</summary>
    public static RowEdit[] SetVariant(Row row, int slot, uint broadcast, float weight)
    {
        if (slot is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(slot));
        return ItemListSpec.Change(TcTables.NpcText, row,
            row.With($"BroadcastTextID{slot}", broadcast).With($"Probability{slot}", weight));
    }
}
