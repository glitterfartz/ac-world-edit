using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The vendor page of either dialect. Read-only it is a paged two-column grid of client icons with
/// game tooltips; with a draft open it is the item-list editor and an item search whose results carry
/// icons. A dialect subclass turns its read model into <see cref="VendorEntry"/> values and supplies the
/// item search and the controls only it has.
/// </summary>
public abstract partial class VendorSection(CreatureContext ctx) : CreatureSection(ctx)
{
    /// <summary>
    /// One thing the creature sells, as drawn. <see cref="Row"/> is the npc_vendor row (null for an item
    /// that comes from a shared vendor list); <see cref="Header"/> marks the row that references such a
    /// list, which the editor shows as a card and the grid leaves out.
    /// </summary>
    protected sealed record VendorEntry(Row? Row, string Id, string Name, Color Color, string Icon, string Price, string Stock, uint FromList, bool Edited, Tooltip? Tip, bool Header = false);

    const int PageSize = 10;
    int page;

    // Icon and tooltip of each search result, by its label.
    protected readonly Dictionary<string, string> ResultIcons = [];
    protected readonly Dictionary<string, Tooltip> ResultTips = [];

    public override string Id => PickerPages.Vendor.Id;
    public override string Title => PickerPages.Vendor.Title;
    public override string IconPath => PickerPages.Vendor.IconPath;
    public override string Tip => "The items or currencies this creature sells, with their position, price, stock limit and restock time.";
    public override string Badge
    {
        get
        {
            int count = Ctx.View is { } v ? Entries(v).Count(e => !e.Header) : 0;
            return count > 0 ? count.ToString(CultureInfo.InvariantCulture) : "";
        }
    }

    public override bool HasContent => Ctx.View is { } v && (Ctx.Editable || Entries(v).Count > 0);

    /// <summary>What the creature sells, in display order.</summary>
    protected abstract IReadOnlyList<VendorEntry> Entries(ICreatureView v);

    /// <summary>Search the dialect's items; each result is a label and what picking it does.</summary>
    protected abstract Task<IReadOnlyList<(string Label, Action Pick)>> SearchItems(string term);

    /// <summary>The item-list descriptor the editor draws (a dialect can adjust its fields).</summary>
    protected virtual ItemListSpec Spec => Ctx.Schema.VendorPage;

    /// <summary>Columns the editor lays an item's fields out in.</summary>
    protected virtual int Columns => 4;

    /// <summary>A specialised editor for one field of an item, or null for the standard one.</summary>
    protected virtual Control? FieldControl(FieldSpec field, Row row) => null;

    /// <summary>Problems to show above the items.</summary>
    protected virtual IEnumerable<string> Warnings(ICreatureView v) => [];

    /// <summary>Text appended to the grid's page line.</summary>
    protected virtual string Footnote(ICreatureView v) => "";

    /// <summary>Whether the item search needs the client (to name and check items).</summary>
    protected virtual bool SearchNeedsClient => false;

    /// <summary>Controls that follow the item search ("Add an item"), such as adding by ID.</summary>
    protected virtual void AddControls(ICreatureView v)
    {
    }

    public override void OnShown(object? argument) => page = 0;

    public override void OnCreatureChanged(CreatureSpawn? creature)
    {
        page = 0;
        foreach (var state in pickers.Values)
        {
            state.Reset();
        }
    }

    public override void Refresh()
    {
        Clear();
        if (Ctx.View is not { } v)
        {
            AddChild(Placeholder());
            return;
        }

        foreach (string warning in Warnings(v))
        {
            AddChild(ClientStyle.Text(warning, 11, ClientStyle.Warning));
        }

        if (Ctx.Editable)
        {
            RenderEditor(v);
        }
        else
        {
            RenderGrid(v);
        }
    }

    void RenderGrid(ICreatureView v)
    {
        var shown = Entries(v).Where(e => !e.Header).ToList();
        int pages = Math.Max(1, (shown.Count + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 0, pages - 1);
        var grid = new GridContainer
        {
            Columns = 2
        };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 8);
        AddChild(grid);
        foreach (var item in shown.Skip(page * PageSize).Take(PageSize))
        {
            var cell = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            cell.AddChild(ClientStyle.Text(item.Name, 14, item.Color));
            string detail = string.Join(" · ", new[] { item.Price, item.Stock, item.FromList != 0 ? $"list {item.FromList}" : "" }.Where(part => part.Length > 0));
            cell.AddChild(ClientStyle.Text(detail, 12, item.Edited ? ClientStyle.Edited : ClientStyle.Faded));
            var holder = IconRow(item.Icon, cell, 37, item.Tip);
            holder.CustomMinimumSize = new(205, 44);
            grid.AddChild(holder);
        }

        AddChild(ClientStyle.Text($"Page {page + 1} of {pages} · {shown.Count} items{Footnote(v)}", 11, ClientStyle.Faded));
        var nav = new HBoxContainer();
        AddChild(nav);
        var previous = new Button
        {
            Text = "◂ Prev",
            Disabled = page == 0
        };
        previous.Pressed += () =>
        {
            page--;
            Ctx.Redraw();
        };
        var next = new Button
        {
            Text = "Next ▸",
            Disabled = page >= pages - 1
        };
        next.Pressed += () =>
        {
            page++;
            Ctx.Redraw();
        };
        nav.AddChild(previous);
        nav.AddChild(next);
    }

    void RenderEditor(ICreatureView v)
    {
        var entries = Entries(v);
        AddChild(ClientStyle.Text("Items for sale", 16, ClientStyle.Header));
        if (entries.Count == 0)
        {
            AddChild(ClientStyle.Text("This creature sells nothing yet. Search below to add items.", 13, ClientStyle.Faded));
        }

        var spec = Spec;
        new PageRenderer(Ctx).RenderItems(this, spec, Ctx.Effective, v.Entry, true,
            row => ItemHeading(entries.FirstOrDefault(e => e.Row != null && spec.Table.KeyOf(e.Row) == spec.Table.KeyOf(row))),
            FieldControl, columns: Columns);
        foreach (var item in entries.Where(e => e.Row == null))
        {
            AddChild(ReferenceCard(item));
        }

        AddChild(new HSeparator());
        AddChild(ClientStyle.Text("Add an item", 16, ClientStyle.Header));
        SearchPicker(this, "item", "Find item by name or ID…", SearchItems, label => ResultIcons.GetValueOrDefault(label), label => ResultTips.GetValueOrDefault(label), SearchNeedsClient, "Find an item or currency to add to this vendor.");
        AddControls(v);
    }

    Control ItemHeading(VendorEntry? item)
    {
        if (item == null)
        {
            return ClientStyle.Text("Item", 14);
        }

        var text = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        var name = ClientStyle.Text(item.Header ? item.Name : $"{item.Name} ({item.Id})", 14, item.Edited ? ClientStyle.Edited : item.Color);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.AddChild(name);
        if (item.Price.Length > 0)
        {
            text.AddChild(ClientStyle.Text(item.Header ? item.Price : $"Price: {item.Price}", 12, ClientStyle.Faded));
        }

        return IconRow(item.Icon, text, 37, item.Tip);
    }

    /// <summary>An item that comes from a shared vendor list: shown for context, edited on the list's own vendor.</summary>
    Control ReferenceCard(VendorEntry item)
    {
        var text = new VBoxContainer();
        text.AddChild(ClientStyle.Text(item.Name, 14, item.Color));
        text.AddChild(ClientStyle.Text($"From shared vendor list {item.FromList}. Edit that list to change it.", 12, ClientStyle.Faded));
        return IconRow(item.Icon, text, 37, item.Tip);
    }
}
