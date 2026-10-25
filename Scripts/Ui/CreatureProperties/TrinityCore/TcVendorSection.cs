using System.Globalization;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>TC vendor fields and client item search, with compound-key edits through the shared renderer.</summary>
public sealed partial class TcVendorSection(CreatureContext ctx) : TcSection(ctx)
{
    public override string Id => "vendor";
    public override string Title => "Vendor";
    public override string Badge => Ctx.TcView is { Vendor.Length: > 0 } v ? v.Vendor.Length.ToString(CultureInfo.InvariantCulture) : "";
    public override bool HasContent => Ctx.TcView is { } v && (Ctx.TcVendorEditable || v.Vendor.Length > 0);
    string searchTerm = "";
    IReadOnlyList<ClientItem> results = [];
    int searchGeneration;
    bool searching;

    public override void OnCreatureChanged(CreatureSpawn? creature)
    {
        searchGeneration++;
        searchTerm = "";
        results = [];
        searching = false;
    }

    protected override void Populate(TcCreatureView v)
    {
        bool editable = Ctx.TcVendorEditable;
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
            var info = row.U("type") == 1 && row.I("item") > 0 ? Ctx.Lookup()?.Item((uint)row.I("item")) : null;
            string name = row.I("item") < 0 ? $"Shared vendor list {-(long)row.I("item")}" : row.U("type") == 2 ? $"Currency {row.I("item")}" : info?.Name is { Length: > 0 } known ? $"{known} ({row.I("item")})" : $"Item {row.I("item")}";
            return ClientStyle.Text(name, 14, info != null ? ClientStyle.Quality((int)info.Quality) : ClientStyle.Body);
        }, (field, row) => editable && field.Column == "maxcount" ? StockEditor(row) : null, columns: 2);
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
        var box = new HBoxContainer();
        AddChild(box);
        var term = new LineEdit { Name = "Search_item", Text = searchTerm, PlaceholderText = "Find item by name or ID…", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var search = new Button { Text = "Search", Disabled = searching || Ctx.Lookup() == null };
        term.TextChanged += text => searchTerm = text;
        async void Search()
        {
            if (searching || Ctx.Lookup() is not { } client) return;
            string text = term.Text.Trim();
            if (text.Length == 0) return;
            text = text[..Math.Min(80, text.Length)];
            searchTerm = text;
            int generation = ++searchGeneration;
            searching = true;
            Ctx.Status = "Searching items…";
            Ctx.Redraw();
            try
            {
                var found = await Task.Run(() => client.SearchItems(text, 30));
                if (generation != searchGeneration) return;
                results = found;
                Ctx.Status = $"{found.Count} item(s) found.";
            }
            catch (Exception error)
            {
                if (generation != searchGeneration) return;
                Ctx.Status = DbErrors.Safe(error);
            }
            if (generation != searchGeneration) return;
            searching = false;
            Ctx.Redraw();
        }
        search.Pressed += Search;
        term.TextSubmitted += _ => Search();
        box.AddChild(term);
        box.AddChild(search);
        if (Ctx.Lookup() == null) Line("Open a retail client to search and validate item IDs.", ClientStyle.Warning, 11);
        foreach (var item in results)
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
