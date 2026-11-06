using System.Globalization;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// TrinityCore vendor rows (npc_vendor, items and currencies), named from the hotfixes over the retail
/// client, with stock and restock saved together and items added from a client search or by ID.
/// </summary>
public sealed partial class TcVendorSection(CreatureContext ctx) : VendorSection(ctx)
{
    /// <summary>Items of a shared vendor list shown under its reference row.</summary>
    const int SharedShown = 25;

    public override bool Supports(WorldDialect dialect) => dialect == WorldDialect.TrinityCore;

    protected override int Columns => 2;
    protected override bool SearchNeedsClient => true;

    // Stock and restock must be saved together to satisfy the server's paired-value rule.
    protected override ItemListSpec Spec
    {
        get
        {
            var spec = base.Spec;
            return spec with { Fields = spec.Fields.Where(f => f.Column != "incrtime").Select(f => f.Column == "maxcount" ? f with { Label = "Stock / restock", Tip = "How many the vendor holds (0 means unlimited) and the seconds between restocks; for a currency row it is the quantity sold. Both values are saved together." } : f).ToArray() };
        }
    }

    protected override IEnumerable<string> Warnings(ICreatureView v) => TcVendorRules.Check(v.Entry, Ctx.Effective).Select(issue => issue.Message);

    protected override IReadOnlyList<VendorEntry> Entries(ICreatureView v)
    {
        var spec = Ctx.Schema.VendorPage;
        List<VendorEntry> entries = [];
        foreach (var row in spec.Select(Ctx.Effective, v.Entry))
        {
            bool edited = Ctx.Snapshot is { } snapshot && snapshot.Rows.Get(spec.Table, spec.Table.KeyOf(row)) != row;
            if (row.I("item") < 0)
            {
                entries.AddRange(SharedList(row, (uint)-row.I("item"), edited));
                continue;
            }

            var (name, color, icon) = Describe(row.U("type"), row.I("item"));
            entries.Add(new(row, row.I("item").ToString(CultureInfo.InvariantCulture), name, color, icon, row.U("ExtendedCost") != 0 ? $"Extended cost {row.U("ExtendedCost")}" : "", Stock(row), 0, edited, TipOf(row.U("type"), row.I("item"))));
        }

        return entries;
    }

    readonly Dictionary<uint, Tooltip?> tips = [];

    /// <summary>The retail tooltip of an item: the client's template, named and graded as the hotfixes say.</summary>
    Tooltip? TipOf(uint type, int item)
    {
        if (type == 2 || item <= 0)
        {
            return null;
        }

        uint id = (uint)item;
        if (!tips.TryGetValue(id, out var tip))
        {
            tip = null;
            if (Ctx.Lookup()?.ItemDetails(id) is { } details)
            {
                if (Ctx.Snapshot?.Lookup.Item(id) is { } shown)
                {
                    details = details with { Info = details.Info with { Name = shown.Name is { Length: > 0 } ? shown.Name : details.Info.Name, Quality = shown.Quality } };
                }

                tip = ItemTooltipBuilder.Build(details, ItemVocabulary.Retail);
            }

            tips[id] = tip;
        }

        return tip;
    }

    /// <summary>Name, quality colour and icon of an item or currency; name and quality come from the hotfixes, the icon from the client item.</summary>
    (string Name, Color Color, string Icon) Describe(uint type, int item)
    {
        if (type == 2)
        {
            return ($"Currency {item}", ClientStyle.Body, ClientStyle.MissingIcon);
        }

        var shown = item > 0 ? Ctx.Snapshot?.Lookup.Item((uint)item) : null;
        var client = item > 0 ? Ctx.Lookup()?.Item((uint)item) : null;
        string name = shown?.Name is { Length: > 0 } known ? known : client?.Name is { Length: > 0 } clientName ? clientName : $"Item {item}";
        var color = shown != null ? ClientStyle.Quality(shown.Quality) : client != null ? ClientStyle.Quality((int)client.Quality) : ClientStyle.Body;
        // The client names an item's icon by FileDataID, which the retail file store reads like a path.
        return (name, color, client is { IconFileId: > 0 } ? AssetRef.FromFileId(client.IconFileId) : ClientStyle.MissingIcon);
    }

    static string Stock(Row row) => row.U("maxcount") == 0 ? ""
        : row.U("type") == 2 ? $"quantity {row.U("maxcount")}"
        : $"stock {row.U("maxcount")} / restock {row.U("incrtime")} s";

