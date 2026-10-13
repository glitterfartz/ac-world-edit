using System.Globalization;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Edit mode: every control builds row edits from the effective view, checks that they introduce no
/// server-rule error, and records one InteractionDraft command. New IDs are reserved past the
/// database maximum and any earlier reservation. Nothing is written to the database.
/// </summary>
public partial class InteractionWindow
{
    /// <summary>
    /// Shared content draft (one file and one undo history for all editors).
    /// </summary>
    public ContentDrafts Drafts { get; set; } = null!;
    InteractionDraft? draft => Drafts?.Draft;
    bool editing;
    Button editToggle = null!, undo = null!, redo = null!;
    Label historyLabel = null!;
    readonly Dictionary<string, (string Term, List<(string Label, Action Pick)> Results)> searches = [];

    public bool Editing => editing;

    /// <summary>
    /// Reload when another editor changes the shared draft.
    /// </summary>
    void WatchDrafts()
    {
        status = Drafts.Error ?? "";
        Drafts.Changed += sender =>
        {
            if (sender != this && target != null)
            {
                _ = Reload();
            }
        };
    }

    void BuildEditToggle(HBoxContainer header)
    {
        editToggle = new Button
        {
            Text = "Edit",
            ToggleMode = true,
            TooltipText = "Edit these records in your local content draft (nothing is written to the database)."
        };
        editToggle.Toggled += value =>
        {
            editing = value && draft != null;
            // Editor rows need more width than the client-sized preview (Render applies it).
            Render();
        };
        header.AddChild(editToggle);
    }

    void BuildHistoryRow(VBoxContainer root)
    {
        var row = new HBoxContainer();
        root.AddChild(row);
        undo = new Button
        {
            Text = "Undo"
        };
        undo.Pressed += () => History(false);
        redo = new Button
        {
            Text = "Redo"
        };
        redo.Pressed += () => History(true);
        historyLabel = ClientStyle.Text("", 11, ClientStyle.Faded);
        row.AddChild(undo);
        row.AddChild(redo);
        row.AddChild(historyLabel);
    }

    void UpdateHistoryRow()
    {
        editToggle.Disabled = draft == null;
        editToggle.SetPressedNoSignal(editing);
        undo.Disabled = draft?.CanUndo != true;
        redo.Disabled = draft?.CanRedo != true;
        undo.TooltipText = draft?.LastLabel is { } last ? $"Undo \"{last}\"" : "";
        redo.TooltipText = draft?.NextLabel is { } next ? $"Redo \"{next}\"" : "";
        historyLabel.Text = draft == null ? "" : "Local content draft · database unchanged";
    }

    void History(bool forward)
    {
        status = Drafts.History(this, forward);
        Rebuild();
        _ = Reload();
    }

    /// <summary>
    /// Validate and record one command. Rejected when the row edits break the column rules or
    /// introduce a server-rule error that was not already present.
    /// </summary>
    public bool Commit(string label, IEnumerable<RowEdit> edits)
    {
        if (draft == null || snapshot == null || view == null)
        {
            return false;
        }

        var (ok, message) = Drafts.Commit(this, sourceKey, snapshot.Rows, effective, label, edits.ToArray(), rows => BuildView(rows).Issues);
        status = message;
        if (!ok)
        {
            Render();
            return false;
        }

        Rebuild();
        _ = Reload();
        return true;
    }

    static RowEdit Update(TableSchema table, Row before, Row after) => new(table.Name, table.KeyOf(before), before, after);
    static RowEdit Insert(TableSchema table, Row after) => new(table.Name, table.KeyOf(after), null, after);
    static RowEdit Delete(TableSchema table, Row before) => new(table.Name, table.KeyOf(before), before, null);

    Row TemplateRow => effective.Get(InteractionTables.CreatureTemplate, snapshot!.Entry.ToString(CultureInfo.InvariantCulture))!;

    uint NextId(TableSchema table, string column, uint databaseMax) =>
        Math.Max(Math.Max(databaseMax, draft?.MaxReserved(sourceKey, table, column) ?? 0), effective.All(table).Select(r => r.U(column)).DefaultIfEmpty(0u).Max()) + 1;

