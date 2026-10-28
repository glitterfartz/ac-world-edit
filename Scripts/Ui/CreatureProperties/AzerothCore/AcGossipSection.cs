using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

public sealed partial class AcGossipSection(CreatureContext ctx) : AcInteractionSection(ctx)
{
    public override string Id => "gossip";
    public override string Title => "Gossip";
    public override string IconPath => ClientStyle.GossipIcon(0);
    public override bool HasContent => Ctx.View is { } v && (Ctx.Editable || v.Menus.Count > 0 || (v.NpcFlag & AcNpcFlags.Gossip) != 0);
    readonly Stack<uint> menus = new();

    protected override void Populate(NpcInteraction v)
    {
        RenderGossip(v);
    }

    public override void OnShown(object? argument) => menus.Clear();

    public override void OnCreatureChanged(CreatureSpawn? creature) => menus.Clear();

    /// <summary>The menu shown now (smoke tests).</summary>
    public uint SmokeCurrentMenu => Ctx.View is { } v ? CurrentMenu(v) : 0;

    uint CurrentMenu(NpcInteraction v) => menus.Count > 0 ? menus.Peek() : v.GossipMenuId;

    void RenderGossip(NpcInteraction v)
    {
        uint menuId = CurrentMenu(v);
        if (!v.Menus.TryGetValue(menuId, out var menu))
        {
            body.AddChild(ClientStyle.Text(menuId == 0 ? "This creature has no gossip menu; the client opens its default greeting or the first service directly." : $"Gossip menu {menuId} could not be loaded.", 15, ClientStyle.Faded));
            if (editable)
            {
                RenderCreateMenu(v);
            }

            return;
        }

        if (menu.SharedBy > 1)
        {
            body.AddChild(ClientStyle.Text($"Menu {menu.MenuId} is shared by {menu.SharedBy} creatures: edits apply to all of them.", 12, ClientStyle.Warning));
            if (editable)
            {
                RenderForkMenu(v, menu);
            }
        }

        foreach (var text in menu.Texts)
        {
            if (menu.Texts.Length > 1)
            {
                body.AddChild(ClientStyle.Text($"Text {text.TextId} · chance {text.Probability:0.##} · chosen by conditions or chance", 11, ClientStyle.Faded));
            }

            if (editable)
            {
                RenderTextEditor(text);
            }
            else
            {
                body.AddChild(ClientStyle.Text(NpcInteraction.Render(text.Male.Length > 0 ? text.Male : text.Female), 16, text.Edited ? ClientStyle.Edited : null));
            }
        }

        body.AddChild(new HSeparator());
        foreach (var option in menu.Options)
        {
            if (editable)
            {
                continue;
            }

            var button = new Button
            {
                Text = NpcInteraction.Render(option.Text),
                Alignment = HorizontalAlignment.Left,
                Disabled = option.HiddenByFlags,
                TooltipText = $"Option {option.OptionId} · {OptionTypeName(option.Type)}{(option.ActionMenu != 0 ? $" → menu {option.ActionMenu}" : "")}{(option.HiddenByFlags ? $"\nHidden in game: OptionNpcFlag {option.NpcFlag:X} not in npcflag" : "")}"
            };
            if (option.Edited)
            {
                button.AddThemeColorOverride("font_color", ClientStyle.Edited);
            }

            var chosen = option;
            button.Pressed += () => Follow(v, chosen);
            body.AddChild(IconRow(ClientStyle.GossipIcon(option.Icon), button));
        }

        if (editable)
        {
            RenderOptions(menu);
            RenderAddOption(v, menu);
        }

        if (menus.Count > 0)
        {
            var back = new Button
            {
                Text = "◂ Back",
                Alignment = HorizontalAlignment.Left
            };
            back.Pressed += () =>
            {
                menus.Pop();
                Render();
            };
            body.AddChild(back);
        }
    }

