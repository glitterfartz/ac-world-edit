using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;
using ACWorldEdit.Data;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

/// <summary>TC gossip navigation and editing through the dialect's shared descriptors.</summary>
public sealed partial class TcGossipSection(CreatureContext ctx) : TcCreatureSection(ctx)
{
    public override string Id => PickerPages.Gossip.Id;
    public override string Title => PickerPages.Gossip.Title;
    public override string IconPath => PickerPages.Gossip.IconPath;
    public override string Tip => "The greeting text and the options players see when they talk to the creature, including where each option leads.";
    public override string Badge => Ctx.TcView is { } v && v.Menus.Length > 0 ? v.Menus.Count(m => m.Root).ToString(CultureInfo.InvariantCulture) : "";
    public override bool HasContent => Ctx.Editable || Ctx.TcView is { Menus.Length: > 0 };
    bool greetingExpanded;
    readonly HashSet<uint> advancedOptions = [];
    int generation;
    uint? selected;
    readonly Stack<uint> back = new();

    public override void OnCreatureChanged(CreatureSpawn? creature)
    {
        greetingExpanded = false;
        advancedOptions.Clear();
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
            RenderCreateMenu(v);
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
        if (editable)
        {
            RootControls(v, menu);
        }

        Heading($"Menu {menu.MenuId}");
        Line("A menu is what the creature says when you right-click it. Its greeting is the text, and each option is a clickable line that can open another menu or a service.", ClientStyle.Faded, 11);
        if (Ctx.Snapshot?.Lookup.MenuUsers(menu.MenuId) > 1)
        {
            Line("This menu is shared by several creatures; editing it changes all of them.", ClientStyle.Warning, 11);
        }

        foreach (var issue in TcGossipRules.Check(Ctx.Effective))
        {
            Line(issue.Message, ClientStyle.Warning, 11);
        }

        Heading("Greeting");
        RenderGreeting(menu, v, editable);

        Heading("Options");
        if (editable)
        {
            RenderOptionActions(v, menu);
            var fields = new PageFields(Ctx);
            foreach (var option in menu.Options)
            {
                var row = page.Options.Select(Ctx.Effective, menu.MenuId).FirstOrDefault(r => r.U("OptionID") == option.OptionId);
                if (row != null) RenderOption(menu, option, row, fields);
            }
        }
        else
        {
            ReadOptions(menu);
        }
    }

    /// <summary>
    /// The greeting reads as the client shows it, with a plain text box for writing a new one. The
    /// eight weighted broadcast variants stay available under "Edit greeting variants" for parity.
    /// </summary>
    void RenderGreeting(TcMenuView menu, TcCreatureView v, bool editable)
    {
        var page = Ctx.Schema.GossipPage;
        foreach (var text in menu.Texts)
        {
            var row = Ctx.Effective.Get(page.TextTable, text.TextId.ToString(CultureInfo.InvariantCulture));
            if (text.Variants.Length == 0 || text.Variants.All(x => x.Text == null))
            {
                Line("No greeting text is set; the client shows a default line.", ClientStyle.Faded, 12);
            }

            foreach (var variant in text.Variants)
            {
                Line(variant.Text is { } shown ? InteractionText.Render(shown) : $"Broadcast text {variant.BroadcastId} not found", variant.Text == null ? ClientStyle.Warning : ClientStyle.Body, 15);
            }

            if (!editable || row == null)
            {
                continue;
            }

            var expand = new Button { Name = "EditGreetingVariants", Text = greetingExpanded ? "Hide greeting variants" : "Edit greeting variants", TooltipText = "TrinityCore stores up to eight weighted broadcast texts for one greeting. Most menus only need the text box above." };
            expand.Pressed += () => { greetingExpanded = !greetingExpanded; Ctx.Redraw(); };
            AddChild(expand);
            if (greetingExpanded)
            {
                Greeting(row);
            }
        }

        if (!editable)
        {
            return;
        }

        var box = new TextEdit
        {
            Name = "NewGreetingText",
            PlaceholderText = menu.Texts.Length == 0 ? "Type the greeting players see…" : "Type a new greeting to replace the current first line…",
            CustomMinimumSize = new(0, 70),
            WrapMode = TextEdit.LineWrappingMode.Boundary
        };
        AddChild(box);
        var save = new Button { Name = "SaveGreetingText", Text = menu.Texts.Length == 0 ? "Add greeting" : "Save greeting", TooltipText = "Write this as a new broadcast text and make it the menu's greeting." };
        save.Pressed += () => Ctx.Try(() =>
        {
            if (box.Text.Trim() is { Length: > 0 } entered)
            {
                SaveGreeting(menu, menu.Texts.FirstOrDefault() is { } first ? Ctx.Effective.Get(page.TextTable, first.TextId.ToString(CultureInfo.InvariantCulture)) : null, entered);
            }
        });
        AddChild(save);
    }

