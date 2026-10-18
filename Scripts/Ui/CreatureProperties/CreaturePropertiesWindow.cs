using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Everything editable about the selected creature in one window: a section picker on the left
/// (<see cref="CreatureSectionRegistry"/>) and the chosen section on the right. The window is only
/// the frame - header, Undo/Redo, Edit toggle, status - while the data lives in
/// <see cref="CreatureContext"/> and the pages in <see cref="CreatureSection"/> classes.
/// </summary>
public partial class CreaturePropertiesWindow : ContentEditorWindow
{
    public const string DefaultSection = "gossip";

    public Func<AreaLookup?> Areas { get; set; } = () => null;
    public Func<uint, string, Task<string>> FlyToEntry { get; set; } = (_, _) => Task.FromResult("");
    /// <summary>Open the behavior of another creature entry (quest giver cards).</summary>
    public Action<uint> BehaviorForEntryRequested { get; set; } = _ => { };

    public CreatureContext Context { get; private set; } = null!;
    readonly List<CreatureSection> sections = [];
    readonly HashSet<CreatureSection> stale = [];
    CreatureSection? active;
    CreatureSpawn? lastSelection;
    ItemList picker = null!;
    Label title = null!, subtitle = null!, statusLabel = null!;
    Button editToggle = null!, undo = null!, redo = null!;
    ScrollContainer scroll = null!;
    VBoxContainer scrollHost = null!, directHost = null!;

    public bool IsOpen => Visible;
    public uint? Entry => Context?.Target?.TemplateId;
    public NpcInteraction? View => Context?.View;
    public bool Editing => Context?.Editing ?? false;
    public string ActiveSection => active?.Id ?? "";
    public IReadOnlyList<CreatureSection> Sections => sections;
    public event Action<string>? SummaryChanged;
    public event Action<uint>? QuestEditorRequested;

    protected override Vector2 TargetSize => new(980, 700);