    /// <summary>
    /// Do what the client would for an option: open a submenu or the matching service page, or say
    /// which client window it opens (bind, bank, auction…). Confirmation boxes are shown first.
    /// </summary>
    void Follow(NpcInteraction v, OptionView option)
    {
        string box = option.BoxText.Length > 0 || option.BoxMoney > 0 ? $"Confirmation: \"{NpcInteraction.Render(option.BoxText)}\"{(option.BoxMoney > 0 ? $" · costs {ClientStyle.Money(option.BoxMoney)}" : "")}{(option.BoxCoded ? " · asks for a code" : "")}. " : "";
        status = box;
        if (option.ActionMenu != 0)
        {
            menus.Push(option.ActionMenu);
        }
        else
        {
            switch (option.Type)
            {
                case 2:
                    Ctx.ShowSection("quests");
                    break;
                case 3:
                case 15:
                    Ctx.ShowSection("vendor");
                    break;
                case 5:
                    Ctx.ShowSection("trainer");
                    break;
                case 4 when catalog != null && target != null:
                    var node = catalog.NearestTaxiNode(target.Pose.Map, target.Pose.X, target.Pose.Y, target.Pose.Z);
                    status += node is { } n ? $"Opens the flight map at {n.Name} (node {n.Id}, {n.Distance:0} yd away)." : "Opens the flight map.";
                    break;
                case 1:
                    status += "Closes the gossip window (no submenu).";
                    break;
                default:
                    status += $"Opens the client's {OptionTypeName(option.Type).ToLowerInvariant()} window.";
                    break;
            }
        }

        Render();
    }

    void RenderCreateMenu(NpcInteraction v)
    {
        var create = new Button
        {
            Text = "+ Create gossip menu"
        };
        create.Pressed += () =>
        {
            uint menu = NextId(Ctx.Schema.GossipPage.Menus.Table, "MenuID", snapshot!.Lookup.MaxMenuId);
            uint text = NextId(Ctx.Schema.GossipPage.TextTable, "ID", snapshot.Lookup.MaxTextId);
            var template = TemplateRow;
            Commit("Create gossip menu",
            [
                Insert(Ctx.Schema.GossipPage.TextTable, NewText(text, "Greetings, $N.")),
                Insert(Ctx.Schema.GossipPage.Menus.Table, Row.Of(("MenuID", menu), ("TextID", text))),
                Update(Ctx.Schema.Tables.Find("creature_template"), template, template.With("gossip_menu_id", menu).With("npcflag", template.U("npcflag") | AcNpcFlags.Gossip))
            ]);
        };
        body.AddChild(create);
    }

    /// <summary>
    /// Copy a shared menu (its texts and options) under new IDs and point this NPC (or the parent
    /// option) at the copy. Submenus the options lead to stay shared.
    /// </summary>
    void RenderForkMenu(NpcInteraction v, MenuView menu)
    {
        // Gossip conditions are keyed by MenuID and are not editable here, so a copy loses them.
        int conditions = v.Conditions.GetValueOrDefault(14) + v.Conditions.GetValueOrDefault(15);
        var fork = new Button
        {
            Text = "Make a copy for this NPC",
            TooltipText = conditions > 0 ? $"Warning: the {conditions} SQL condition(s) on this NPC's menus are not copied; copied texts and options become unconditional." : "Copy texts and options under new IDs."
        };
        if (conditions > 0)
        {
            body.AddChild(ClientStyle.Text($"Copying drops {conditions} condition(s) (e.g. holiday-only options would always show).", 11, ClientStyle.Warning));
        }

        fork.Pressed += () =>
        {
            uint copy = NextId(Ctx.Schema.GossipPage.Menus.Table, "MenuID", snapshot!.Lookup.MaxMenuId);
            uint nextText = NextId(Ctx.Schema.GossipPage.TextTable, "ID", snapshot.Lookup.MaxTextId);
            List<RowEdit> edits = [];
            foreach (var link in effective.All(Ctx.Schema.GossipPage.Menus.Table).Where(r => r.U("MenuID") == menu.MenuId).ToArray())
            {
                var text = effective.Get(Ctx.Schema.GossipPage.TextTable, link["TextID"]);
                uint textId = link.U("TextID");
                if (text != null)
                {
                    textId = nextText++;
                    edits.Add(Insert(Ctx.Schema.GossipPage.TextTable, text.With("ID", textId)));
                }

                edits.Add(Insert(Ctx.Schema.GossipPage.Menus.Table, Row.Of(("MenuID", copy), ("TextID", textId))));
            }

            edits.AddRange(effective.All(Ctx.Schema.GossipPage.Options.Table).Where(r => r.U("MenuID") == menu.MenuId).ToArray().Select(r => Insert(Ctx.Schema.GossipPage.Options.Table, r.With("MenuID", copy))));
            if (menus.Count == 0)
            {
                var template = TemplateRow;
                edits.Add(Update(Ctx.Schema.Tables.Find("creature_template"), template, template.With("gossip_menu_id", copy)));
            }
            else
            {
                uint current = menus.Pop();
                uint parent = menus.Count > 0 ? menus.Peek() : v.GossipMenuId;
                menus.Push(current);
                foreach (var option in effective.All(Ctx.Schema.GossipPage.Options.Table).Where(r => r.U("MenuID") == parent && r.U("ActionMenuID") == menu.MenuId).ToArray())
                {
                    edits.Add(Update(Ctx.Schema.GossipPage.Options.Table, option, option.With("ActionMenuID", copy)));
                }
            }

            if (Commit($"Copy menu {menu.MenuId} for this NPC", edits) && menus.Count > 0)
            {
                menus.Pop();
                menus.Push(copy);
                Render();
            }
        };
        body.AddChild(fork);
    }

