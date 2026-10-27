using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;
using ACWorldEdit.Data;

namespace ACWorldEdit;

/// <summary>TC gossip navigation and editing through the dialect's shared descriptors.</summary>
public sealed partial class TcGossipSection(CreatureContext ctx) : TcSection(ctx)
{
    public override string Id => "gossip";
    public override string Title => "Gossip";
    public override string Badge => Ctx.TcView is { } v && v.Menus.Length > 0 ? v.Menus.Count(m => m.Root).ToString(CultureInfo.InvariantCulture) : "";
    public override bool HasContent => Ctx.TcEditable || Ctx.TcView is { Menus.Length: > 0 };
    bool greetingExpanded;
    int generation;
    uint? selected;
    readonly Stack<uint> back = new();

    public override void OnCreatureChanged(CreatureSpawn? creature)
    {
        greetingExpanded = false;
        generation++;
        selected = null;
        back.Clear();
    }

    void Navigate(uint menu)
    {
        if (Ctx.TcView?.Menus.Any(m => m.MenuId == menu) != true)
        {
            Ctx.Status = $"Menu {menu} is outside the {TcCreatureView.MaxMenus}-menu navigation limit.";
            Ctx.Redraw();
            return;
        }
        if (selected is { } old && old != menu) back.Push(old);
        selected = menu;
        Ctx.Redraw();
    }

