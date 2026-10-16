using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using static ACWorldEdit.Foundation.InteractionEdits;

namespace ACWorldEdit;

/// <summary>
/// Client-style window showing what right-clicking a creature does: gossip text and options,
/// quests, vendor items, trainer spells and other NPC actions, built from read-only database rows
/// plus the local interaction draft. Edit mode (InteractionWindow.Edit.cs) records changes in that
/// draft only. Conditions, SmartAI, C++ scripts and player state are listed, never evaluated.
/// </summary>
public partial class InteractionWindow : ContentEditorWindow
{
    enum Page
    {
        Gossip,
        Quests,
        QuestDetail,
        Vendor,
        Trainer,
        Info
    }

    const int VendorPageSize = 10;
    /// <summary>
    /// Summary for the creature inspector card, updated after each load or edit.
    /// </summary>
    public event Action<string>? SummaryChanged;
    /// <summary>
    /// Open the quest editor on a quest (0 = no quest).
    /// </summary>
    public event Action<uint>? QuestEditorRequested;
    /// <summary>
    /// Open the behavior (SmartAI) editor for a creature entry and spawn GUID.
    /// </summary>
    public event Action<uint, uint>? BehaviorRequested;

    ClientIcons icons = null!;
    CreatureSpawn? target;
    InteractionSnapshot? snapshot;
    NpcInteraction? view;
    RowSet effective = new();
    string sourceKey = "";
    int generation;
    CancellationTokenSource cancellation = new();
    string status = "";
    Page page;
    readonly Stack<uint> menus = new();
    uint questDetail;
    int vendorPage;
    Label title = null!, subtitle = null!, statusLabel = null!, footer = null!;
    VBoxContainer body = null!;
    HBoxContainer tabs = null!;

    public bool IsOpen => Visible;
    public uint? Entry => target?.TemplateId;
    public NpcInteraction? View => view;

