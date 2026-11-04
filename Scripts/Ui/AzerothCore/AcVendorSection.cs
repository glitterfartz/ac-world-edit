using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>AzerothCore vendor rows (npc_vendor, with shared lists expanded), priced from item_template and the extended-cost catalog.</summary>
public sealed partial class AcVendorSection(CreatureContext ctx) : VendorSection(ctx)
{
    protected override IReadOnlyList<VendorEntry> Entries(ICreatureView v) =>
        v is AcCreatureView ac ? ac.Vendor.Select(Entry).ToList() : [];

    VendorEntry Entry(VendorView item) => new(item.Row, item.Item.ToString(CultureInfo.InvariantCulture), item.Info?.Name ?? "Missing item",
        item.Info != null ? ClientStyle.Quality(item.Info.Quality) : ClientStyle.Error, IconOf(item.Info), Price(item),
        item.MaxCount > 0 ? $"stock {item.MaxCount}" : "", item.FromReference, item.Edited, TipOf(item.Item));

    string Price(VendorView item) => item.ExtendedCost != 0
        ? Ctx.Catalog?.DescribeExtendedCost(item.ExtendedCost) ?? $"Extended cost {item.ExtendedCost}"
        : ClientStyle.Money(item.Info?.BuyPrice ?? 0);

    string IconOf(ItemInfo? info) => info != null && Ctx.Catalog?.ItemIconPath(info.DisplayId) is { } path ? path : ClientStyle.MissingIcon;

    /// <summary>The game tooltip of an item the vendor lists, when its template was loaded.</summary>
    Tooltip? TipOf(uint id) => Ctx.Snapshot?.Lookup.ItemDetails.TryGetValue(id, out var details) == true ? ItemTooltipBuilder.Build(details) : null;

    protected override string Footnote(ICreatureView v)
    {
        int conditions = v is AcCreatureView ac ? ac.Conditions.GetValueOrDefault(23) : 0;
        return $"{(conditions > 0 ? $" · {conditions} conditional" : "")} · event-only items (game_event_npc_vendor) are not shown.";
    }

    protected override async Task<IReadOnlyList<(string Label, Action Pick)>> SearchItems(string term)
    {
        var found = await DBSource.Open(Ctx.Connection()).SearchItemDetailsAsync(term, CancellationToken.None);
        return found.Select(details =>
        {
            var item = details.Info;
            string label = $"{item.Name} ({item.Id}) · {ClientStyle.Money(item.BuyPrice)}";
            ResultIcons[label] = IconOf(item);
            ResultTips[label] = ItemTooltipBuilder.Build(details);
            return (label, (Action)(() => Add(details)));
        }).ToList();
    }

    void Add(ItemDetails details)
    {
        var item = details.Info;
        var snapshot = Ctx.Snapshot!;
        snapshot.Lookup.Items.TryAdd(item.Id, item);
        snapshot.Lookup.ItemDetails.TryAdd(item.Id, details);
        Ctx.Commit($"Add vendor item {item.Id}", [Ctx.Schema.VendorPage.Add(Ctx.Effective, snapshot.Entry, ("item", checked((int)item.Id)))]);
    }
}
