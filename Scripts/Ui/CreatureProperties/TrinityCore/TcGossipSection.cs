using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;
using ACWorldEdit.Data;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

/// <summary>TC gossip navigation and editing through the dialect's shared descriptors.</summary>
public sealed partial class TcGossipSection(CreatureContext ctx) : TcSection(ctx)
{
    public override string Id => "gossip";
    public override string Title => "Gossip";
    public override string Badge => Ctx.TcView is { } v && v.Menus.Length > 0 ? v.Menus.Count(m => m.Root).ToString(CultureInfo.InvariantCulture) : "";
    public override bool HasContent => Ctx.Editable || Ctx.TcView is { Menus.Length: > 0 };
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
        bool editable = Ctx.Editable;
        var page = Ctx.Schema.GossipPage;
        if (v.Menus.Length == 0)
        {
            if (editable) RootControls(v, null);
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
        if (editable) RootControls(v, menu);
        Heading($"Menu {menu.MenuId}");
        Line("Menus and greetings can be shared by several creatures. Editing them affects every use.", ClientStyle.Faded, 11);
        foreach (var issue in TcGossipRules.Check(Ctx.Effective)) Line(issue.Message, ClientStyle.Warning, 11);
        Heading("Greeting");
        if (menu.Texts.Length == 0)
        {
            Line("No gossip_menu text: the client shows a default greeting.", ClientStyle.Faded, 12);
            if (editable) GreetingControls(menu.MenuId, false, v.Entry);
        }
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
                Line($"{(variant.Text is { } shown ? InteractionText.Render(shown) : $"Broadcast text {variant.BroadcastId} not found")} · weight {Number(variant.Probability)}");
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
                return ClientStyle.Text($"{option.OptionId}. {InteractionText.Render(option.Text)}{trainer}", 13, ClientStyle.Gold);
            }, (field, row) =>
            {
                if (editable && field.Column is "ActionMenuID" or "ActionPoiID" or "OptionBroadcastTextID" or "BoxBroadcastTextID")
                {
                    var edit = new LineEdit { Text = row.T(field.Column), CustomMinimumSize = new(110, 0) };
                    edit.TextSubmitted += text => Ctx.Try(() =>
                    {
                        uint id = uint.Parse(text, CultureInfo.InvariantCulture);
                        _ = SaveReference($"Change {field.Label.ToLowerInvariant()}", ItemListSpec.Change(page.Options.Table, row, row.With(field.Column, id)),
                            id, field.Column switch { "ActionMenuID" => Reference.Menu, "ActionPoiID" => Reference.Poi, _ => Reference.Broadcast });
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
        Line($"{label} broadcast {id}: {(text == null ? "not resolved in hotfixes/client data" : InteractionText.Render(text.Value.Male))}", text == null ? ClientStyle.Warning : ClientStyle.Body, 12, parent);
        if (text is { } variant && variant.Female != variant.Male && variant.Female.Length > 0)
            Line(InteractionText.Render(variant.Female), ClientStyle.Faded, 12, parent);
    }

    enum Reference { Menu, Broadcast, Poi }

    /// <summary>Menus the creature opens with (creature_template_gossip), and a copy for a menu other creatures share.</summary>
    void RootControls(TcCreatureView v, TcMenuView? menu)
    {
        Heading("Creature menus");
        var box = new HBoxContainer();
        AddChild(box);
        GreetingControls(0, true, v.Entry);
        var id = new LineEdit { Name = "AssignMenuId", PlaceholderText = "Menu ID", CustomMinimumSize = new(110, 0) };
        var assign = new Button { Name = "AssignMenu", Text = "Assign menu", TooltipText = "Let this creature open the menu." };
        assign.Pressed += () => Ctx.Try(() =>
        {
            uint menuId = uint.Parse(id.Text, CultureInfo.InvariantCulture);
            _ = SaveReference($"Assign gossip menu {menuId}", [Insert(TcTables.CreatureGossip, Row.Of(("CreatureID", v.Entry), ("MenuID", menuId)))], menuId, Reference.Menu);
        });
        id.TextSubmitted += _ => assign.EmitSignal(BaseButton.SignalName.Pressed);
        box.AddChild(id);
        box.AddChild(assign);
        if (menu is not { Root: true } || RootLink(v.Entry, menu.MenuId) is not { } link) return;
        var remove = new Button { Name = "UnassignMenu", Text = "Unassign menu", TooltipText = "This creature stops opening the menu; the menu itself stays." };
        remove.Pressed += () => Ctx.Commit($"Unassign gossip menu {menu.MenuId}", [Delete(TcTables.CreatureGossip, link)]);
        box.AddChild(remove);
        if (Ctx.Snapshot?.Lookup.MenuUsers(menu.MenuId) > 1)
        {
            var copy = new Button { Name = "CopyMenu", Text = "Copy for this creature", TooltipText = "Give this creature its own copy of the menu and its options. Conditions and scripts that name the menu do not move." };
            copy.Pressed += () => CopyMenu(v.Entry, menu, link);
            box.AddChild(copy);
        }
    }

    /// <summary>
    /// A greeting for a menu with none (a new npc_text row and its gossip_menu link), or, with
    /// <paramref name="newMenu"/>, a whole new menu with that greeting for the creature.
    /// </summary>
    void GreetingControls(uint menu, bool newMenu, uint entry)
    {
        var box = new HBoxContainer();
        AddChild(box);
        var broadcast = new LineEdit { Name = newMenu ? "NewMenuBroadcast" : "AddGreetingBroadcast", PlaceholderText = "Greeting broadcast ID", CustomMinimumSize = new(160, 0), TooltipText = "The broadcast text the greeting plays." };
        var add = new Button { Name = newMenu ? "NewMenu" : "AddGreeting", Text = newMenu ? "+ New menu" : "Add greeting", TooltipText = newMenu ? "Create a menu with this greeting and let the creature open it." : "Give this menu a greeting." };
        add.Pressed += () => Ctx.Try(() =>
        {
            uint broadcastId = uint.Parse(broadcast.Text, CultureInfo.InvariantCulture);
            uint text = Ctx.NextId(TcTables.NpcText, "ID", Ctx.Snapshot!.Lookup.MaxTextId);
            uint target = newMenu ? Math.Max(Ctx.NextId(TcTables.GossipMenu, "MenuID", Ctx.Snapshot.Lookup.MaxMenuId), Ctx.NextId(TcTables.GossipOption, "MenuID", 0)) : menu;
            List<RowEdit> edits = [Insert(TcTables.NpcText, TcGossipRules.NewGreeting(text, broadcastId)), Insert(TcTables.GossipMenu, Row.Of(("MenuID", target), ("TextID", text)))];
            if (newMenu) edits.Add(Insert(TcTables.CreatureGossip, Row.Of(("CreatureID", entry), ("MenuID", target))));
            _ = SaveReference(newMenu ? $"New gossip menu {target}" : "Add greeting", [.. edits], broadcastId, Reference.Broadcast);
            if (newMenu) selected = target;
        });
        broadcast.TextSubmitted += _ => add.EmitSignal(BaseButton.SignalName.Pressed);
        box.AddChild(broadcast);
        box.AddChild(add);
    }

    Row? RootLink(uint entry, uint menu) => Ctx.Effective.All(TcTables.CreatureGossip).FirstOrDefault(r => r.U("CreatureID") == entry && r.U("MenuID") == menu);

    void CopyMenu(uint entry, TcMenuView menu, Row link)
    {
        uint copy = Math.Max(Ctx.NextId(TcTables.GossipMenu, "MenuID", Ctx.Snapshot!.Lookup.MaxMenuId), Ctx.NextId(TcTables.GossipOption, "MenuID", 0));
        List<RowEdit> edits = [];
        edits.AddRange(Ctx.Effective.All(TcTables.GossipMenu).Where(r => r.U("MenuID") == menu.MenuId).Select(r => Insert(TcTables.GossipMenu, r.With("MenuID", copy))));
        edits.AddRange(Ctx.Effective.All(TcTables.GossipOption).Where(r => r.U("MenuID") == menu.MenuId).Select(r => Insert(TcTables.GossipOption, r.With("MenuID", copy))));
        edits.Add(Delete(TcTables.CreatureGossip, link));
        edits.Add(Insert(TcTables.CreatureGossip, Row.Of(("CreatureID", entry), ("MenuID", copy))));
        Ctx.Commit($"Copy gossip menu {menu.MenuId}", edits);
        selected = copy;
    }

    async Task SaveReference(string label, RowEdit[] edits, uint id, Reference kind)
    {
        int current = generation;
        string source = Ctx.SourceKey;
        try
        {
            if (id != 0)
            {
                var db = DBSource.Open(Ctx.Connection(), Ctx.Lookup());
                bool found = kind switch
                {
                    Reference.Menu => Ctx.Effective.All(Ctx.Schema.GossipPage.Menus.Table).Any(r => r.U("MenuID") == id)
                        || Ctx.Effective.All(Ctx.Schema.GossipPage.Options.Table).Any(r => r.U("MenuID") == id)
                        || await db.MenuExistsAsync(id, CancellationToken.None),
                    Reference.Poi => Ctx.Snapshot?.Lookup.PoiExists(id) == true || await db.PoiExistsAsync(id, CancellationToken.None),
                    _ => Ctx.Snapshot?.Lookup.BroadcastText(id) != null
                        || (await db.SearchBroadcastAsync(id.ToString(CultureInfo.InvariantCulture), CancellationToken.None)).Any(r => r.Id == id)
                };
                if (current != generation || source != Ctx.SourceKey) return;
                if (!found)
                {
                    Ctx.Status = $"Not saved: {kind switch { Reference.Menu => "gossip menu", Reference.Poi => "point of interest", _ => "broadcast text" }} {id} was not found.";
                    Ctx.Redraw();
                    return;
                }

                if (kind == Reference.Poi) Ctx.Snapshot?.Lookup.Pois.Add(id);
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
                _ = SaveReference("Change greeting variant", edits, broadcast, Reference.Broadcast);
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