    static Row NewText(uint id, string text) => Row.Of(("ID", id), ("text0_0", text), ("text0_1", ""), ("BroadcastTextID0", 0), ("Probability0", 1f));

    static Row NewOption(uint menu, uint id, string text, uint type, uint npcFlag, uint icon, uint action) =>
        Row.Of(("MenuID", menu), ("OptionID", id), ("OptionIcon", icon), ("OptionText", text), ("OptionBroadcastTextID", 0), ("OptionType", type), ("OptionNpcFlag", npcFlag), ("ActionMenuID", action), ("ActionPoiID", 0u), ("BoxCoded", 0u), ("BoxMoney", 0u), ("BoxText", ""));

    static uint ParseUInt(string text) => text.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? uint.Parse(text.Trim()[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : uint.Parse(text.Trim(), CultureInfo.InvariantCulture);

    /// <summary>
    /// Run a parse/build step from a control; bad numbers show a message instead of throwing.
    /// </summary>
    void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            status = "Not saved: enter a whole number (decimal or 0x hex).";
            Render();
        }
    }

    // ---- Gossip -------------------------------------------------------------------------------

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
            if (!await new InteractionSource(ConnectionPath()).MenuExistsAsync(menu, CancellationToken.None))
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

    // ---- Quests -------------------------------------------------------------------------------

    void RenderQuestLinkEditor(NpcInteraction v)
    {
        body.AddChild(new HSeparator());
        body.AddChild(ClientStyle.Text("Edit quest links", 16, ClientStyle.Header));
        foreach (var (table, links, kind) in new[] { (InteractionTables.QuestStarter, v.Starts, "starts"), (InteractionTables.QuestEnder, v.Ends, "ends") })
        {
            foreach (var link in links)
            {
                var row = new HBoxContainer();
                row.AddChild(ClientStyle.Text($"{kind}: {link.Info?.Title ?? "missing"} ({link.Quest})", 13, link.Edited ? ClientStyle.Edited : null));
                var remove = new Button
                {
                    Text = "Remove"
                };
                var existing = effective.Get(table, $"{v.Entry}|{link.Quest}");
                remove.Disabled = existing == null;
                remove.Pressed += () => Commit($"Remove quest {link.Quest} ({kind})", [Delete(table, existing!)]);
                row.AddChild(remove);
                var open = new Button
                {
                    Text = "Quest Editor"
                };
                open.Pressed += () => QuestEditorRequested?.Invoke(link.Quest);
                row.AddChild(open);
                body.AddChild(row);
            }
        }

        SearchPicker("quest", "Find quest by title or ID…", async term =>
        {
            var found = await new InteractionSource(ConnectionPath()).SearchQuestsAsync(term, CancellationToken.None);
            return found.SelectMany(q => new (string, Action)[]
            {
                ($"Starts: {q.Title} ({q.Id}, level {q.Level})", () => LinkQuest(InteractionTables.QuestStarter, q)),
                ($"Ends: {q.Title} ({q.Id})", () => LinkQuest(InteractionTables.QuestEnder, q))
            }).ToList();
        });
    }

    void LinkQuest(TableSchema table, (uint Id, string Title, int Level) quest)
    {
        // Seed the lookup so validation knows the picked quest before the next reload.
        snapshot!.Lookup.Quests.TryAdd(quest.Id, new QuestInfo(quest.Id, quest.Title, quest.Level, 0, "", "", 0, []));
        Commit($"Link quest {quest.Id}", [Insert(table, Row.Of(("id", snapshot.Entry), ("quest", quest.Id)))]);
    }

    // ---- Vendor -------------------------------------------------------------------------------

