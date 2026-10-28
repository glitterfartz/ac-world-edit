using System.Globalization;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>TC vendor fields and client item search, with compound-key edits through the shared renderer.</summary>
public sealed partial class TcVendorSection(CreatureContext ctx) : TcSection(ctx)
{
    public override string Id => PickerPages.Vendor.Id;
    public override string Title => PickerPages.Vendor.Title;
    public override string IconPath => PickerPages.Vendor.IconPath;
    public override string Badge => Ctx.TcView is { Vendor.Length: > 0 } v ? v.Vendor.Length.ToString(CultureInfo.InvariantCulture) : "";
    public override bool HasContent => Ctx.TcView is { } v && (Ctx.Editable || v.Vendor.Length > 0);
    readonly SearchState<ClientItem> search = new();

    public override void OnCreatureChanged(CreatureSpawn? creature) => search.Reset();

    protected override void Populate(TcCreatureView v)
    {
        bool editable = Ctx.Editable;
        if (editable) AddControls(v.Entry);
        if (v.Vendor.Length == 0) Line("This creature sells nothing.", ClientStyle.Faded);
        else Heading($"{v.Vendor.Length} item(s) for sale");

        foreach (var issue in TcVendorRules.Check(v.Entry, Ctx.Effective))
            Line(issue.Message, ClientStyle.Warning, 11);

        var spec = Ctx.Schema.VendorPage;
        // Stock and restock must be saved together to satisfy the server's paired-value rule.
        if (editable) spec = spec with { Fields = spec.Fields.Where(f => f.Column != "incrtime").Select(f => f.Column == "maxcount" ? f with { Label = "Stock / restock", Tip = "Item stock (0 unlimited) and restock seconds, or currency quantity. Save both together." } : f).ToArray() };
        new PageRenderer(Ctx).RenderItems(this, spec, Ctx.Effective, v.Entry, editable, row =>
        {
            // Name and quality come from the snapshot (hotfixes over the client); the icon from the client item.
            var shown = row.U("type") == 1 && row.I("item") > 0 ? Ctx.Snapshot?.Lookup.Item((uint)row.I("item")) : null;
            var info = row.U("type") == 1 && row.I("item") > 0 ? Ctx.Lookup()?.Item((uint)row.I("item")) : null;
            if (row.I("item") < 0) return SharedList((uint)-row.I("item"));
            string name = row.U("type") == 2 ? $"Currency {row.I("item")}" : shown?.Name is { Length: > 0 } known ? $"{known} ({row.I("item")})" : info?.Name is { Length: > 0 } clientName ? $"{clientName} ({row.I("item")})" : $"Item {row.I("item")}";
            var label = ClientStyle.Text(name, 14, shown != null ? ClientStyle.Quality(shown.Quality) : info != null ? ClientStyle.Quality((int)info.Quality) : ClientStyle.Body);
            if (info is not { IconFileId: > 0 }) return label;
            // The client names an item's icon by FileDataID, which the retail file store reads like a path.
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 6);
            line.AddChild(new TextureRect
            {
                Texture = Ctx.Icons.Get(AssetRef.FromFileId(info.IconFileId)),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new(28, 28)
            });
            line.AddChild(label);
            return line;
        }, (field, row) => editable && field.Column == "maxcount" ? StockEditor(row) : null, columns: 2);
    }

    /// <summary>A shared vendor list: its items, read-only (they belong to the list's own entry).</summary>
    Control SharedList(uint list)
    {
        const int Shown = 25;
        var rows = Ctx.Snapshot?.Lookup.ReferenceVendor(list) ?? [];
        var box = new VBoxContainer();
        box.AddChild(ClientStyle.Text($"Shared vendor list {list} · {rows.Count} item(s). Edit the list on its own vendor.", 14, ClientStyle.Gold));
        foreach (var item in rows.Take(Shown))
        {
            uint id = (uint)Math.Max(item.I("item"), 0);
            string name = item.U("type") == 2 ? $"Currency {id}" : Ctx.Snapshot!.Lookup.Item(id)?.Name is { Length: > 0 } known ? $"{known} ({id})" : $"Item {item.I("item")}";
            box.AddChild(ClientStyle.Text(name, 12, ClientStyle.Faded));
        }

        if (rows.Count > Shown) box.AddChild(ClientStyle.Text($"and {rows.Count - Shown} more", 12, ClientStyle.Faded));
        return box;
    }

    Control StockEditor(Row row)
    {
        var box = new HBoxContainer();
        var count = new LineEdit { Text = row.T("maxcount"), CustomMinimumSize = new(100, 0), TooltipText = "Stock limit, or currency quantity" };
        var time = new LineEdit { Text = row.T("incrtime"), CustomMinimumSize = new(100, 0), TooltipText = "Restock seconds (items only)" };
        var save = new Button { Text = "Save stock" };
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

    void AddControls(uint entry)
    {
        Heading("Add an item");
        SearchBox(this, search, "Search_item", "Find item by name or ID…", "item", true, text =>
        {
            var client = Ctx.Lookup()!;
            return Task.Run(() => client.SearchItems(text, 30));
        });
        if (Ctx.Lookup() == null) Line("Open a retail client to search and validate item IDs.", ClientStyle.Warning, 11);
        foreach (var item in search.Results)
        {
            var add = new Button { Text = $"+ {item.Name} ({item.Id})", Alignment = HorizontalAlignment.Left };
            add.Pressed += () => Add(entry, checked((int)item.Id), 1);
            AddChild(add);
        }

        var manual = new HBoxContainer();
        AddChild(manual);
        var id = new LineEdit { PlaceholderText = "Item or currency ID", CustomMinimumSize = new(180, 0) };
        var type = new OptionButton();
        type.AddItem("Item", 1);
        type.AddItem("Currency", 2);
        var addId = new Button { Text = "Add ID" };
        addId.Pressed += () => Ctx.Try(() => Add(entry, int.Parse(id.Text, CultureInfo.InvariantCulture), (uint)type.GetSelectedId()));
        manual.AddChild(id);
        manual.AddChild(type);
        manual.AddChild(addId);
    }

    void Add(uint entry, int item, uint type) => Ctx.Try(() =>
    {
        var spec = Ctx.Schema.VendorPage;
        Ctx.Commit($"Add vendor {(type == 2 ? "currency" : "item")} {item}", [spec.Add(Ctx.Effective, entry, ("item", item), ("type", type), ("maxcount", type == 2 ? 1u : 0u))]);
    });
}
