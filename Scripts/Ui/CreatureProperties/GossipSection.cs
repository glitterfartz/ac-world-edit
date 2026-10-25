using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

public sealed partial class GossipSection(CreatureContext ctx) : InteractionSection(ctx)
{
    public override string Id => "gossip";
    public override string Title => "Gossip";
    public override string IconPath => ClientStyle.GossipIcon(0);
    public override bool HasContent => Ctx.View is { } v && (Ctx.Editable || v.Menus.Count > 0 || (v.NpcFlag & NpcFlags.Gossip) != 0);
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
                RenderOptionEditor(v, menu, option);
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
            uint menu = NextId(InteractionTables.GossipMenu, "MenuID", snapshot!.Lookup.MaxMenuId);
            uint text = NextId(InteractionTables.NpcText, "ID", snapshot.Lookup.MaxTextId);
            var template = TemplateRow;
            Commit("Create gossip menu",
            [
                Insert(InteractionTables.NpcText, NewText(text, "Greetings, $N.")),
                Insert(InteractionTables.GossipMenu, Row.Of(("MenuID", menu), ("TextID", text))),
                Update(InteractionTables.CreatureTemplate, template, template.With("gossip_menu_id", menu).With("npcflag", template.U("npcflag") | NpcFlags.Gossip))
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
            uint copy = NextId(InteractionTables.GossipMenu, "MenuID", snapshot!.Lookup.MaxMenuId);
            uint nextText = NextId(InteractionTables.NpcText, "ID", snapshot.Lookup.MaxTextId);
            List<RowEdit> edits = [];
            foreach (var link in effective.All(InteractionTables.GossipMenu).Where(r => r.U("MenuID") == menu.MenuId).ToArray())
            {
                var text = effective.Get(InteractionTables.NpcText, link["TextID"]);
                uint textId = link.U("TextID");
                if (text != null)
                {
                    textId = nextText++;
                    edits.Add(Insert(InteractionTables.NpcText, text.With("ID", textId)));
                }

                edits.Add(Insert(InteractionTables.GossipMenu, Row.Of(("MenuID", copy), ("TextID", textId))));
            }

            edits.AddRange(effective.All(InteractionTables.GossipOption).Where(r => r.U("MenuID") == menu.MenuId).ToArray().Select(r => Insert(InteractionTables.GossipOption, r.With("MenuID", copy))));
            if (menus.Count == 0)
            {
                var template = TemplateRow;
                edits.Add(Update(InteractionTables.CreatureTemplate, template, template.With("gossip_menu_id", copy)));
            }
            else
            {
                uint current = menus.Pop();
                uint parent = menus.Count > 0 ? menus.Peek() : v.GossipMenuId;
                menus.Push(current);
                foreach (var option in effective.All(InteractionTables.GossipOption).Where(r => r.U("MenuID") == parent && r.U("ActionMenuID") == menu.MenuId).ToArray())
                {
                    edits.Add(Update(InteractionTables.GossipOption, option, option.With("ActionMenuID", copy)));
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
        var row = effective.Get(InteractionTables.NpcText, text.TextId.ToString(CultureInfo.InvariantCulture));
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
        save.Pressed += () => Commit($"Edit text {text.TextId}", [Update(InteractionTables.NpcText, row!, row!.With("text0_0", editor.Text).With("BroadcastTextID0", 0))]);
        actions.AddChild(save);
        actions.AddChild(ClientStyle.Text(text.FromBroadcast ? $"Currently shows broadcast_text {text.BroadcastId}; saving switches to this npc_text." : $"npc_text {text.TextId}{(snapshot!.Lookup.TextUsers(text.TextId) > 1 ? $" · used by {snapshot.Lookup.TextUsers(text.TextId)} menus" : "")} · $N name, $B newline", 11, ClientStyle.Faded));
    }

    void RenderOptionEditor(NpcInteraction v, MenuView menu, OptionView option)
    {
        var row = option.Row;
        var box = new VBoxContainer();
        body.AddChild(box);
        var line = new HBoxContainer();
        box.AddChild(line);
        var icon = new OptionButton
        {
            TooltipText = "Icon"
        };
        for (int i = 0; i < NpcFlags.OptionIcons.Length; i++)
        {
            icon.AddIconItem(icons.Get(ClientStyle.GossipIcon((uint)i)) ?? new PlaceholderTexture2D(), NpcFlags.OptionIcons[i], i);
        }

        icon.Select((int)Math.Min(option.Icon, (uint)NpcFlags.OptionIcons.Length - 1));
        icon.ItemSelected += index => Commit("Change option icon", [Update(InteractionTables.GossipOption, row, row.With("OptionIcon", (uint)index))]);
        line.AddChild(icon);
        var text = new LineEdit
        {
            Text = row.T("OptionText"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            PlaceholderText = option.FromBroadcast ? option.Text : "Option text"
        };
        text.TextSubmitted += value => Commit("Edit option text", [Update(InteractionTables.GossipOption, row, row.With("OptionText", value).With("OptionBroadcastTextID", 0))]);
        line.AddChild(text);
        var remove = new Button
        {
            Text = "✕",
            TooltipText = "Delete option"
        };
        remove.Pressed += () => Commit("Delete option", [Delete(InteractionTables.GossipOption, row)]);
        line.AddChild(remove);
        var details = new HBoxContainer();
        box.AddChild(details);
        var type = new OptionButton
        {
            TooltipText = "OptionType (what the client does)"
        };
        for (int i = 0; i < NpcFlags.OptionTypes.Length; i++)
        {
            type.AddItem(NpcFlags.OptionTypes[i], i);
        }

        type.Select((int)Math.Min(option.Type, (uint)NpcFlags.OptionTypes.Length - 1));
        type.ItemSelected += index => Commit("Change option type", [Update(InteractionTables.GossipOption, row, row.With("OptionType", (uint)index))]);
        details.AddChild(type);
        details.AddChild(ClientStyle.Tag("NpcFlag"));
        var flag = new LineEdit
        {
            Text = $"0x{option.NpcFlag:X}",
            CustomMinimumSize = new(80, 0),
            TooltipText = "Shown only when this & npcflag is non-zero"
        };
        flag.TextSubmitted += value => Try(() => Commit("Change option NpcFlag", [Update(InteractionTables.GossipOption, row, row.With("OptionNpcFlag", ParseUInt(value)))]));
        details.AddChild(flag);
        details.AddChild(ClientStyle.Tag("Submenu"));
        var action = new LineEdit
        {
            Text = option.ActionMenu.ToString(CultureInfo.InvariantCulture),
            CustomMinimumSize = new(70, 0),
            TooltipText = "ActionMenuID (0 = none); must exist"
        };
        action.TextSubmitted += value => Try(() => _ = SetSubmenu(row, ParseUInt(value)));
        details.AddChild(action);
        var up = new Button
        {
            Text = "▲",
            TooltipText = "Move up"
        };
        up.Pressed += () => MoveOption(menu, option, -1);
        details.AddChild(up);
        var down = new Button
        {
            Text = "▼",
            TooltipText = "Move down"
        };
        down.Pressed += () => MoveOption(menu, option, 1);
        details.AddChild(down);
        if (option.ActionMenu != 0)
        {
            var open = new Button
            {
                Text = "Open submenu ▸"
            };
            open.Pressed += () =>
            {
                menus.Push(option.ActionMenu);
                Render();
            };
            box.AddChild(open);
        }

        box.AddChild(new HSeparator());
    }

    async Task SetSubmenu(Row option, uint menu)
    {
        if (menu != 0 && !effective.All(InteractionTables.GossipMenu).Any(r => r.U("MenuID") == menu) && !snapshot!.Lookup.MenuExists(menu))
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

        Commit("Change option submenu", [Update(InteractionTables.GossipOption, option, option.With("ActionMenuID", menu))]);
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
        Commit("Reorder options",
        [
            Delete(InteractionTables.GossipOption, option.Row),
            Delete(InteractionTables.GossipOption, other.Row),
            Insert(InteractionTables.GossipOption, option.Row.With("OptionID", other.OptionId)),
            Insert(InteractionTables.GossipOption, other.Row.With("OptionID", option.OptionId))
        ]);
    }

    void RenderAddOption(NpcInteraction v, MenuView menu)
    {
        uint nextOption = menu.Options.Length == 0 ? 0 : menu.Options.Max(o => o.OptionId) + 1;
        var row = new HBoxContainer();
        body.AddChild(row);
        var add = new Button
        {
            Text = "+ Add option"
        };
        add.Pressed += () => Commit("Add option", [Insert(InteractionTables.GossipOption, NewOption(menu.MenuId, nextOption, "New option", 1, NpcFlags.Gossip, 0, 0))]);
        row.AddChild(add);
        var withMenu = new Button
        {
            Text = "+ Add option → new submenu"
        };
        withMenu.Pressed += () =>
        {
            uint newMenu = NextId(InteractionTables.GossipMenu, "MenuID", snapshot!.Lookup.MaxMenuId);
            uint text = NextId(InteractionTables.NpcText, "ID", snapshot.Lookup.MaxTextId);
            Commit("Add option with new submenu",
            [
                Insert(InteractionTables.NpcText, NewText(text, "New submenu text.")),
                Insert(InteractionTables.GossipMenu, Row.Of(("MenuID", newMenu), ("TextID", text))),
                Insert(InteractionTables.GossipOption, NewOption(menu.MenuId, nextOption, "Tell me more.", 1, NpcFlags.Gossip, 0, newMenu))
            ]);
        };
        row.AddChild(withMenu);
    }
}