    void RenderVendorEditor(NpcInteraction v)
    {
        foreach (var item in v.Vendor)
        {
            if (item.Row is not { } row)
            {
                body.AddChild(ClientStyle.Text($"{item.Info?.Name ?? $"Item {item.Item}"} · from vendor list {item.FromReference} (edit that vendor)", 13, ClientStyle.Faded));
                continue;
            }

            var line = new HBoxContainer();
            body.AddChild(line);
            line.AddChild(ClientStyle.Text($"{item.Info?.Name ?? "missing"} ({item.Item})", 13, item.Edited ? ClientStyle.Edited : item.Info != null ? ClientStyle.Quality(item.Info.Quality) : ClientStyle.Error));
            LineEdit Field(string tooltip, uint value, Func<uint, IEnumerable<RowEdit>> apply)
            {
                var field = new LineEdit
                {
                    Text = value.ToString(CultureInfo.InvariantCulture),
                    TooltipText = tooltip,
                    CustomMinimumSize = new(58, 0)
                };
                field.TextSubmitted += text => Try(() => Commit($"Edit vendor item {item.Item}", apply(ParseUInt(text))));
                line.AddChild(field);
                return field;
            }

            Field("Max count (0 = unlimited)", item.MaxCount, n => [Update(InteractionTables.Vendor, row, row.With("maxcount", n))]);
            Field("Restock seconds (incrtime)", item.IncrTime, n => [Update(InteractionTables.Vendor, row, row.With("incrtime", n))]);
            // ExtendedCost is part of the key: changing it re-inserts the row.
            Field("Extended cost ID (0 = gold price)", item.ExtendedCost, n => [Delete(InteractionTables.Vendor, row), Insert(InteractionTables.Vendor, row.With("ExtendedCost", n))]);
            var remove = new Button
            {
                Text = "✕",
                TooltipText = "Remove item"
            };
            remove.Pressed += () => Commit($"Remove vendor item {item.Item}", [Delete(InteractionTables.Vendor, row)]);
            line.AddChild(remove);
        }

        SearchPicker("item", "Find item by name or ID…", async term =>
        {
            var found = await new InteractionSource(ConnectionPath()).SearchItemsAsync(term, CancellationToken.None);
            return found.Select(item => ($"{item.Name} ({item.Id}) · {ClientStyle.Money(item.BuyPrice)}", (Action)(() => AddVendorItem(item)))).ToList();
        });
    }

    void AddVendorItem(ItemInfo item)
    {
        snapshot!.Lookup.Items.TryAdd(item.Id, item);
        int slot = effective.All(InteractionTables.Vendor).Where(r => r.U("entry") == snapshot.Entry).Select(r => r.I("slot")).DefaultIfEmpty(-1).Max() + 1;
        Commit($"Add vendor item {item.Id}", [Insert(InteractionTables.Vendor, Row.Of(("entry", snapshot.Entry), ("item", (int)item.Id), ("ExtendedCost", 0u), ("slot", slot), ("maxcount", 0u), ("incrtime", 0u)))]);
    }

    // ---- Trainer ------------------------------------------------------------------------------