    public CreaturePropertiesWindow() : base("CreatureProperties")
    {
        CustomMinimumSize = new(760, 520);
        Size = TargetSize;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Ready()
    {
        Context = new CreatureContext(Drafts, () => Store())
        {
            Connection = () => Connection(),
            Areas = () => Areas(),
            FlyToEntry = (entry, name) => FlyToEntry(entry, name),
            BehaviorForEntryRequested = entry => BehaviorForEntryRequested(entry)
        };
        Context.Changed += OnContextChanged;
        Context.CreatureChanged += creature =>
        {
            foreach (var section in sections)
            {
                section.OnCreatureChanged(creature);
            }
        };
        Context.SummaryChanged += text => SummaryChanged?.Invoke(text);
        Context.QuestEditorRequested += quest => QuestEditorRequested?.Invoke(quest);
        Context.SectionRequested += ShowSection;
        BuildFrame();
        foreach (var section in CreatureSectionRegistry.Create(Context))
        {
            sections.Add(section);
            section.Visible = false;
            if (section.ScrollsItself)
            {
                section.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            }

            (section.ScrollsItself ? directHost : scrollHost).AddChild(section);
            picker.AddItem(section.Title);
        }

        ShowSection(DefaultSection, null);
    }

    void BuildFrame()
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
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
        editToggle = new Button
        {
            Text = "Edit",
            ToggleMode = true,
            TooltipText = "Edit these records."
        };
        editToggle.Toggled += value => Context.Editing = value;
        header.AddChild(editToggle);
        undo = ButtonIn(header, "Undo", () => History(false));
        redo = ButtonIn(header, "Redo", () => History(true));
        ButtonIn(header, "✕", CloseWindow).TooltipText = "Close (Esc)";
        root.AddChild(new HSeparator());
        var split = new HBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        split.AddThemeConstantOverride("separation", 10);
        root.AddChild(split);
        picker = new ItemList
        {
            Name = "SectionPicker",
            CustomMinimumSize = new(150, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            AllowReselect = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        picker.ItemSelected += index => ShowSection(sections[(int)index].Id, null);
        split.AddChild(picker);
        var content = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        split.AddChild(content);
        scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        content.AddChild(scroll);
        scrollHost = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        scroll.AddChild(scrollHost);
        directHost = new VBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        content.AddChild(directHost);
        statusLabel = ClientStyle.Text("", 12, ClientStyle.Warning);
        root.AddChild(statusLabel);
    }

    public override void _Process(double delta)
    {
        if (Visible && Context.Icons.Poll())
        {
            Context.Redraw();
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
    /// Follow the inspector selection (called every frame). A creature opened by entry (a quest giver
    /// card) stays until the selection actually changes.
    /// </summary>
    public void Track(CreatureSpawn? spawn)
    {
        if (spawn?.Guid == lastSelection?.Guid && spawn?.TemplateId == lastSelection?.TemplateId)
        {
            return;
        }

        lastSelection = spawn;
        Context.Track(spawn);
        if (spawn == null)
        {
            CloseWindow();
        }
    }

    /// <summary>Show the window for a creature on a section (the last one when null).</summary>
    public void Open(CreatureSpawn? spawn, string? section = null)
    {
        lastSelection = spawn;
        Context.Track(spawn);
        Show(section);
    }

    /// <summary>Show the window for a creature entry without a spawn (quest giver cards).</summary>
    public void OpenEntry(uint entry, string name, string? section = null)
    {
        Context.Track(new CreatureSpawn(0, entry, name, new WorldPose(0, 0, 0, 0, 0), 1, 1));
        Show(section);
    }

    void Show(string? section)
    {
        Visible = true;
        PlaceOnFirstOpen(0, 60);
        Size = TargetSize;
        ShowSection(section ?? active?.Id ?? DefaultSection, null);
    }

    public void CloseWindow()
    {
        Visible = false;
        EndDrag();
    }

    /// <summary>Map/archive change: close and drop everything tied to the old world.</summary>
    public void Reset()
    {
        lastSelection = null;
        Context?.Reset();
        CloseWindow();
    }

    public void ShowSection(string id, object? argument)
    {
        var next = sections.FirstOrDefault(s => s.Id == id) ?? sections.FirstOrDefault();
        if (next == null)
        {
            return;
        }

        active = next;
        foreach (var section in sections)
        {
            section.Visible = section == next;
        }

        scroll.Visible = !next.ScrollsItself;
        directHost.Visible = next.ScrollsItself;
        next.OnShown(argument);
        RefreshActive();
        UpdateChrome();
    }

    void OnContextChanged()
    {
        if (picker == null)
        {
            return;
        }

        foreach (var section in sections.Where(s => s != active))
        {
            stale.Add(section);
        }

        RefreshActive();
        UpdateChrome();
    }

    void RefreshActive()
    {
        if (active == null)
        {
            return;
        }

        stale.Remove(active);
        active.Refresh();
    }

    void UpdateChrome()
    {
        var view = Context.View;
        title.Text = view?.Name ?? (Context.Target != null ? "Loading…" : "No creature selected");
        subtitle.Text = view == null ? "" : (view.Subname.Length > 0 ? $"<{view.Subname}>   " : "") + $"Entry {view.Entry} · {view.SpawnCount} spawn(s)";
        statusLabel.Text = Context.Status;
        statusLabel.Visible = Context.Status.Length > 0;
        for (int i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            picker.SetItemText(i, section.Badge.Length > 0 ? $"{section.Title}  ·  {section.Badge}" : section.Title);
            picker.SetItemCustomFgColor(i, section.HasContent ? ClientStyle.Body : ClientStyle.Faded);
            if (section == active)
            {
                picker.Select(i);
            }
        }

        editToggle.Visible = active?.UsesEditMode == true;
        editToggle.Disabled = Context.Draft == null;
        editToggle.SetPressedNoSignal(Context.Editing);
        SyncHistory();
    }

    void SyncHistory()
    {
        var draft = Drafts.Draft;
        undo.Disabled = draft?.CanUndo != true;
        redo.Disabled = draft?.CanRedo != true;
        undo.TooltipText = draft?.LastLabel is { } last ? $"Undo \"{last}\"" : "";
        redo.TooltipText = draft?.NextLabel is { } next ? $"Redo \"{next}\"" : "";
    }

    void History(bool forward)
    {
        // The window is the sender, so the context and the embedded panels reload as for a foreign edit.
        Context.Status = Drafts.History(this, forward);
        Context.Rebuild();
    }

    public CreatureSection? Section(string id) => sections.FirstOrDefault(s => s.Id == id);

    public override void _ExitTree()
    {
        Context?.Cancel();
    }
}
