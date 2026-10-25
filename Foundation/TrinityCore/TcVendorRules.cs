using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>TC vendor checks from ObjectMgr::IsVendorItemValid and the npc_vendor schema.</summary>
public static class TcVendorRules
{
    public static IEnumerable<InteractionIssue> Check(uint entry, RowSet rows, Func<uint, bool>? itemExists = null)
    {
        var items = rows.All(TcTables.Vendor).Where(r => r.U("entry") == entry).ToArray();
        if (items.Length > 0 && (rows.Get(TcTables.CreatureTemplate, entry.ToString(CultureInfo.InvariantCulture))?.UL("npcflag") & 0x80ul) is not > 0)
            yield return new(true, "Vendor items require the Vendor NPC flag on the Template page.");

        foreach (var row in items)
        {
            string key = TcTables.Vendor.KeyOf(row);
            InteractionIssue Error(string message) => new(true, $"Vendor row {key}: {message}");
            if (row.I("slot") is < short.MinValue or > short.MaxValue)
                yield return Error("position must fit a signed smallint (-32768 to 32767).");
            uint type = row.U("type");
            if (type is not (1 or 2))
                yield return Error("type must be Item or Currency.");
            if (row.I("item") == 0)
                yield return Error("enter an item or currency ID.");
            // Negative IDs are shared vendor references in TC as well as AC.
            if (row.I("item") < 0) continue;
            if (type == 1)
            {
                if (itemExists != null && !itemExists((uint)row.I("item")))
                    yield return Error("item does not exist in the client's ItemSparse table.");
                if ((row.U("maxcount") == 0) != (row.U("incrtime") == 0))
                    yield return Error("limited stock needs a restock time; unlimited stock needs zero restock time.");
                if (row.T("BonusListIDs").Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(s => !int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                    yield return Error("bonus lists must be space-separated signed integer IDs.");
            }
            if (type == 2 && row.U("maxcount") == 0)
                yield return Error("currency quantity must be greater than zero.");
            if (row.U("IgnoreFiltering") > 1)
                yield return Error("ignore filtering must be zero or one.");
        }
    }
}