    /// <summary>A shared vendor list: the reference row as a header, then its items (read-only: they belong to the list's own entry).</summary>
    IEnumerable<VendorEntry> SharedList(Row reference, uint list, bool edited)
    {
        var rows = Ctx.Snapshot?.Lookup.ReferenceVendor(list) ?? [];
        yield return new(reference, $"-{list}", $"Shared vendor list {list} · {rows.Count} item(s)", ClientStyle.Gold, ClientStyle.GossipIcon(1), "Edit the list on its own vendor.", "", list, edited, null, Header: true);
        foreach (var item in rows.Take(SharedShown))
        {
            var (name, color, icon) = Describe(item.U("type"), item.I("item"));
            yield return new(null, item.I("item").ToString(CultureInfo.InvariantCulture), name, color, icon, "", Stock(item), list, false, TipOf(item.U("type"), item.I("item")));
        }
    }

    protected override Control? FieldControl(FieldSpec field, Row row) => field.Column == "maxcount" ? StockEditor(row) : null;

    Control StockEditor(Row row)
    {
        var box = new HBoxContainer();
        var count = new LineEdit { Text = row.T("maxcount"), CustomMinimumSize = new(100, 0), TooltipText = "How many the vendor holds before it stops selling; 0 means unlimited. For a currency row, the amount sold." };
        var time = new LineEdit { Text = row.T("incrtime"), CustomMinimumSize = new(100, 0), TooltipText = "How many seconds until the vendor restocks one item after a purchase. Only used for item rows." };
        var save = new Button { Text = "Save stock", TooltipText = "Save both the stock limit and the restock time together." };
        void Save() => Ctx.Try(() => Ctx.Commit("Change vendor stock", ItemListSpec.Change(Ctx.Schema.VendorPage.Table, row,
            row.With("maxcount", uint.Parse(count.Text, CultureInfo.InvariantCulture)).With("incrtime", uint.Parse(time.Text, CultureInfo.InvariantCulture)))));
        save.Pressed += Save;
        count.TextSubmitted += _ => Save();
        time.TextSubmitted += _ => Save();
        box.AddChild(count);
        box.AddChild(time);
        box.AddChild(save);
        return box;
    }

    protected override async Task<IReadOnlyList<(string Label, Action Pick)>> SearchItems(string term)
    {
        if (Ctx.Lookup() is not { } client)
        {
            return [];
        }

        var found = await Task.Run(() => client.SearchItems(term, 30));
        return found.Select(item =>
        {
            string label = $"{item.Name} ({item.Id})";
            if (item.IconFileId > 0)
            {
                ResultIcons[label] = AssetRef.FromFileId(item.IconFileId);
            }

            if (TipOf(1, checked((int)item.Id)) is { } tip)
            {
                ResultTips[label] = tip;
            }

            return (label, (Action)(() => Add(checked((int)item.Id), 1)));
        }).ToList();
    }

    protected override void AddControls(ICreatureView v)
    {
        if (Ctx.Lookup() == null)
        {
            AddChild(ClientStyle.Text("Open a retail client to search and validate item IDs.", 11, ClientStyle.Warning));
        }

        var manual = new HBoxContainer();
        AddChild(manual);
        var id = new LineEdit { PlaceholderText = "Item or currency ID", CustomMinimumSize = new(180, 0), TooltipText = "The item ID, or the currency ID for a currency row." };
        var type = new OptionButton { TooltipText = "Whether the ID names an item or a currency." };
        type.AddItem("Item", 1);
        type.AddItem("Currency", 2);
        var addId = new Button { Text = "Add ID", TooltipText = "Add this item or currency to the vendor." };
        addId.Pressed += () => Ctx.Try(() => Add(int.Parse(id.Text, CultureInfo.InvariantCulture), (uint)type.GetSelectedId()));
        manual.AddChild(id);
        manual.AddChild(type);
        manual.AddChild(addId);
    }

    void Add(int item, uint type) => Ctx.Try(() =>
        Ctx.Commit($"Add vendor {(type == 2 ? "currency" : "item")} {item}",
            [Ctx.Schema.VendorPage.Add(Ctx.Effective, Ctx.View!.Entry, ("item", item), ("type", type), ("maxcount", type == 2 ? 1u : 0u))]));
}
