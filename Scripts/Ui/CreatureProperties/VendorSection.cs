using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

public sealed partial class VendorSection(CreatureContext ctx) : InteractionSection(ctx)
{
    public override string Id => "vendor";
    public override string Title => "Vendor";
    public override string IconPath => ClientStyle.GossipIcon(1);
    public override string Badge => Ctx.View is { Vendor.Length: > 0 } v ? v.Vendor.Length.ToString() : "";
    public override bool HasContent => Ctx.View is { } v && (Ctx.Editable || v.Vendor.Length > 0);
    const int VendorPageSize = 10;
    int vendorPage;
    readonly Dictionary<string, string> iconPaths = [];
    readonly Dictionary<string, Tooltip> tooltips = [];

    protected override void Populate(NpcInteraction v)
    {
        RenderVendor(v);
    }

    public override void OnShown(object? argument) => vendorPage = 0;

    void RenderVendor(NpcInteraction v)
    {
        if (editable)
        {
            RenderVendorEditor(v);
            return;
        }

        int pages = Math.Max(1, (v.Vendor.Length + VendorPageSize - 1) / VendorPageSize);
        vendorPage = Math.Clamp(vendorPage, 0, pages - 1);
        var grid = new GridContainer
        {
            Columns = 2
        };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 8);
        body.AddChild(grid);
        foreach (var item in v.Vendor.Skip(vendorPage * VendorPageSize).Take(VendorPageSize))
        {
            var cell = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            var name = ClientStyle.Text(item.Info?.Name ?? $"Item {item.Item} (missing)", 14, item.Info != null ? ClientStyle.Quality(item.Info.Quality) : ClientStyle.Error);
            cell.AddChild(name);
            string price = item.ExtendedCost != 0 ? catalog?.DescribeExtendedCost(item.ExtendedCost) ?? $"Extended cost {item.ExtendedCost}" : ClientStyle.Money(item.Info?.BuyPrice ?? 0);
            cell.AddChild(ClientStyle.Text(price + (item.MaxCount > 0 ? $" · stock {item.MaxCount}" : "") + (item.FromReference != 0 ? $" · list {item.FromReference}" : ""), 12, item.Edited ? ClientStyle.Edited : ClientStyle.Faded));
            string icon = item.Info != null && catalog?.ItemIconPath(item.Info.DisplayId) is { } path ? path : ClientStyle.MissingIcon;
            var holder = IconRow(icon, cell, 37, ItemTip(item.Item));
            holder.CustomMinimumSize = new(205, 44);
            grid.AddChild(holder);
        }

