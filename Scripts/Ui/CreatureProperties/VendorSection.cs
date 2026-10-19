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
    public override string Badge => Ctx.View is { Vendor.Length: > 0 } v ? v.Vendor.Length.ToString() : "";
    public override bool HasContent => Ctx.View is { } v && (Ctx.Editable || v.Vendor.Length > 0);
    const int VendorPageSize = 10;
    int vendorPage;

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
            var holder = IconRow(icon, cell, 37);
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
        foreach (var item in v.Vendor)
        {
            if (item.Row is not { } row)
            {
                body.AddChild(ClientStyle.Text($"{item.Info?.Name ?? $"Item {item.Item}"} · from vendor list {item.FromReference} (edit that vendor)", 13, ClientStyle.Faded));
                continue;
            }

            var line = new HBoxContainer();
            body.AddChild(line);
            line.AddChild(ClientStyle.Text($"{item.Info?.Name ?? "missing"} ({item.Item})", 13, item.Edited ? ClientStyle.Edited : item.Info != null ? ClientStyle.Quality(item.Info.Quality) : ClientStyle.Error));
            LineEdit Field(string tooltip, uint value, Func<uint, IEnumerable<RowEdit>> apply)
            {
                var field = new LineEdit
                {
                    Text = value.ToString(CultureInfo.InvariantCulture),
                    TooltipText = tooltip,
                    CustomMinimumSize = new(58, 0)
                };
                field.TextSubmitted += text => Try(() => Commit($"Edit vendor item {item.Item}", apply(ParseUInt(text))));
                line.AddChild(field);
                return field;
            }

            Field("Max count (0 = unlimited)", item.MaxCount, n => [Update(InteractionTables.Vendor, row, row.With("maxcount", n))]);
            Field("Restock seconds (incrtime)", item.IncrTime, n => [Update(InteractionTables.Vendor, row, row.With("incrtime", n))]);
            // ExtendedCost is part of the key: changing it re-inserts the row.
            Field("Extended cost ID (0 = gold price)", item.ExtendedCost, n => [Delete(InteractionTables.Vendor, row), Insert(InteractionTables.Vendor, row.With("ExtendedCost", n))]);
            var remove = new Button
            {
                Text = "✕",
                TooltipText = "Remove item"
            };
            remove.Pressed += () => Commit($"Remove vendor item {item.Item}", [Delete(InteractionTables.Vendor, row)]);
            line.AddChild(remove);
        }

        SearchPicker("item", "Find item by name or ID…", async term =>
        {
            var found = await new InteractionSource(Connection()).SearchItemsAsync(term, CancellationToken.None);
            return found.Select(item => ($"{item.Name} ({item.Id}) · {ClientStyle.Money(item.BuyPrice)}", (Action)(() => AddVendorItem(item)))).ToList();
        });
    }

    void AddVendorItem(ItemInfo item)
    {
        snapshot!.Lookup.Items.TryAdd(item.Id, item);
        int slot = effective.All(InteractionTables.Vendor).Where(r => r.U("entry") == snapshot.Entry).Select(r => r.I("slot")).DefaultIfEmpty(-1).Max() + 1;
        Commit($"Add vendor item {item.Id}", [Insert(InteractionTables.Vendor, Row.Of(("entry", snapshot.Entry), ("item", (int)item.Id), ("ExtendedCost", 0u), ("slot", slot), ("maxcount", 0u), ("incrtime", 0u)))]);
    }
}