    void RenderTrainerEditor(NpcInteraction v)
    {
        body.AddChild(new HSeparator());
        var assign = new HBoxContainer();
        body.AddChild(assign);
        assign.AddChild(ClientStyle.Tag("Trainer ID"));
        var id = new LineEdit
        {
            Text = v.Trainer?.Id.ToString(CultureInfo.InvariantCulture) ?? "",
            CustomMinimumSize = new(80, 0),
            PlaceholderText = "existing ID"
        };
        id.TextSubmitted += text => Try(() => _ = AssignTrainer(ParseUInt(text)));
        assign.AddChild(id);
        var create = new Button
        {
            Text = "+ New trainer"
        };
        create.Pressed += () =>
        {
            uint trainer = NextId(InteractionTables.Trainer, "Id", snapshot!.Lookup.MaxTrainerId);
            Commit("Create trainer", [Insert(InteractionTables.Trainer, Row.Of(("Id", trainer), ("Type", 0u), ("Requirement", 0u), ("Greeting", ""))), .. AssignEdits(trainer, NpcFlags.Trainer)]);
        };
        assign.AddChild(create);
        if (v.Trainer is not { } current)
        {
            return;
        }

        if (current.SharedBy > 1)
        {
            var fork = new Button
            {
                Text = "Make a copy for this NPC"
            };
            fork.Pressed += () =>
            {
                uint copy = NextId(InteractionTables.Trainer, "Id", snapshot!.Lookup.MaxTrainerId);
                var trainerRow = effective.Get(InteractionTables.Trainer, current.Id.ToString(CultureInfo.InvariantCulture));
                List<RowEdit> edits = [Insert(InteractionTables.Trainer, trainerRow?.With("Id", copy) ?? Row.Of(("Id", copy), ("Type", current.Type), ("Requirement", current.Requirement), ("Greeting", current.Greeting)))];
                edits.AddRange(current.Spells.Select(s => Insert(InteractionTables.TrainerSpell, s.Row.With("TrainerId", copy))));
                edits.AddRange(AssignEdits(copy, 0));
                Commit($"Copy trainer {current.Id} for this NPC", edits);
            };
            body.AddChild(fork);
        }

        foreach (var spell in current.Spells)
        {
            var line = new HBoxContainer();
            body.AddChild(line);
            line.AddChild(ClientStyle.Text(spell.Name, 13, spell.Edited ? ClientStyle.Edited : null));
            foreach (var (column, tooltip, value) in new[] { ("MoneyCost", "Cost in copper", spell.Cost), ("ReqLevel", "Required level", spell.ReqLevel) })
            {
                var field = new LineEdit
                {
                    Text = value.ToString(CultureInfo.InvariantCulture),
                    TooltipText = tooltip,
                    CustomMinimumSize = new(64, 0)
                };
                var row = spell.Row;
                field.TextSubmitted += text => Try(() => Commit($"Edit trainer spell {spell.SpellId}", [Update(InteractionTables.TrainerSpell, row, row.With(column, ParseUInt(text)))]));
                line.AddChild(field);
            }

            var remove = new Button
            {
                Text = "✕"
            };
            remove.Pressed += () => Commit($"Remove trainer spell {spell.SpellId}", [Delete(InteractionTables.TrainerSpell, spell.Row)]);
            line.AddChild(remove);
        }

        SearchPicker("spell", "Find spell by name or ID…", term => Task.FromResult<IReadOnlyList<(string, Action)>>(
            (catalog?.SearchSpells(term) ?? []).Select(s => ($"{s.Name} ({s.Id})", (Action)(() => Commit($"Add trainer spell {s.Id}", [Insert(InteractionTables.TrainerSpell, Row.Of(("TrainerId", current.Id), ("SpellId", s.Id), ("MoneyCost", 0u), ("ReqSkillLine", 0u), ("ReqSkillRank", 0u), ("ReqLevel", 1u)))])))).ToList()));
    }

    /// <summary>
    /// Insert or update creature_default_trainer for this NPC, optionally adding npcflag bits.
    /// </summary>
    IEnumerable<RowEdit> AssignEdits(uint trainer, uint flag)
    {
        var link = effective.Get(InteractionTables.DefaultTrainer, snapshot!.Entry.ToString(CultureInfo.InvariantCulture));
        var next = Row.Of(("CreatureId", snapshot.Entry), ("TrainerId", trainer));
        yield return link == null ? Insert(InteractionTables.DefaultTrainer, next) : Update(InteractionTables.DefaultTrainer, link, next);
        if (flag != 0 && (TemplateRow.U("npcflag") & flag) == 0)
        {
            yield return Update(InteractionTables.CreatureTemplate, TemplateRow, TemplateRow.With("npcflag", TemplateRow.U("npcflag") | flag));
        }
    }

    async Task AssignTrainer(uint trainer)
    {
        if (!effective.All(InteractionTables.Trainer).Any(r => r.U("Id") == trainer) && !snapshot!.Lookup.TrainerExists(trainer))
        {
            if (!await new InteractionSource(ConnectionPath()).TrainerExistsAsync(trainer, CancellationToken.None))
            {
                status = $"Not saved: trainer {trainer} does not exist.";
                Render();
                return;
            }

            snapshot!.Lookup.Trainers.Add(trainer);
        }

        Commit($"Assign trainer {trainer}", AssignEdits(trainer, NpcFlags.Trainer));
    }

    // ---- Template and spell click ------------------------------------------------------------