    /// <summary>
    /// Greeting text editor. Saving clears BroadcastTextID0: the server sends broadcast_text instead
    /// of npc_text whenever that ID resolves, which would hide the edit in game.
    /// </summary>
    void RenderTextEditor(TextView text)
    {
        var row = effective.Get(Ctx.Schema.GossipPage.TextTable, text.TextId.ToString(CultureInfo.InvariantCulture));
        var editor = new TextEdit
        {
            Text = row?.T("text0_0") ?? text.Male,
            CustomMinimumSize = new(0, 90),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            Editable = row != null
        };
        body.AddChild(editor);
        var actions = new HBoxContainer();
        body.AddChild(actions);
        var save = new Button
        {
            Text = "Save text",
            Disabled = row == null
        };
        save.Pressed += () => Commit($"Edit text {text.TextId}", [Update(Ctx.Schema.GossipPage.TextTable, row!, row!.With("text0_0", editor.Text).With("BroadcastTextID0", 0))]);
        actions.AddChild(save);
        actions.AddChild(ClientStyle.Text(text.FromBroadcast ? $"Currently shows broadcast_text {text.BroadcastId}; saving switches to this npc_text." : $"npc_text {text.TextId}{(snapshot!.Lookup.TextUsers(text.TextId) > 1 ? $" · used by {snapshot.Lookup.TextUsers(text.TextId)} menus" : "")} · $N name, $B newline", 11, ClientStyle.Faded));
    }