        int conditions = v.Conditions.GetValueOrDefault(23);
        body.AddChild(ClientStyle.Text($"Page {vendorPage + 1} of {pages} · {v.Vendor.Length} items{(conditions > 0 ? $" · {conditions} conditional" : "")} · event-only items (game_event_npc_vendor) are not shown.", 11, ClientStyle.Faded));
        var nav = new HBoxContainer();
        body.AddChild(nav);
        var previous = new Button
        {
            Text = "◂ Prev",
            Disabled = vendorPage == 0
        };
        previous.Pressed += () =>
        {
            vendorPage--;
            Render();
        };
        var next = new Button
        {
            Text = "Next ▸",
            Disabled = vendorPage >= pages - 1
        };
        next.Pressed += () =>
        {
            vendorPage++;
            Render();
        };
        nav.AddChild(previous);
        nav.AddChild(next);
    }

    void RenderVendorEditor(NpcInteraction v)
    {
        body.AddChild(ClientStyle.Text("Items for sale", 16, ClientStyle.Header));
        if (v.Vendor.Length == 0)
        {
            body.AddChild(ClientStyle.Text("This creature sells nothing yet. Search below to add items.", 13, ClientStyle.Faded));
        }

        foreach (var item in v.Vendor)
        {
            body.AddChild(item.Row is { } row ? ItemCard(item, row) : ReferenceCard(item));
        }

        body.AddChild(new HSeparator());
        body.AddChild(ClientStyle.Text("Add an item", 16, ClientStyle.Header));
        SearchPicker("item", "Find item by name or ID…", async term =>
        {
            var found = await new InteractionSource(Connection()).SearchItemDetailsAsync(term, CancellationToken.None);
            return found.Select(details =>
            {
                var item = details.Info;
                string label = $"{item.Name} ({item.Id}) · {ClientStyle.Money(item.BuyPrice)}";
                iconPaths[label] = ItemIconPath(item);
                tooltips[label] = ItemTooltipBuilder.Build(details);
                return (label, (Action)(() => AddVendorItem(details)));
            }).ToList();
        }, label => iconPaths.GetValueOrDefault(label), label => tooltips.GetValueOrDefault(label));
    }

    /// <summary>The game tooltip of an item the vendor lists, when its template was loaded.</summary>
    Tooltip? ItemTip(uint id) => snapshot?.Lookup.ItemDetails.TryGetValue(id, out var details) == true ? ItemTooltipBuilder.Build(details) : null;

    string ItemIconPath(ItemInfo? info) => info != null && catalog?.ItemIconPath(info.DisplayId) is { } path ? path : ClientStyle.MissingIcon;

    /// <summary>An item that comes from a shared vendor list: shown for context, edited on the list's own vendor.</summary>
    Control ReferenceCard(VendorView item)
    {
        var text = new VBoxContainer();
        text.AddChild(ClientStyle.Text(item.Info?.Name ?? $"Item {item.Item} (missing)", 14, item.Info != null ? ClientStyle.Quality(item.Info.Quality) : ClientStyle.Error));
        text.AddChild(ClientStyle.Text($"From shared vendor list {item.FromReference}. Edit that list to change it.", 12, ClientStyle.Faded));
        return IconRow(ItemIconPath(item.Info), text, 37, ItemTip(item.Item));
    }

    Control ItemCard(VendorView item, Row row)
    {
        var text = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        var head = new HBoxContainer();
        var name = ClientStyle.Text($"{item.Info?.Name ?? "Missing item"} ({item.Item})", 14, item.Edited ? ClientStyle.Edited : item.Info != null ? ClientStyle.Quality(item.Info.Quality) : ClientStyle.Error);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(name);
        var remove = new Button
        {
            Text = "Remove",
            TooltipText = "Stop selling this item."
        };
        remove.Pressed += () => Commit($"Remove vendor item {item.Item}", [Delete(InteractionTables.Vendor, row)]);
        head.AddChild(remove);
        text.AddChild(head);
        string price = item.ExtendedCost != 0 ? catalog?.DescribeExtendedCost(item.ExtendedCost) ?? $"Extended cost {item.ExtendedCost}" : ClientStyle.Money(item.Info?.BuyPrice ?? 0);
        text.AddChild(ClientStyle.Text($"Price: {price}", 12, ClientStyle.Faded));

        var grid = new GridContainer
        {
            Columns = 4
        };
        grid.AddThemeConstantOverride("h_separation", 8);
        text.AddChild(grid);
        void Field(string label, string tip, uint value, Func<uint, IEnumerable<RowEdit>> apply)
        {
            grid.AddChild(Caption(label, tip));
            var edit = new LineEdit
            {
                Text = value.ToString(CultureInfo.InvariantCulture),
                TooltipText = tip,
                CustomMinimumSize = new(70, 0)
            };
            edit.TextSubmitted += input => Try(() => Commit($"Edit vendor item {item.Item}", apply(ParseUInt(input))));
            grid.AddChild(edit);
        }

        Field("Position", "Where the item appears in the vendor window. Lower numbers come first.", (uint)Math.Max(0, item.Slot), n => [Update(InteractionTables.Vendor, row, row.With("slot", (int)n))]);
        Field("Stock limit", "How many the vendor holds. 0 means unlimited. A limited item refills over time (see Restock time).", item.MaxCount, n => [Update(InteractionTables.Vendor, row, row.With("maxcount", n))]);
        Field("Restock time", "Seconds until the stock refills. Only used with a stock limit.", item.IncrTime, n => [Update(InteractionTables.Vendor, row, row.With("incrtime", n))]);
        // ExtendedCost is part of the key: changing it re-inserts the row.
        Field("Alternate price", "ID of an extended cost (honor, arena points, tokens, items) charged instead of gold. 0 sells for the item's gold price.", item.ExtendedCost, n => [Delete(InteractionTables.Vendor, row), Insert(InteractionTables.Vendor, row.With("ExtendedCost", n))]);
        return IconRow(ItemIconPath(item.Info), text, 37, ItemTip(item.Item));
    }

    void AddVendorItem(ItemDetails details)
    {
        var item = details.Info;
        snapshot!.Lookup.Items.TryAdd(item.Id, item);
        snapshot.Lookup.ItemDetails.TryAdd(item.Id, details);
        int slot = effective.All(InteractionTables.Vendor).Where(r => r.U("entry") == snapshot.Entry).Select(r => r.I("slot")).DefaultIfEmpty(-1).Max() + 1;
        Commit($"Add vendor item {item.Id}", [Insert(InteractionTables.Vendor, Row.Of(("entry", snapshot.Entry), ("item", (int)item.Id), ("ExtendedCost", 0u), ("slot", slot), ("maxcount", 0u), ("incrtime", 0u)))]);
    }
}