    /// <summary>Write a greeting as a new broadcast text and make it the first line of the menu's npc_text.</summary>
    void SaveGreeting(TcMenuView menu, Row? row, string text)
    {
        if (!NewText(text, out uint broadcast, out var created))
        {
            return;
        }

        List<RowEdit> edits = [.. created];
        if (row != null)
        {
            var after = row;
            for (int i = 0; i < 8; i++)
            {
                after = after.With($"BroadcastTextID{i}", i == 0 ? broadcast : 0u).With($"Probability{i}", i == 0 ? 1f : 0f);
            }

            edits.Add(InteractionEdits.Update(Ctx.Schema.GossipPage.TextTable, row, after));
        }
        else
        {
            uint textId = Ctx.NextId(TcTables.NpcText, "ID", Ctx.Snapshot!.Lookup.MaxTextId);
            edits.Add(Insert(TcTables.NpcText, TcGossipRules.NewGreeting(textId, broadcast)));
            edits.Add(Insert(TcTables.GossipMenu, Row.Of(("MenuID", menu.MenuId), ("TextID", textId))));
        }

        _ = SaveReference("Save greeting", [.. edits], 0, Reference.Broadcast);
    }

    /// <summary>The "create a menu" card when the creature has none, or a "new menu" button beside an existing one.</summary>
    void RenderCreateMenu(TcCreatureView v)
    {
        Heading("Gossip menu");
        Line("This creature has no gossip menu. Create one and write the greeting players see; options can be added after it.", ClientStyle.Faded, 12);
        if (!Ctx.Editable)
        {
            return;
        }

        var text = new TextEdit
        {
            Name = "NewMenuText",
            PlaceholderText = "Greetings, $N.",
            CustomMinimumSize = new(0, 70),
            WrapMode = TextEdit.LineWrappingMode.Boundary
        };
        AddChild(text);
        var create = new Button { Name = "NewMenu", Text = "Create gossip menu", TooltipText = "Create a menu with this greeting and make the creature open it." };
        create.Pressed += () => Ctx.Try(() =>
        {
            if (text.Text.Trim() is { Length: > 0 } greeting)
            {
                CreateMenu(v, greeting);
            }
        });
        AddChild(create);
    }

    void CreateMenu(TcCreatureView v, string greeting)
    {
        if (!NewText(greeting, out uint broadcast, out var created))
        {
            return;
        }

        uint text = Ctx.NextId(TcTables.NpcText, "ID", Ctx.Snapshot!.Lookup.MaxTextId);
        uint menu = NewMenuId();
        List<RowEdit> edits =
        [
            .. created,
            Insert(TcTables.NpcText, TcGossipRules.NewGreeting(text, broadcast)),
            Insert(TcTables.GossipMenu, Row.Of(("MenuID", menu), ("TextID", text))),
            Insert(TcTables.CreatureGossip, Row.Of(("CreatureID", v.Entry), ("MenuID", menu)))
        ];
        EnsureGossipFlag(v, edits);
        _ = SaveReference($"New gossip menu {menu}", [.. edits], 0, Reference.Broadcast);
        selected = menu;
    }