    public InteractionWindow() : base("InteractionWindow")
    {
        CustomMinimumSize = new(470, 540);
        Size = new(470, 600);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready()
    {
        icons = new ClientIcons(() => Store());
        WatchDrafts();
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);
        var header = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Stop
        };
        header.GuiInput += DragHeader;
        root.AddChild(header);
        var titles = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        header.AddChild(titles);
        title = ClientStyle.Text("", 19, ClientStyle.Header);
        title.MouseFilter = MouseFilterEnum.Ignore;
        titles.AddChild(title);
        subtitle = ClientStyle.Text("", 13, ClientStyle.Faded);
        subtitle.MouseFilter = MouseFilterEnum.Ignore;
        titles.AddChild(subtitle);
        var behavior = new Button
        {
            Text = "Behavior…",
            TooltipText = "Edit what this creature does (SmartAI rules and speech)."
        };
        behavior.Pressed += () =>
        {
            if (target != null)
            {
                BehaviorRequested?.Invoke(target.TemplateId, target.Guid);
            }
        };
        header.AddChild(behavior);
        BuildEditToggle(header);
        var close = new Button
        {
            Text = "✕",
            TooltipText = "Close (Esc)"
        };
        close.Pressed += CloseWindow;
        header.AddChild(close);
        root.AddChild(new HSeparator());
        tabs = new HBoxContainer();
        root.AddChild(tabs);
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        root.AddChild(scroll);
        body = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(body);
        root.AddChild(new HSeparator());
        statusLabel = ClientStyle.Text("", 12, ClientStyle.Warning);
        root.AddChild(statusLabel);
        BuildHistoryRow(root);
        footer = ClientStyle.Text("", 11, ClientStyle.Faded);
        root.AddChild(footer);
    }

    public override void _Process(double delta)
    {
        if (icons.Poll() && Visible)
        {
            Render();
        }

        base._Process(delta);
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (Visible && input is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            CloseWindow();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Follow the inspector selection: load its interactions in the background for the summary.
    /// A different creature cancels the previous load; stale results are dropped.
    /// </summary>
    public void Track(CreatureSpawn? spawn)
    {
        // Same creature: keep the loaded (or loading) snapshot.
        if (spawn != null && target != null && spawn.TemplateId == target.TemplateId && spawn.Guid == target.Guid)
        {
            target = spawn;
            return;
        }

        target = spawn;
        snapshot = null;
        view = null;
        if (spawn == null)
        {
            generation++;
            cancellation.Cancel();
            SummaryChanged?.Invoke("Select a creature to see its interactions.");
            CloseWindow();
            return;
        }

        _ = Reload();
    }

    /// <summary>
    /// Show the window for a creature, starting at its gossip (or first available) page.
    /// </summary>
    public void Open(CreatureSpawn spawn)
    {
        Track(spawn);
        menus.Clear();
        page = Page.Gossip;
        vendorPage = 0;
        Visible = true;
        PlaceOnFirstOpen(0, 140);

        KeepSize();

        Render();
    }

    public void CloseWindow()
    {
        Visible = false;
        EndDrag();
    }

    /// <summary>
    /// Map/archive change: close and drop everything tied to the old world.
    /// </summary>
    public void Reset()
    {
        generation++;
        cancellation.Cancel();
        target = null;
        snapshot = null;
        view = null;
        CloseWindow();
    }

    /// <summary>
    /// Re-read the database snapshot (including draft-touched rows) on a worker, then rebuild.
    /// </summary>
    async Task Reload()
    {
        if (target is not { } spawn)
        {
            return;
        }

        int current = ++generation;
        cancellation.Cancel();
        cancellation = new();
        var token = cancellation.Token;
        if (snapshot == null)
        {
            status = "Loading interactions…";
            SummaryChanged?.Invoke(status);
        }

        try
        {
            var source = new InteractionSource(Connection());
            var cat = await EnsureCatalog();
            if (cat == null && Store() != null)
            {
                status = "Client spell/item tables unavailable; names and icons limited.";
            }

            sourceKey = source.SourceKey;
            var keys = draft?.Keys(sourceKey) ?? [];
            var apply = draft?.Frozen(sourceKey) ?? (rows => rows);
            var result = await Task.Run(() => source.ReadAsync(spawn.TemplateId, spawn.Guid, keys, apply, cat, token), token);
            if (current != generation)
            {
                return;
            }

            snapshot = result;
            status = draft != null && draft.Conflicts(sourceKey, result.Rows).Count > 0 ? "Some draft rows changed in the database since they were edited; those rows show database values." : "";
            Rebuild();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (current == generation)
            {
                status = CreatureSource.SafeError(error);
                SummaryChanged?.Invoke(status);
                Render();
            }
        }
    }

    /// <summary>
    /// Effective rows = snapshot + draft; rebuild the view, summary and page.
    /// </summary>
    void Rebuild()
    {
        if (snapshot == null)
        {
            return;
        }

        effective = draft?.Effective(sourceKey, snapshot.Rows) ?? snapshot.Rows;
        view = BuildView(effective);
        SummaryChanged?.Invoke(Summary(view));
        Render();
    }

    NpcInteraction BuildView(RowSet rows) => NpcInteraction.Build(snapshot!.Entry, snapshot.Guid, snapshot.Name, snapshot.ScriptName, snapshot.AIName, snapshot.SpawnCount, snapshot.Rows, rows, snapshot.Lookup, snapshot.Scripts, snapshot.Conditions);

    /// <summary>
    /// Rebuild the visible page from the current view. Cheap enough to redo after every change.
    /// </summary>
    void Render()
    {
        if (body == null)
        {
            return;
        }

        foreach (var child in body.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var child in tabs.GetChildren())
        {
            child.QueueFree();
        }

        statusLabel.Text = status;
        statusLabel.Visible = status.Length > 0;
        UpdateHistoryRow();
        if (view is not { } v)
        {
            title.Text = target != null ? "Loading…" : "";
            subtitle.Text = "";
            footer.Text = "";
            return;
        }

        title.Text = v.Name;
        subtitle.Text = (v.Subname.Length > 0 ? $"<{v.Subname}>   " : "") + $"Entry {v.Entry} · {v.SpawnCount} spawn(s)";
        AddTab("Gossip", Page.Gossip, editing || v.Menus.Count > 0 || (v.NpcFlag & NpcFlags.Gossip) != 0);
        AddTab("Quests", Page.Quests, editing || v.Starts.Length + v.Ends.Length > 0);
        AddTab("Vendor", Page.Vendor, editing || v.Vendor.Length > 0);
        AddTab("Trainer", Page.Trainer, editing || v.Trainer != null);
        AddTab("Info", Page.Info, true);
        switch (page)
        {
            case Page.Gossip:
                RenderGossip(v);
                break;
            case Page.Quests:
                RenderQuests(v);
                break;
            case Page.QuestDetail:
                RenderQuestDetail(v);
                break;
            case Page.Vendor:
                RenderVendor(v);
                break;
            case Page.Trainer:
                RenderTrainer(v);
                break;
            default:
                RenderInfo(v);
                break;
        }

        KeepSize();
        int conditions = v.Conditions.Values.Sum();
        footer.Text = $"SQL preview: conditions ({conditions}), SmartAI events ({v.Scripts.Length}){(v.ScriptName.Length > 0 ? $" and C++ script {v.ScriptName}" : "")} are not evaluated; player state is ignored.";
    }

    /// <summary>
    /// Intended window size. Wrapped labels created during a render report a stale (larger)
    /// minimum size until their layout settles, which clamps the panel larger and never shrinks it;
    /// _Process re-applies this every frame it differs, so the window converges after layout.
    /// </summary>
    protected override Vector2 TargetSize => new(editing ? 660 : 470, 600);

    void KeepSize() => Size = TargetSize;

    void AddTab(string text, Page target, bool show)
    {
        if (!show)
        {
            return;
        }

        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            ButtonPressed = page == target || (target == Page.Quests && page == Page.QuestDetail)
        };
        button.Pressed += () => Navigate(target);
        tabs.AddChild(button);
    }

    void Navigate(Page next)
    {
        page = next;
        if (next == Page.Gossip)
        {
            menus.Clear();
        }

        Render();
    }

    HBoxContainer IconRow(string iconPath, Control content, int size = 16)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(new TextureRect
        {
            Texture = icons.Get(iconPath) ?? icons.Get(ClientStyle.MissingIcon),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new(size, size)
        });
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(content);
        return row;
    }

    uint CurrentMenu(NpcInteraction v) => menus.Count > 0 ? menus.Peek() : v.GossipMenuId;

    void RenderGossip(NpcInteraction v)
    {
        uint menuId = CurrentMenu(v);
        if (!v.Menus.TryGetValue(menuId, out var menu))
        {
            body.AddChild(ClientStyle.Text(menuId == 0 ? "This creature has no gossip menu; the client opens its default greeting or the first service directly." : $"Gossip menu {menuId} could not be loaded.", 15, ClientStyle.Faded));
            if (editing)
            {
                RenderCreateMenu(v);
            }

            return;
        }

        if (menu.SharedBy > 1)
        {
            body.AddChild(ClientStyle.Text($"Menu {menu.MenuId} is shared by {menu.SharedBy} creatures: edits apply to all of them.", 12, ClientStyle.Warning));
            if (editing)
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

            if (editing)
            {
                RenderTextEditor(text);
            }
            else
            {
                body.AddChild(ClientStyle.Text(NpcInteraction.Render(text.Male.Length > 0 ? text.Male : text.Female), 16, text.Edited ? ClientStyle.Edited : null));
            }
        }

        body.AddChild(new HSeparator());
        if (menus.Count == 0 && (v.NpcFlag & NpcFlags.QuestGiver) != 0)
        {
            foreach (var quest in v.Starts)
            {
                body.AddChild(QuestButton(quest, ClientStyle.AvailableQuestIcon));
            }

            foreach (var quest in v.Ends)
            {
                body.AddChild(QuestButton(quest, ClientStyle.ActiveQuestIcon));
            }
        }

        foreach (var option in menu.Options)
        {
            if (editing)
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

        if (editing)
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
            page = Page.Gossip;
        }
        else
        {
            switch (option.Type)
            {
                case 2:
                    page = Page.Quests;
                    break;
                case 3:
                case 15:
                    page = Page.Vendor;
                    vendorPage = 0;
                    break;
                case 5:
                    page = Page.Trainer;
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

    Control QuestButton(QuestLinkView quest, string icon)
    {
        var button = new Button
        {
            Text = quest.Info != null ? $"{quest.Info.Title} ({quest.Info.Level})" : $"Quest {quest.Quest} (missing)",
            Alignment = HorizontalAlignment.Left
        };
        if (quest.Edited)
        {
            button.AddThemeColorOverride("font_color", ClientStyle.Edited);
        }

        button.Pressed += () =>
        {
            questDetail = quest.Quest;
            page = Page.QuestDetail;
            Render();
        };
        return IconRow(icon, button);
    }

    void RenderQuests(NpcInteraction v)
    {
        body.AddChild(ClientStyle.Text("Current Quests", 16, ClientStyle.Header));
        if (v.Ends.Length == 0)
        {
            body.AddChild(ClientStyle.Text("None", 13, ClientStyle.Faded));
        }

        foreach (var quest in v.Ends)
        {
            body.AddChild(QuestButton(quest, ClientStyle.ActiveQuestIcon));
        }

        body.AddChild(ClientStyle.Text("Available Quests", 16, ClientStyle.Header));
        if (v.Starts.Length == 0)
        {
            body.AddChild(ClientStyle.Text("None", 13, ClientStyle.Faded));
        }

        foreach (var quest in v.Starts)
        {
            body.AddChild(QuestButton(quest, ClientStyle.AvailableQuestIcon));
        }

        int conditions = v.Conditions.GetValueOrDefault(19);
        body.AddChild(ClientStyle.Text($"In game, availability depends on the player's level, race, class and earlier quests{(conditions > 0 ? $" and {conditions} SQL condition(s)" : "")}.", 11, ClientStyle.Faded));
        if (editing)
        {
            RenderQuestLinkEditor(v);
        }
    }

    void RenderQuestDetail(NpcInteraction v)
    {
        var info = v.Starts.Concat(v.Ends).FirstOrDefault(q => q.Quest == questDetail)?.Info;
        if (info == null)
        {
            body.AddChild(ClientStyle.Text($"Quest {questDetail} is not in quest_template.", 15, ClientStyle.Error));
        }
        else
        {
            body.AddChild(ClientStyle.Text(info.Title, 19, ClientStyle.Header));
            body.AddChild(ClientStyle.Text($"Level {info.Level} · requires level {info.MinLevel} · ID {info.Id}", 12, ClientStyle.Faded));
            body.AddChild(ClientStyle.Text(NpcInteraction.Render(info.Details), 15));
            body.AddChild(ClientStyle.Text("Quest Objectives", 16, ClientStyle.Header));
            body.AddChild(ClientStyle.Text(NpcInteraction.Render(info.Objectives), 15));
            body.AddChild(ClientStyle.Text("Rewards", 16, ClientStyle.Header));
            body.AddChild(ClientStyle.Text(info.RewardItems.Length == 0 && info.RewardMoney <= 0 ? "None listed" : string.Join("\n", info.RewardItems.Append(info.RewardMoney > 0 ? ClientStyle.Money(info.RewardMoney) : "").Where(s => s.Length > 0)), 15));
        }

        var row = new HBoxContainer();
        body.AddChild(row);
        var back = new Button
        {
            Text = "◂ Back"
        };
        back.Pressed += () => Navigate(Page.Quests);
        row.AddChild(back);
        var editor = new Button
        {
            Text = "Open in Quest Editor",
            TooltipText = "Quest content is read-only here; the quest editor will own it."
        };
        editor.Pressed += () => QuestEditorRequested?.Invoke(questDetail);
        row.AddChild(editor);
    }

    void RenderVendor(NpcInteraction v)
    {
        if (editing)
        {
            RenderVendorEditor(v);
            return;
        }

        int pages = Math.Max(1, (v.Vendor.Length + VendorPageSize - 1) / VendorPageSize);
        vendorPage = Math.Clamp(vendorPage, 0, pages - 1);
        var grid = new GridContainer
        {
            Columns = 2
        };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 8);
        body.AddChild(grid);
        foreach (var item in v.Vendor.Skip(vendorPage * VendorPageSize).Take(VendorPageSize))
        {
            var cell = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            var name = ClientStyle.Text(item.Info?.Name ?? $"Item {item.Item} (missing)", 14, item.Info != null ? ClientStyle.Quality(item.Info.Quality) : ClientStyle.Error);
            cell.AddChild(name);
            string price = item.ExtendedCost != 0 ? catalog?.DescribeExtendedCost(item.ExtendedCost) ?? $"Extended cost {item.ExtendedCost}" : ClientStyle.Money(item.Info?.BuyPrice ?? 0);
            cell.AddChild(ClientStyle.Text(price + (item.MaxCount > 0 ? $" · stock {item.MaxCount}" : "") + (item.FromReference != 0 ? $" · list {item.FromReference}" : ""), 12, item.Edited ? ClientStyle.Edited : ClientStyle.Faded));
            string icon = item.Info != null && catalog?.ItemIconPath(item.Info.DisplayId) is { } path ? path : ClientStyle.MissingIcon;
            var holder = IconRow(icon, cell, 37);
            holder.CustomMinimumSize = new(205, 44);
            grid.AddChild(holder);
        }

        int conditions = v.Conditions.GetValueOrDefault(23);
        body.AddChild(ClientStyle.Text($"Page {vendorPage + 1} of {pages} · {v.Vendor.Length} items{(conditions > 0 ? $" · {conditions} conditional" : "")} · event-only items (game_event_npc_vendor) are not shown.", 11, ClientStyle.Faded));
        var nav = new HBoxContainer();
        body.AddChild(nav);
        var previous = new Button
        {
            Text = "◂ Prev",
            Disabled = vendorPage == 0
        };
        previous.Pressed += () =>
        {
            vendorPage--;
            Render();
        };
        var next = new Button
        {
            Text = "Next ▸",
            Disabled = vendorPage >= pages - 1
        };
        next.Pressed += () =>
        {
            vendorPage++;
            Render();
        };
        nav.AddChild(previous);
        nav.AddChild(next);
    }

    void RenderTrainer(NpcInteraction v)
    {
        if (v.Trainer is not { } trainer)
        {
            body.AddChild(ClientStyle.Text("No trainer is assigned (creature_default_trainer).", 15, ClientStyle.Faded));
        }
        else
        {
            if (trainer.Greeting.Length > 0)
            {
                body.AddChild(ClientStyle.Text(NpcInteraction.Render(trainer.Greeting), 15));
            }

            body.AddChild(ClientStyle.Text($"Trainer {trainer.Id} · type {trainer.Type} · requirement {trainer.Requirement}{(trainer.SharedBy > 1 ? $" · shared by {trainer.SharedBy} creatures" : "")}", 12, trainer.SharedBy > 1 ? ClientStyle.Warning : ClientStyle.Faded));
            if (!editing)
            {
                foreach (var spell in trainer.Spells)
                {
                    body.AddChild(ClientStyle.Text($"{spell.Name}   ·   {ClientStyle.Money(spell.Cost)}   ·   level {spell.ReqLevel}{(spell.ReqSkillLine != 0 ? $"   ·   skill {spell.ReqSkillLine} {spell.ReqSkillRank}" : "")}", 14, spell.Edited ? ClientStyle.Edited : null));
                }
            }
        }

        if (editing)
        {
            RenderTrainerEditor(v);
        }
    }

    void RenderInfo(NpcInteraction v)
    {
        body.AddChild(ClientStyle.Text("NPC flags", 16, ClientStyle.Header));
        body.AddChild(ClientStyle.Text(v.NpcFlag == 0 ? "None (right-click does nothing)" : string.Join(", ", NpcFlags.Names(v.NpcFlag)) + $"   (npcflag 0x{v.NpcFlag:X})", 14));
        if (editing)
        {
            RenderTemplateEditor(v);
        }

        if ((v.NpcFlag & NpcFlags.FlightMaster) != 0 && catalog != null && target != null && catalog.NearestTaxiNode(target.Pose.Map, target.Pose.X, target.Pose.Y, target.Pose.Z) is { } node)
        {
            body.AddChild(ClientStyle.Text($"Flight master: nearest taxi node {node.Name} ({node.Id}), {node.Distance:0} yd.", 14));
        }

        body.AddChild(ClientStyle.Text("Spell click", 16, ClientStyle.Header));
        if (v.SpellClicks.Length == 0)
        {
            body.AddChild(ClientStyle.Text("None", 13, ClientStyle.Faded));
        }

        foreach (var click in v.SpellClicks)
        {
            body.AddChild(ClientStyle.Text($"{click.Name}   ·   cast flags {click.CastFlags}   ·   user type {click.UserType}", 14, click.Edited ? ClientStyle.Edited : null));
        }

        if (editing)
        {
            RenderSpellClickEditor(v);
        }

        body.AddChild(ClientStyle.Text("Scripted behaviour", 16, ClientStyle.Header));
        if (v.ScriptName.Length > 0)
        {
            body.AddChild(ClientStyle.Text($"C++ script {v.ScriptName} may replace gossip or quest handling entirely.", 14, ClientStyle.Warning));
        }

        if (v.AIName.Length > 0)
        {
            body.AddChild(ClientStyle.Text($"AI: {v.AIName}", 14));
        }

        foreach (var hook in v.Scripts)
        {
            string name = hook.EventType switch
            {
                64 => "gossip hello",
                62 => $"gossip select (menu {hook.EventParam1}, option {hook.EventParam2})",
                19 => "quest accepted",
                20 => "quest rewarded",
                _ => $"event {hook.EventType}"
            };
            body.AddChild(ClientStyle.Text($"SmartAI on {name} → action {hook.ActionType}{(hook.Comment.Length > 0 ? $"  ({hook.Comment})" : "")}", 13, ClientStyle.Warning));
        }

        if (v.ScriptName.Length == 0 && v.Scripts.Length == 0)
        {
            body.AddChild(ClientStyle.Text("No interaction scripts found.", 13, ClientStyle.Faded));
        }

        RenderIssues(v);
    }

    void RenderIssues(NpcInteraction v)
    {
        if (v.Issues.Length == 0)
        {
            return;
        }

        body.AddChild(ClientStyle.Text("Validation", 16, ClientStyle.Header));
        foreach (var issue in v.Issues)
        {
            var row = new HBoxContainer();
            row.AddChild(ClientStyle.Text((issue.Error ? "Error: " : "Warning: ") + issue.Message, 13, issue.Error ? ClientStyle.Error : ClientStyle.Warning));
            if (issue.Fix != null && draft != null)
            {
                var fix = new Button
                {
                    Text = "Fix"
                };
                var edits = issue.Fix;
                fix.Pressed += () => Commit("Fix npcflag", edits);
                row.AddChild(fix);
            }

            body.AddChild(row);
        }
    }

    public override void _ExitTree()
    {
        cancellation.Cancel();
    }
}
