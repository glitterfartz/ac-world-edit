using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Quest editor skeleton: read-only quest search and summary with links back to the creatures that
/// start or end the quest. Editing sections are placeholders with disabled Save/Undo; nothing is
/// drafted or written yet.
/// </summary>
public partial class QuestEditorWindow : PanelContainer
{
    public Func<string> ConnectionPath { get; set; } = () => CreatureSource.DefaultConfigPath;
    /// <summary>
    /// Fly to a loaded spawn of a creature entry; returns false when none is loaded nearby.
    /// </summary>
    public Func<uint, bool> FlyToEntry { get; set; } = _ => false;

    LineEdit search = null!;
    VBoxContainer results = null!, detail = null!;
    Label status = null!;
    Vector2? dragOffset;
    int generation;
    public uint? QuestId { get; private set; }
    public int ResultCount { get; private set; }

    public QuestEditorWindow()
    {
        Name = "QuestEditor";
        Visible = false;
        Theme = ClientStyle.Theme();
        AddThemeStyleboxOverride("panel", ClientStyle.Panel());
        CustomMinimumSize = new(560, 620);
        Size = new(560, 640);
    }

    public override void _Ready()
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);
        var header = new HBoxContainer();
        header.GuiInput += Drag;
        root.AddChild(header);
        var title = ClientStyle.Text("Quest Editor", 19, ClientStyle.Header);
        title.MouseFilter = MouseFilterEnum.Ignore;
        header.AddChild(title);
        var save = new Button
        {
            Text = "Save",
            Disabled = true,
            TooltipText = "Quest editing is not implemented yet."
        };
        var undo = new Button
        {
            Text = "Undo",
            Disabled = true,
            TooltipText = "Quest editing is not implemented yet."
        };
        header.AddChild(save);
        header.AddChild(undo);
        var close = new Button
        {
            Text = "✕"
        };
        close.Pressed += () => Visible = false;
        header.AddChild(close);
        root.AddChild(ClientStyle.Text("Skeleton: browse quests read-only. Editing sections are placeholders; nothing is drafted or written.", 11, ClientStyle.Warning));
        var row = new HBoxContainer();
        root.AddChild(row);
        search = new LineEdit
        {
            PlaceholderText = "Quest title or ID…",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        search.TextSubmitted += text =>
        {
            _ = Search();
        };
        row.AddChild(search);
        var go = new Button
        {
            Text = "Search"
        };
        go.Pressed += () => _ = Search();
        row.AddChild(go);
        var split = new HSplitContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddChild(split);
        var resultScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new(190, 0)
        };
        results = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        resultScroll.AddChild(results);
        split.AddChild(resultScroll);
        var detailScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        detail = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        detailScroll.AddChild(detail);
        split.AddChild(detailScroll);
        status = ClientStyle.Text("", 12, ClientStyle.Faded);
        root.AddChild(status);
        ShowEmpty();
    }

    void Drag(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            dragOffset = button.Pressed ? button.GlobalPosition - GlobalPosition : null;
            AcceptEvent();
        }
        else if (input is InputEventMouseMotion motion && dragOffset.HasValue)
        {
            GlobalPosition = (motion.GlobalPosition - dragOffset.Value).Clamp(Vector2.Zero, (GetViewportRect().Size - Size).Max(Vector2.Zero));
            AcceptEvent();
        }
    }

    /// <summary>
    /// Show the editor; a quest ID of 0 opens it empty (toolbar), otherwise loads that quest.
    /// </summary>
    public void Open(uint quest)
    {
        Visible = true;
        if (Position == Vector2.Zero)
        {
            Position = new(GetViewportRect().Size.X / 2 - Size.X / 2 + 60, 110);
        }


        if (quest == 0)
        {
            QuestId = null;
            ShowEmpty();
        }
        else
        {
            _ = Load(quest);
        }
    }

    /// <summary>
    /// Re-apply the intended size each frame it differs: wrapped labels report a stale larger
    /// minimum until laid out, which would otherwise grow the window permanently.
    /// </summary>
    public override void _Process(double delta)
    {
        var target = new Vector2(560, 640);
        if (Visible && Size != target)
        {
            Size = target;
        }
    }

    void ShowEmpty()
    {
        foreach (var child in detail.GetChildren())
        {
            child.QueueFree();
        }

        detail.AddChild(ClientStyle.Text("Search for a quest, or open one from a creature's interaction window.", 14, ClientStyle.Faded));
    }

    async Task Search()
    {
        int current = ++generation;
        status.Text = "Searching…";
        try
        {
            var found = await new QuestSource(ConnectionPath()).SearchAsync(search.Text, CancellationToken.None);
            if (current != generation)
            {
                return;
            }

            foreach (var child in results.GetChildren())
            {
                child.QueueFree();
            }

            foreach (var (id, title, level) in found)
            {
                var button = new Button
                {
                    Text = $"{title} ({level})",
                    Alignment = HorizontalAlignment.Left,
                    TooltipText = $"Quest {id}"
                };
                button.Pressed += () => _ = Load(id);
                results.AddChild(button);
            }

            ResultCount = found.Count;
            status.Text = $"{found.Count} result(s){(found.Count >= InteractionSource.MaxSearch ? " · limit reached" : "")}";
        }
        catch (Exception error)
        {
            status.Text = CreatureSource.SafeError(error);
        }
    }

    async Task Load(uint quest)
    {
        int current = ++generation;
        status.Text = $"Loading quest {quest}…";
        try
        {
            var details = await new QuestSource(ConnectionPath()).ReadAsync(quest, CancellationToken.None);
            if (current != generation)
            {
                return;
            }

            QuestId = quest;
            Render(quest, details);
            status.Text = "";
        }
        catch (Exception error)
        {
            status.Text = CreatureSource.SafeError(error);
        }
    }

    void Render(uint quest, QuestDetails? details)
    {
        foreach (var child in detail.GetChildren())
        {
            child.QueueFree();
        }

        if (details == null)
        {
            detail.AddChild(ClientStyle.Text($"Quest {quest} is not in quest_template.", 15, ClientStyle.Error));
            return;
        }

        var info = details.Info;
        detail.AddChild(ClientStyle.Text(info.Title, 19, ClientStyle.Header));
        detail.AddChild(ClientStyle.Text($"ID {info.Id} · level {info.Level} · requires {info.MinLevel} · type {details.QuestType} · flags 0x{details.Flags:X}", 12, ClientStyle.Faded));
        Section("Description", NpcInteraction.Render(info.Details));
        Section("Objectives text", NpcInteraction.Render(info.Objectives));
        foreach (string placeholder in new[] { "Objectives", "Rewards", "Requirements", "Quest chain (previous / next)", "Conditions" })
        {
            Section(placeholder, "Editing not implemented yet.", true);
        }

        detail.AddChild(ClientStyle.Text("Starters / Enders", 16, ClientStyle.Header));
        if (details.Givers.Length == 0)
        {
            detail.AddChild(ClientStyle.Text("No creature starts or ends this quest (it may use items or GameObjects).", 13, ClientStyle.Faded));
        }

        foreach (var giver in details.Givers)
        {
            var row = new HBoxContainer();
            row.AddChild(ClientStyle.Text($"{(giver.Starts ? "Starts" : "Ends")}: {giver.Name} ({giver.Entry})", 13));
            var fly = new Button
            {
                Text = "Fly to"
            };
            var entry = giver.Entry;
            fly.Pressed += () => status.Text = FlyToEntry(entry) ? "" : $"No loaded spawn of creature {entry} nearby.";
            row.AddChild(fly);
            detail.AddChild(row);
        }
    }

    void Section(string heading, string text, bool placeholder = false)
    {
        detail.AddChild(ClientStyle.Text(heading, 16, ClientStyle.Header));
        detail.AddChild(ClientStyle.Text(text.Length == 0 ? "(empty)" : text, placeholder ? 12 : 14, placeholder ? ClientStyle.Faded : null));
    }

    /// <summary>
    /// Smoke hooks: run the real search path, and confirm the editing controls stay disabled.
    /// </summary>
    public void SmokeSearch(string term)
    {
        search.Text = term;
        _ = Search();
    }

    public string SmokeStatus => status.Text;

    public bool SmokeEditingDisabled => FindChildren("*", "Button", true, false).OfType<Button>().Where(b => b.Text is "Save" or "Undo").All(b => b.Disabled);
}
