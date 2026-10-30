namespace ACWorldEdit.Foundation;

/// <summary>TC gossip column and npc_text loader rules. Text weights are relative, not percentages.</summary>
public static class TcGossipRules
{
    public static IEnumerable<InteractionIssue> Check(RowSet rows, Func<ClientRecord, uint, bool>? exists = null, Func<uint, bool>? poiExists = null)
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
            if (exists != null)
            {
                if (row.U("Language") != 0 && !exists(ClientRecord.Language, row.U("Language")))
                    yield return new(true, $"Gossip option {key}: language {row.U("Language")} does not exist in the client.");
                if (row.I("SpellID") > 0 && !exists(ClientRecord.Spell, (uint)row.I("SpellID")))
                    yield return new(true, $"Gossip option {key}: spell {row.I("SpellID")} does not exist in the client.");
                if (row.I("OverrideIconID") > 0 && !exists(ClientRecord.FileData, (uint)row.I("OverrideIconID")))
                    yield return new(true, $"Gossip option {key}: icon file {row.I("OverrideIconID")} does not exist in the client.");
                if (row.I("GossipNpcOptionID") > 0 && !exists(ClientRecord.GossipNpcOption, (uint)row.I("GossipNpcOptionID")))
                    yield return new(true, $"Gossip option {key}: client option {row.I("GossipNpcOptionID")} does not exist in the client.");
            }

            if (poiExists != null && row.U("ActionPoiID") != 0 && !poiExists(row.U("ActionPoiID")))
                yield return new(true, $"Gossip option {key}: point of interest {row.U("ActionPoiID")} does not exist.");
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

    /// <summary>A new npc_text row whose first slot plays one broadcast text; the other slots are empty.</summary>
    public static Row NewGreeting(uint id, uint broadcast) =>
        Row.Of(TcTables.NpcText.Columns.Select(c => (c.Name, c.Kind == ColumnKind.Float ? (object)0f : 0u)).ToArray())
            .With("ID", id).With("BroadcastTextID0", broadcast).With("Probability0", 1f);

    /// <summary>Save a broadcast and its weight together, allowing unused slots to be cleared.</summary>
    public static RowEdit[] SetVariant(Row row, int slot, uint broadcast, float weight)
    {
        if (slot is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(slot));
        return ItemListSpec.Change(TcTables.NpcText, row,
            row.With($"BroadcastTextID{slot}", broadcast).With($"Probability{slot}", weight));
    }
}
