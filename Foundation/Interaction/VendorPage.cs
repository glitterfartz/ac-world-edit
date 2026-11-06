namespace ACWorldEdit.Foundation;

/// <summary>Vendor item-list descriptors. Schema references: AzerothCore and TrinityCore npc_vendor wiki pages.</summary>
public static class VendorPage
{
    public static readonly ItemListSpec AzerothCore = Build(AcTables.Vendor, false);
    public static readonly ItemListSpec TrinityCore = Build(TcTables.Vendor, true);

    static ItemListSpec Build(TableSchema table, bool retail)
    {
        FieldSpec Whole(string column, string label, string tip, ulong max = 0) =>
            new("item", table, column, label, tip, FieldKind.Whole, RowBinding.Item, Max: max);
        var fields = new List<FieldSpec>
        {
            Whole("slot", "Position", "Where the item appears in the vendor window. Lower numbers come first.", (ulong)short.MaxValue),
            Whole("maxcount", "Stock limit", "How many the vendor holds before it stops selling. 0 means unlimited.", retail ? uint.MaxValue : byte.MaxValue),
            Whole("incrtime", "Restock time", "How many seconds until the vendor restocks one item after a purchase. Only used with a stock limit."),
            Whole("ExtendedCost", "Alternate price", "The ExtendedCost ID that takes tokens, honor or other items instead of gold. 0 charges the item's normal gold price.", retail ? uint.MaxValue : 16777215)
        };
        if (retail)
        {
            fields.Insert(0, Whole("item", "Item or currency ID", "The client item ID, or the currency ID for a currency row."));
            fields.Insert(1, new("item", table, "type", "Type", "What this row sells: an item or a currency.", FieldKind.Choice, RowBinding.Item, Options: [(1, "Item"), (2, "Currency")]));
            fields.Add(new("item", table, "BonusListIDs", "Bonus lists", "Space-separated item bonus list IDs that change the item's stats or appearance. Empty means none.", FieldKind.Text, RowBinding.Item, AllowEmpty: true));
            fields.Add(Whole("PlayerConditionID", "Player condition", "PlayerCondition ID that must pass before a player can see this item. 0 means no condition."));
            fields.Add(new("item", table, "IgnoreFiltering", "Ignore filtering", "Show this item even when the vendor's normal item filtering would hide it.", FieldKind.Check, RowBinding.Item));
        }

        Row NewRow(uint entry, RowSet rows)
        {
            int slot = checked(rows.All(table).Where(r => r.U("entry") == entry).Select(r => r.I("slot")).DefaultIfEmpty(-1).Max() + 1);
            var row = Row.Of(("entry", entry), ("item", 0), ("ExtendedCost", 0u), ("slot", slot), ("maxcount", 0u), ("incrtime", 0u));
            return retail ? row.With("type", 1u).With("BonusListIDs", "").With("PlayerConditionID", 0u).With("IgnoreFiltering", 0u) : row;
        }

        return new(table, "entry", "slot", "vendor item", fields.ToArray(), NewRow,
            retail ? null : r => r.I("item") > 0, ThenByColumn: "item");
    }
}