    protected override void Populate(TcCreatureView v)
    {
        bool editable = Ctx.TcEditable;
        var page = Ctx.Schema.GossipPage;
        if (v.Menus.Length == 0)
        {
            Line("This creature has no gossip menu.", ClientStyle.Faded);
            return;
        }
        var menu = v.Menus.FirstOrDefault(m => m.MenuId == selected) ?? v.Menus[0];
        selected = menu.MenuId;
        var nav = new HBoxContainer();
        AddChild(nav);
        var previous = new Button { Text = "Back", Disabled = back.Count == 0 };
        previous.Pressed += () => { selected = back.Pop(); Ctx.Redraw(); };
        nav.AddChild(previous);
        var picker = new OptionButton { Name = "GossipMenuPicker" };
        for (int i = 0; i < v.Menus.Length; i++)
        {
            var item = v.Menus[i];
            picker.AddItem($"Menu {item.MenuId}" + (item.Root ? " · root" : " · submenu"));
            if (item.MenuId == selected) picker.Select(i);
        }
        picker.ItemSelected += i => Navigate(v.Menus[(int)i].MenuId);
        nav.AddChild(picker);
        Heading($"Menu {menu.MenuId}");
        Line("Menus and greetings can be shared by several creatures. Editing them affects every use.", ClientStyle.Faded, 11);
        foreach (var issue in TcGossipRules.Check(Ctx.Effective)) Line(issue.Message, ClientStyle.Warning, 11);
        Heading("Greeting");
        if (menu.Texts.Length == 0) Line("No gossip_menu text: the client shows a default greeting.", ClientStyle.Faded, 12);
        foreach (var text in menu.Texts)
        {
            var row = Ctx.Effective.Get(page.TextTable, text.TextId.ToString(CultureInfo.InvariantCulture));
            if (row == null)
            {
                Line($"Text {text.TextId} is missing from npc_text.", ClientStyle.Error);
                continue;
            }
            Line($"npc_text {text.TextId}", ClientStyle.Gold);
            if (editable)
            {
                var expand = new Button { Name = "EditGreetingVariants", Text = greetingExpanded ? "Hide greeting variants" : "Edit greeting variants" };
                expand.Pressed += () => { greetingExpanded = !greetingExpanded; Ctx.Redraw(); };
                AddChild(expand);
                if (greetingExpanded) Greeting(row);
            }
            if (!editable || !greetingExpanded) foreach (var variant in text.Variants)
                Line($"{variant.Text ?? $"Broadcast text {variant.BroadcastId} not found"} · weight {Number(variant.Probability)}");
        }
        Heading("Options");
        if (editable)
        {
            var add = new Button { Name = "AddGossipOption", Text = "Add option" };
            add.Pressed += () => Ctx.Try(() => Ctx.Commit("Add gossip option", [page.Options.Add(Ctx.Effective, menu.MenuId)]));
            AddChild(add);
        }
        new PageRenderer(Ctx).RenderItems(this, page.Options, Ctx.Effective, menu.MenuId, editable,
            row =>
            {
                var option = menu.Options.First(o => o.OptionId == row.U("OptionID"));
                string trainer = option.TrainerId is { } id ? $" · trainer {id}" : "";
                return ClientStyle.Text($"{option.OptionId}. {option.Text}{trainer}", 13, ClientStyle.Gold);
            }, (field, row) =>
            {
                if (editable && field.Column is "ActionMenuID" or "OptionBroadcastTextID" or "BoxBroadcastTextID")
                {
                    var edit = new LineEdit { Text = row.T(field.Column), CustomMinimumSize = new(110, 0) };
                    edit.TextSubmitted += text => Ctx.Try(() =>
                    {
                        uint id = uint.Parse(text, CultureInfo.InvariantCulture);
                        _ = SaveReference($"Change {field.Label.ToLowerInvariant()}", ItemListSpec.Change(page.Options.Table, row, row.With(field.Column, id)),
                            id, field.Column == "ActionMenuID");
                    });
                    return edit;
                }
                if (editable && field.Column is "OptionText" or "BoxText")
                {
                    var edit = new LineEdit { Text = row.T(field.Column), CustomMinimumSize = new(220, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
                    edit.TextSubmitted += text => Ctx.Commit($"Change {field.Label.ToLowerInvariant()}", field.Column == "OptionText" ? page.ChangeText(row, text) : page.ChangeConfirmation(row, text));
                    return edit;
                }
                return null;
            }, (card, row) =>
            {
                BroadcastPreview(card, row.U("OptionBroadcastTextID"), "Option");
                BroadcastPreview(card, row.U("BoxBroadcastTextID"), "Confirmation");
                if (row.U("ActionMenuID") != 0)
                {
                    var open = new Button { Text = $"Open menu {row.U("ActionMenuID")}" };
                    open.Pressed += () => Navigate(row.U("ActionMenuID"));
                    card.AddChild(open);
                }
            }, columns: 2);
    }

    void BroadcastPreview(Control parent, uint id, string label)
    {
        if (id == 0) return;
        var text = Ctx.Snapshot?.Lookup.BroadcastText(id);
        Line($"{label} broadcast {id}: {text?.Male ?? "not resolved in hotfixes/client data"}", text == null ? ClientStyle.Warning : ClientStyle.Body, 12, parent);
        if (text is { } variant && variant.Female != variant.Male && variant.Female.Length > 0)
            Line(variant.Female, ClientStyle.Faded, 12, parent);
    }

    async Task SaveReference(string label, RowEdit[] edits, uint id, bool menu)
    {
        int current = generation;
        string source = Ctx.SourceKey;
        try
        {
            if (id != 0)
            {
                var db = DBSource.Open(Ctx.Connection(), Ctx.Lookup());
                bool found = menu
                    ? Ctx.Effective.All(Ctx.Schema.GossipPage.Menus.Table).Any(r => r.U("MenuID") == id)
                        || Ctx.Effective.All(Ctx.Schema.GossipPage.Options.Table).Any(r => r.U("MenuID") == id)
                        || await db.MenuExistsAsync(id, CancellationToken.None)
                    : Ctx.Snapshot?.Lookup.BroadcastText(id) != null
                        || (await db.SearchBroadcastAsync(id.ToString(CultureInfo.InvariantCulture), CancellationToken.None)).Any(r => r.Id == id);
                if (current != generation || source != Ctx.SourceKey) return;
                if (!found)
                {
                    Ctx.Status = $"Not saved: {(menu ? "gossip menu" : "broadcast text")} {id} was not found.";
                    Ctx.Redraw();
                    return;
                }
            }
            if (current == generation && source == Ctx.SourceKey) Ctx.Commit(label, edits);
        }
        catch (Exception error)
        {
            if (current != generation || source != Ctx.SourceKey) return;
            Ctx.Status = DbErrors.Safe(error);
            Ctx.Redraw();
        }
    }

    void Greeting(Row row)
    {
        Line("Save each broadcast ID and weight together. 0 / 0 clears a slot; at least one slot needs a positive weight.", ClientStyle.Faded, 11);
        var grid = new GridContainer { Columns = 4 };
        AddChild(grid);
        for (int i = 0; i < 8; i++)
        {
            int slot = i;
            grid.AddChild(ClientStyle.Tag($"Slot {slot}"));
            var id = new LineEdit { Name = $"GreetingBroadcast{slot}", Text = row.T($"BroadcastTextID{slot}"), CustomMinimumSize = new(110, 0), TooltipText = "Broadcast text ID" };
            var weight = new LineEdit { Name = $"GreetingWeight{slot}", Text = row.T($"Probability{slot}"), CustomMinimumSize = new(85, 0), TooltipText = "Relative weight (usually 0 to 1)" };
            var save = new Button { Text = "Save", Name = $"SaveGreeting{slot}" };
            void Save() => Ctx.Try(() =>
            {
                uint broadcast = uint.Parse(id.Text, CultureInfo.InvariantCulture);
                var edits = TcGossipRules.SetVariant(row, slot, broadcast, float.Parse(weight.Text, CultureInfo.InvariantCulture));
                _ = SaveReference("Change greeting variant", edits, broadcast, false);
            });
            save.Pressed += Save;
            id.TextSubmitted += _ => Save();
            weight.TextSubmitted += _ => Save();
            grid.AddChild(id);
            grid.AddChild(weight);
            grid.AddChild(save);
        }
        foreach (int slot in Enumerable.Range(0, 8).Where(i => row.U($"BroadcastTextID{i}") != 0))
            BroadcastPreview(this, row.U($"BroadcastTextID{slot}"), $"Slot {slot}");
    }
}