    void RenderOptions(MenuView menu)
    {
        new PageRenderer(Ctx).RenderItems(body, Ctx.Schema.GossipPage.Options, effective, menu.MenuId, true,
            row => ClientStyle.Text($"Option {row.U("OptionID")}", 14, ClientStyle.Header),
            (field, row) =>
            {
                var option = menu.Options.First(o => o.OptionId == row.U("OptionID"));
                switch (field.Column)
                {
                    case "OptionIcon":
                        var icon = new OptionButton();
                        foreach (var (value, name) in field.Options!)
                        {
                            icon.AddIconItem(icons.Get(ClientStyle.GossipIcon((uint)value)) ?? new PlaceholderTexture2D(), name, value);
                        }

                        int selected = icon.GetItemIndex((int)option.Icon);
                        if (selected < 0)
                        {
                            icon.AddItem($"Unknown ({option.Icon})", (int)option.Icon);
                            selected = icon.ItemCount - 1;
                        }

                        icon.Select(selected);
                        icon.ItemSelected += index => Commit("Change option icon", [Update(field.Table, row, row.With(field.Column, icon.GetItemId((int)index)))]);
                        return icon;
                    case "OptionText":
                        var text = new LineEdit
                        {
                            Text = row.T(field.Column),
                            SizeFlagsHorizontal = SizeFlags.ExpandFill,
                            PlaceholderText = option.FromBroadcast ? option.Text : "Option text",
                            CustomMinimumSize = new(220, 0)
                        };
                        text.TextSubmitted += value => Commit("Edit option text", Ctx.Schema.GossipPage.ChangeText(row, value));
                        return text;
                    case "ActionMenuID":
                        var action = new LineEdit { Text = row.T(field.Column), CustomMinimumSize = new(70, 0) };
                        action.TextSubmitted += value => Try(() => _ = SetSubmenu(row, ParseUInt(value)));
                        return action;
                    default:
                        return null;
                }
            }, (card, row) =>
            {
                var option = menu.Options.First(o => o.OptionId == row.U("OptionID"));
                var actions = new HBoxContainer();
                card.AddChild(actions);
                var up = new Button { Text = "▲", TooltipText = "Move up", Disabled = option == menu.Options[0] };
                up.Pressed += () => MoveOption(menu, option, -1);
                actions.AddChild(up);
                var down = new Button { Text = "▼", TooltipText = "Move down", Disabled = option == menu.Options[^1] };
                down.Pressed += () => MoveOption(menu, option, 1);
                actions.AddChild(down);
                if (option.ActionMenu != 0)
                {
                    var open = new Button { Text = "Open submenu ▸" };
                    open.Pressed += () =>
                    {
                        menus.Push(option.ActionMenu);
                        Render();
                    };
                    actions.AddChild(open);
                }

                card.AddChild(new HSeparator());
            }, columns: 2);
    }

    async Task SetSubmenu(Row option, uint menu)
    {
        if (menu != 0 && !effective.All(Ctx.Schema.GossipPage.Menus.Table).Any(r => r.U("MenuID") == menu) && !snapshot!.Lookup.MenuExists(menu))
        {
            status = $"Checking menu {menu}…";
            Render();
            if (!await DBSource.Open(Connection()).MenuExistsAsync(menu, CancellationToken.None))
            {
                status = $"Not saved: gossip menu {menu} does not exist.";
                Render();
                return;
            }

            snapshot!.Lookup.Menus.Add(menu);
        }

        Commit("Change option submenu", [Update(Ctx.Schema.GossipPage.Options.Table, option, option.With("ActionMenuID", menu))]);
    }

    /// <summary>
    /// Swap two options' OptionID (the display order). OptionID is part of the key, so this is a
    /// delete and re-insert of both rows in one command.
    /// </summary>
    void MoveOption(MenuView menu, OptionView option, int direction)
    {
        int index = Array.IndexOf(menu.Options, option) + direction;
        if (index < 0 || index >= menu.Options.Length)
        {
            return;
        }

        var other = menu.Options[index];
        Commit("Reorder options", Ctx.Schema.GossipPage.SwapOptions(option.Row, other.Row));
    }

    void RenderAddOption(NpcInteraction v, MenuView menu)
    {
        var row = new HBoxContainer();
        body.AddChild(row);
        var add = new Button
        {
            Text = "+ Add option"
        };
        add.Pressed += () => Commit("Add option", [Ctx.Schema.GossipPage.Options.Add(effective, menu.MenuId)]);
        row.AddChild(add);
        var withMenu = new Button
        {
            Text = "+ Add option → new submenu"
        };
        withMenu.Pressed += () =>
        {
            uint newMenu = NextId(Ctx.Schema.GossipPage.Menus.Table, "MenuID", snapshot!.Lookup.MaxMenuId);
            uint text = NextId(Ctx.Schema.GossipPage.TextTable, "ID", snapshot.Lookup.MaxTextId);
            Commit("Add option with new submenu",
            [
                Insert(Ctx.Schema.GossipPage.TextTable, NewText(text, "New submenu text.")),
                Insert(Ctx.Schema.GossipPage.Menus.Table, Row.Of(("MenuID", newMenu), ("TextID", text))),
                Ctx.Schema.GossipPage.Options.Add(effective, menu.MenuId, ("OptionText", "Tell me more."), ("ActionMenuID", newMenu))
            ]);
        };
        row.AddChild(withMenu);
    }
}
