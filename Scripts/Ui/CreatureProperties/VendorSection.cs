using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

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

        var renderer = new PageRenderer(Ctx);
        var spec = Ctx.Schema.VendorPage;
        renderer.RenderItems(body, spec, effective, v.Entry, editable, row =>
        {
            var item = v.Vendor.First(i => i.Row != null && spec.Table.KeyOf(i.Row) == spec.Table.KeyOf(row));
            return ItemHeading(item);
        });
        foreach (var item in v.Vendor.Where(i => i.Row == null))
        {
            body.AddChild(ReferenceCard(item));
        }

        body.AddChild(new HSeparator());
        body.AddChild(ClientStyle.Text("Add an item", 16, ClientStyle.Header));
        SearchPicker("item", "Find item by name or ID…", async term =>
        {
            var found = await DBSource.Open(Connection()).SearchItemDetailsAsync(term, CancellationToken.None);
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

    Control ItemHeading(VendorView item)
    {
        var text = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        var name = ClientStyle.Text($"{item.Info?.Name ?? "Missing item"} ({item.Item})", 14, item.Edited ? ClientStyle.Edited : item.Info != null ? ClientStyle.Quality(item.Info.Quality) : ClientStyle.Error);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.AddChild(name);
        string price = item.ExtendedCost != 0 ? catalog?.DescribeExtendedCost(item.ExtendedCost) ?? $"Extended cost {item.ExtendedCost}" : ClientStyle.Money(item.Info?.BuyPrice ?? 0);
        text.AddChild(ClientStyle.Text($"Price: {price}", 12, ClientStyle.Faded));

        return IconRow(ItemIconPath(item.Info), text, 37, ItemTip(item.Item));
    }

    void AddVendorItem(ItemDetails details)
    {
        var item = details.Info;
        snapshot!.Lookup.Items.TryAdd(item.Id, item);
        snapshot.Lookup.ItemDetails.TryAdd(item.Id, details);
        Commit($"Add vendor item {item.Id}", [Ctx.Schema.VendorPage.Add(effective, snapshot.Entry, ("item", checked((int)item.Id)))]);
    }
}