    void RenderTemplateEditor(NpcInteraction v)
    {
        var template = TemplateRow;
        body.AddChild(ClientStyle.Text($"creature_template {v.Entry} applies to all {v.SpawnCount} spawn(s) of this entry.", 11, ClientStyle.Warning));
        var grid = new GridContainer
        {
            Columns = 3
        };
        body.AddChild(grid);
        foreach (var (bit, name) in NpcFlags.All)
        {
            var check = new CheckBox
            {
                Text = name,
                ButtonPressed = (v.NpcFlag & bit) != 0
            };
            check.Toggled += on => Commit($"{(on ? "Add" : "Remove")} {name} flag", [Update(InteractionTables.CreatureTemplate, template, template.With("npcflag", on ? template.U("npcflag") | bit : template.U("npcflag") & ~bit))]);
            grid.AddChild(check);
        }

        var line = new HBoxContainer();
        body.AddChild(line);
        line.AddChild(ClientStyle.Tag("Subname"));
        var subname = new LineEdit
        {
            Text = v.Subname,
            MaxLength = 100,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            PlaceholderText = "<Title under the name>"
        };
        subname.TextSubmitted += text => Commit("Edit subname", [Update(InteractionTables.CreatureTemplate, template, template.With("subname", text.Trim()))]);
        line.AddChild(subname);
        line.AddChild(ClientStyle.Tag("Gossip menu"));
        var menu = new LineEdit
        {
            Text = v.GossipMenuId.ToString(CultureInfo.InvariantCulture),
            CustomMinimumSize = new(70, 0)
        };
        menu.TextSubmitted += text => Try(() => _ = SetRootMenu(ParseUInt(text)));
        line.AddChild(menu);
    }

    async Task SetRootMenu(uint menu)
    {
        if (menu != 0 && !effective.All(InteractionTables.GossipMenu).Any(r => r.U("MenuID") == menu) && !snapshot!.Lookup.MenuExists(menu))
        {
            if (!await new InteractionSource(ConnectionPath()).MenuExistsAsync(menu, CancellationToken.None))
            {
                status = $"Not saved: gossip menu {menu} does not exist.";
                Render();
                return;
            }

            snapshot!.Lookup.Menus.Add(menu);
        }

        Commit("Change gossip menu", [Update(InteractionTables.CreatureTemplate, TemplateRow, TemplateRow.With("gossip_menu_id", menu))]);
    }

    void RenderSpellClickEditor(NpcInteraction v)
    {
        foreach (var click in v.SpellClicks)
        {
            var remove = new Button
            {
                Text = $"Remove {click.Name}"
            };
            remove.Pressed += () => Commit($"Remove spell click {click.SpellId}", [Delete(InteractionTables.SpellClick, click.Row)]);
            body.AddChild(remove);
        }

        SearchPicker("click", "Add spell click: spell name or ID…", term => Task.FromResult<IReadOnlyList<(string, Action)>>(
            (catalog?.SearchSpells(term) ?? []).Select(s => ($"{s.Name} ({s.Id})", (Action)(() => Commit($"Add spell click {s.Id}", [Insert(InteractionTables.SpellClick, Row.Of(("npc_entry", v.Entry), ("spell_id", s.Id), ("cast_flags", 1u), ("user_type", 0u)))])))).ToList()));
    }

    /// <summary>
    /// Search box with result buttons that survive re-renders; searches are bounded and read-only.
    /// </summary>
    void SearchPicker(string kind, string placeholder, Func<string, Task<IReadOnlyList<(string Label, Action Pick)>>> run)
    {
        var current = searches.GetValueOrDefault(kind, ("", []));
        var row = new HBoxContainer();
        body.AddChild(row);
        var term = new LineEdit
        {
            Text = current.Term,
            PlaceholderText = placeholder,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Name = "Search_" + kind
        };
        var go = new Button
        {
            Text = "Search"
        };
        async void Search()
        {
            string text = term.Text;
            status = "Searching…";
            Render();
            try
            {
                var results = await run(text);
                searches[kind] = (text, results.ToList());
                status = $"{results.Count} result(s) for \"{text}\".";
            }
            catch (Exception error)
            {
                status = CreatureSource.SafeError(error);
            }

            Render();
        }

        term.TextSubmitted += _ => Search();
        go.Pressed += Search;
        row.AddChild(term);
        row.AddChild(go);
        foreach (var (label, pick) in current.Results)
        {
            var result = new Button
            {
                Text = "+ " + label,
                Alignment = HorizontalAlignment.Left
            };
            result.Pressed += pick;
            body.AddChild(result);
        }
    }

    /// <summary>
    /// Run a picker search programmatically (smoke checks use the same path as the Search button).
    /// </summary>
    public IReadOnlyList<(string Label, Action Pick)> SearchResults(string kind) => searches.GetValueOrDefault(kind, ("", [])).Results;
}