    /// <summary>The options as the client lists them: an icon and the option text, which a click follows.</summary>
    void ReadOptions(TcMenuView menu)
    {
        var page = Ctx.Schema.GossipPage;
        if (menu.Options.Length == 0) Line("No options.", ClientStyle.Faded, 12);
        var rows = page.Options.Select(Ctx.Effective, menu.MenuId).ToList();
        foreach (var option in menu.Options)
        {
            var row = rows.FirstOrDefault(r => r.U("OptionID") == option.OptionId);
            string target = option.ActionMenuId != 0 ? $" → menu {option.ActionMenuId}" : option.TrainerId is { } trainer ? $" → trainer {trainer}" : "";
            var button = new Button
            {
                Text = InteractionText.Render(option.Text),
                Alignment = HorizontalAlignment.Left,
                TooltipText = $"Option {option.OptionId} · {TcCreatureView.OptionNpcName(option.OptionNpc)}{target}"
            };
            button.Pressed += () => Follow(option, row);
            AddChild(IconRow(ClientStyle.GossipIcon(row == null ? 0 : page.IconOf(row)), button));
        }
    }

    /// <summary>
    /// Do what the client would for an option: open its submenu or the matching service page, or say which
    /// client window it opens. A confirmation box is shown first.
    /// </summary>
    void Follow(TcOptionView option, Row? row)
    {
        Ctx.Status = option.BoxText.Length > 0 || option.BoxMoney > 0
            ? $"Confirmation: \"{InteractionText.Render(option.BoxText)}\"{(option.BoxMoney > 0 ? $" · costs {ClientStyle.Money((long)option.BoxMoney)}" : "")}{(option.Coded ? " · asks for a code" : "")}. "
            : "";
        if (option.ActionMenuId != 0)
        {
            Navigate(option.ActionMenuId);
            return;
        }

        Ctx.Status += OpenService(row == null ? GossipService.Chat : Ctx.Schema.GossipPage.ServiceOf(row), TcCreatureView.OptionNpcName(option.OptionNpc));
        Ctx.Redraw();
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

    /// <summary>Assign an existing menu, create a new one, and remove or copy this creature's root link.</summary>
    void RootControls(TcCreatureView v, TcMenuView menu)
    {
        Heading("Creature menus");
        var box = new HBoxContainer();
        AddChild(box);
        var id = new LineEdit { Name = "AssignMenuId", PlaceholderText = "Menu ID", CustomMinimumSize = new(110, 0) };
        var find = new Button { Name = "FindMenu", Text = "Find…", TooltipText = "Search the existing gossip menus by ID or greeting text." };
        var assign = new Button { Name = "AssignMenu", Text = "Use existing menu", TooltipText = "Let this creature open the menu." };
        assign.Pressed += () => Ctx.Try(() =>
        {
            uint menuId = uint.Parse(id.Text, CultureInfo.InvariantCulture);
            List<RowEdit> edits = [Insert(TcTables.CreatureGossip, Row.Of(("CreatureID", v.Entry), ("MenuID", menuId)))];
            EnsureGossipFlag(v, edits);
            _ = SaveReference($"Assign gossip menu {menuId}", [.. edits], menuId, Reference.Menu);
        });
        id.TextSubmitted += _ => assign.EmitSignal(BaseButton.SignalName.Pressed);
        find.Pressed += () => ChooseMenu(picked =>
        {
            id.Text = picked.ToString(CultureInfo.InvariantCulture);
            assign.EmitSignal(BaseButton.SignalName.Pressed);
        });
        box.AddChild(id);
        box.AddChild(find);
        box.AddChild(assign);
        var create = new HBoxContainer();
        AddChild(create);
        var greeting = new LineEdit { Name = "NewMenuText", PlaceholderText = "Write a greeting for a new menu…", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var newMenu = new Button { Name = "NewMenu", Text = "+ New menu", TooltipText = "Create a menu with this greeting and make this creature open it." };
        newMenu.Pressed += () => Ctx.Try(() =>
        {
            if (greeting.Text.Trim() is { Length: > 0 } entered)
            {
                CreateMenu(v, entered);
            }
        });
        greeting.TextSubmitted += _ => newMenu.EmitSignal(BaseButton.SignalName.Pressed);
        create.AddChild(greeting);
        create.AddChild(newMenu);
        if (menu is not { Root: true } || RootLink(v.Entry, menu.MenuId) is not { } link) return;
        var remove = new Button { Name = "UnassignMenu", Text = "Stop using menu", TooltipText = "This creature stops opening the menu; the menu itself stays." };
        remove.Pressed += () => Ctx.Commit($"Unassign gossip menu {menu.MenuId}", [Delete(TcTables.CreatureGossip, link)]);
        box.AddChild(remove);
        if (Ctx.Snapshot?.Lookup.MenuUsers(menu.MenuId) > 1)
        {
            var copy = new Button { Name = "CopyMenu", Text = "Copy for this creature", TooltipText = "Give this creature its own copy of the menu and its options. Its conditions are copied, and this creature's own gossip-select SmartAI rules follow the copy." };
            copy.Pressed += () => CopyMenu(v.Entry, menu, link);
            box.AddChild(copy);
        }
    }

    /// <summary>Add, remove and chain options; the card below each option edits its text and target.</summary>
    void RenderOptionActions(TcCreatureView v, TcMenuView menu)
    {
        var actions = new HBoxContainer();
        AddChild(actions);
        var add = new Button { Name = "AddGossipOption", Text = "+ Add option" };
        add.Pressed += () => Ctx.Try(() => Ctx.Commit("Add gossip option", [Ctx.Schema.GossipPage.Options.Add(Ctx.Effective, menu.MenuId)]));
        actions.AddChild(add);
        var withMenu = new Button { Name = "AddSubmenuOption", Text = "+ Add option → new submenu", TooltipText = "Write the submenu's greeting, then add an option that opens it." };
        withMenu.Pressed += () => PromptText("Write the submenu's greeting", greeting => Ctx.Try(() =>
        {
            if (!NewText(greeting, out uint broadcast, out var created)) return;
            uint text = Ctx.NextId(TcTables.NpcText, "ID", Ctx.Snapshot!.Lookup.MaxTextId);
            uint submenu = NewMenuId();
            _ = SaveReference("Add option with new submenu",
            [
                .. created,
                Insert(TcTables.NpcText, TcGossipRules.NewGreeting(text, broadcast)),
                Insert(TcTables.GossipMenu, Row.Of(("MenuID", submenu), ("TextID", text))),
                Ctx.Schema.GossipPage.Options.Add(Ctx.Effective, menu.MenuId, ("OptionText", "Tell me more."), ("ActionMenuID", submenu))
            ], 0, Reference.Broadcast);
        }));
        actions.AddChild(withMenu);
    }

    /// <summary>One option as the client shows it, with its target and confirmation next to the text.</summary>
    void RenderOption(TcMenuView menu, TcOptionView option, Row row, PageFields fields)
    {
        var page = Ctx.Schema.GossipPage;
        var card = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        card.AddThemeConstantOverride("separation", 4);
        AddChild(card);
        var head = new HBoxContainer();
        card.AddChild(head);
        var title = IconRow(ClientStyle.GossipIcon(page.IconOf(row)), ClientStyle.Text($"{option.OptionId}. {InteractionText.Render(option.Text)}", 15, ClientStyle.Gold));
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        var up = new Button { Text = "▲", TooltipText = "Move up", Disabled = option == menu.Options[0] };
        up.Pressed += () => MoveOption(menu, option, -1);
        head.AddChild(up);
        var down = new Button { Text = "▼", TooltipText = "Move down", Disabled = option == menu.Options[^1] };
        down.Pressed += () => MoveOption(menu, option, 1);
        head.AddChild(down);
        var remove = new Button { Text = "Remove", TooltipText = "Delete this option." };
        remove.Pressed += () => Ctx.Commit("Remove gossip option", page.Options.Remove(row, Ctx.Effective));
        head.AddChild(remove);

        var text = new LineEdit { Text = row.T("OptionText"), PlaceholderText = option.Text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.TextSubmitted += value => Ctx.Commit("Edit option text", page.ChangeText(row, value));
        var useBroadcast = new Button { Text = "Use broadcast…", TooltipText = "Pick a broadcast text that replaces the literal text in game." };
        useBroadcast.Pressed += () => ChooseBroadcast(id => SetOptionField(row, "OptionBroadcastTextID", id));
        var write = new Button { Text = "Write…", TooltipText = "Write a new broadcast text for this option." };
        write.Pressed += () => PromptText("Write the option text", value => Ctx.Try(() =>
        {
            if (NewText(value, out uint id, out var created))
                Ctx.Commit("Write option text", [.. created, .. ItemListSpec.Change(page.Options.Table, row, row.With("OptionBroadcastTextID", id))]);
        }));
        card.AddChild(EditorFields.Row("Text", "The line the player clicks. A broadcast text replaces the literal text in game.", text, useBroadcast, write));
        if (row.U("OptionBroadcastTextID") != 0)
        {
            BroadcastPreview(card, row.U("OptionBroadcastTextID"), "Option");
        }

        var service = EditorFields.Choice(TcCreatureView.OptionNpcNames.Select((name, id) => ((uint)id, name)), row.U("OptionNpc"),
            value => SetOptionField(row, "OptionNpc", value), "What the client opens when the player chooses this option.");
        card.AddChild(EditorFields.Row("When chosen", "The service or client window this option opens. A submenu takes priority over a service.", service));

        var submenu = new LineEdit { Text = row.T("ActionMenuID"), CustomMinimumSize = new(90, 0) };
        submenu.TextSubmitted += value => Ctx.Try(() => _ = SetSubmenu(row, uint.Parse(value, CultureInfo.InvariantCulture)));
        var chooseMenu = new Button { Text = "Choose…", TooltipText = "Search gossip menus and chain this option to one." };
        chooseMenu.Pressed += () => ChooseMenu(picked =>
        {
            submenu.Text = picked.ToString(CultureInfo.InvariantCulture);
            Ctx.Try(() => _ = SetSubmenu(row, picked));
        }, row.U("ActionMenuID") is var now and not 0 ? now : null);
        card.AddChild(EditorFields.Row("Goes to", "The next menu this option opens. Leave 0 when the service should open instead.", submenu, chooseMenu));
        if (row.U("ActionMenuID") != 0)
        {
            var open = new Button { Text = $"Open menu {row.U("ActionMenuID")}" };
            open.Pressed += () => Navigate(row.U("ActionMenuID"));
            card.AddChild(open);
        }

        var box = new LineEdit { Text = row.T("BoxText"), PlaceholderText = "No confirmation", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.TextSubmitted += value => Ctx.Commit("Change confirmation text", page.ChangeConfirmation(row, value));
        var money = EditorFields.Number((long)row.UL("BoxMoney"), value => SetOptionField(row, "BoxMoney", (ulong)Math.Max(0, value)), 90,
            "How much money the confirmation asks for, in copper (100 copper = 1 silver, 100 silver = 1 gold).");
        var coded = new CheckBox { ButtonPressed = row.U("BoxCoded") != 0, TooltipText = "Ask the player to type a code before the option runs." };
        coded.Toggled += on => SetOptionField(row, "BoxCoded", on ? 1u : 0u);
        card.AddChild(EditorFields.Row("Confirmation", "The dialog shown before the option runs, and what it costs.", box, ClientStyle.Tag("cost"), money, coded));
        if (row.U("BoxBroadcastTextID") != 0)
        {
            BroadcastPreview(card, row.U("BoxBroadcastTextID"), "Confirmation");
        }

        bool shown = advancedOptions.Contains(option.OptionId);
        var advanced = new Button { Text = shown ? "Hide advanced fields" : "Advanced…", TooltipText = "Client IDs, language, flags and the spell cast by this option." };
        advanced.Pressed += () =>
        {
            if (!advancedOptions.Remove(option.OptionId)) advancedOptions.Add(option.OptionId);
            Ctx.Redraw();
        };
        card.AddChild(advanced);
        if (shown)
        {
            var details = new VBoxContainer();
            card.AddChild(details);
            foreach (var field in page.Options.Fields.Where(f => f.Column is "GossipOptionID" or "Language" or "Flags" or "GossipNpcOptionID" or "SpellID" or "OverrideIconID" or "ActionPoiID"))
            {
                if (field.Kind == FieldKind.Flags)
                {
                    var flags = new VBoxContainer();
                    fields.Flags(flags, field, row, true);
                    details.AddChild(flags);
                }
                else
                {
                    details.AddChild(EditorFields.Row(field.Label, field.Tip, fields.Editor(field, row)));
                }
            }
        }

        card.AddChild(new HSeparator());
    }

    void SetOptionField(Row row, string column, object value) =>
        Ctx.Commit($"Change {column}", ItemListSpec.Change(Ctx.Schema.GossipPage.Options.Table, row, row.With(column, value)));

    async Task SetSubmenu(Row option, uint menu)
    {
        if (menu != 0 && !Ctx.Effective.All(Ctx.Schema.GossipPage.Menus.Table).Any(r => r.U("MenuID") == menu) && !Ctx.Snapshot!.Lookup.MenuExists(menu))
        {
            Ctx.Status = $"Checking menu {menu}…";
            Ctx.Redraw();
            if (!await DBSource.Open(Ctx.Connection()).MenuExistsAsync(menu, CancellationToken.None))
            {
                Ctx.Status = $"Not saved: gossip menu {menu} does not exist.";
                Ctx.Redraw();
                return;
            }

            Ctx.Snapshot!.Lookup.Menus.Add(menu);
        }

        Ctx.Commit("Change option submenu", [Update(Ctx.Schema.GossipPage.Options.Table, option, option.With("ActionMenuID", menu))]);
    }

    /// <summary>
    /// Swap two options' OptionID (the display order). OptionID is part of the key, so this is a
    /// delete and re-insert of both rows in one command.
    /// </summary>
    void MoveOption(TcMenuView menu, TcOptionView option, int direction)
    {
        int index = Array.IndexOf(menu.Options, option) + direction;
        if (index < 0 || index >= menu.Options.Length)
        {
            return;
        }

        var other = menu.Options[index];
        var rows = Ctx.Schema.GossipPage.Options.Select(Ctx.Effective, menu.MenuId).ToList();
        var first = rows.FirstOrDefault(r => r.U("OptionID") == option.OptionId);
        var second = rows.FirstOrDefault(r => r.U("OptionID") == other.OptionId);
        if (first != null && second != null)
        {
            Ctx.Commit("Reorder options", Ctx.Schema.GossipPage.SwapOptions(first, second));
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
            var id = new LineEdit { Name = $"GreetingBroadcast{slot}", Text = row.T($"BroadcastTextID{slot}"), CustomMinimumSize = new(110, 0), TooltipText = "BroadcastText ID of this greeting variant. 0 clears the slot." };
            var weight = new LineEdit { Name = $"GreetingWeight{slot}", Text = row.T($"Probability{slot}"), CustomMinimumSize = new(85, 0), TooltipText = "Relative weight when the server picks a greeting, usually 0 to 1. 0 clears the slot." };
            var save = new Button { Text = "Save", Name = $"SaveGreeting{slot}", TooltipText = "Save this greeting slot's broadcast text and weight together." };
            void Save() => Ctx.Try(() =>
            {
                uint broadcast = uint.Parse(id.Text, CultureInfo.InvariantCulture);
                var edits = TcGossipRules.SetVariant(row, slot, broadcast, float.Parse(weight.Text, CultureInfo.InvariantCulture));
                _ = SaveReference("Change greeting variant", edits, broadcast, Reference.Broadcast);
            });
            save.Pressed += Save;
            id.TextSubmitted += _ => Save();
            weight.TextSubmitted += _ => Save();
            var find = new Button { Text = "…", TooltipText = "Search broadcast texts by ID or text." };
            find.Pressed += () => ChooseBroadcast(picked => id.Text = picked.ToString(CultureInfo.InvariantCulture), row.U($"BroadcastTextID{slot}") is var now and not 0 ? now : null);
            var write = new Button { Text = "New…", Name = $"NewGreetingText{slot}", TooltipText = "Write a new broadcast text for this slot." };
            write.Pressed += () => PromptText($"New greeting for slot {slot}", text => Ctx.Try(() =>
            {
                if (NewText(text, out uint created, out var rows))
                    Ctx.Commit("Write greeting text", [.. rows, .. TcGossipRules.SetVariant(row, slot, created, float.TryParse(weight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float w) && w > 0 ? w : 1f)]);
            }));
            var pick = new HBoxContainer();
            pick.AddChild(id);
            pick.AddChild(find);
            pick.AddChild(write);
            grid.AddChild(pick);
            grid.AddChild(weight);
            grid.AddChild(save);
        }
        foreach (int slot in Enumerable.Range(0, 8).Where(i => row.U($"BroadcastTextID{i}") != 0))
            BroadcastPreview(this, row.U($"BroadcastTextID{slot}"), $"Slot {slot}");
    }
}
