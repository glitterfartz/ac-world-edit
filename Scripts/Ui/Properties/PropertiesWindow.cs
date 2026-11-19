using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The frame of a properties window: a header with Undo/Redo and close, a section picker on the left, the
/// chosen <see cref="EntitySection"/> on the right and a status line. The window knows nothing about the
/// entity; the derived window supplies its context, sections and header text, and the sections draw the
/// data. <see cref="CreaturePropertiesWindow"/> is the first client.
/// </summary>
public abstract partial class PropertiesWindow : ContentEditorWindow
{
    readonly List<EntitySection> sections = [];
    /// <summary>The sections of the open database's dialect, in picker order.</summary>
    readonly List<EntitySection> listed = [];
    WorldDialect? listedFor;
    readonly HashSet<EntitySection> stale = [];
    ItemList picker = null!;
    Button undo = null!, redo = null!;
    ScrollContainer scroll = null!;
    VBoxContainer scrollHost = null!, directHost = null!;

    protected PropertiesWindow(string name, string title) : base(name, title)
    {
    }

    /// <summary>The section a window opens on when none is asked for.</summary>
    protected abstract string DefaultSection { get; }

    /// <summary>The context the window follows (set before <see cref="BuildShell"/>).</summary>
    protected abstract IEntityContext Shell { get; }

    /// <summary>The window title and its line beneath, from the current state of the entity.</summary>
    protected abstract (string Title, string Subtitle) Header();

    protected EntitySection? Active { get; private set; }
    public bool IsOpen => Visible;
    public string ActiveSection => Active?.Id ?? "";
    public string SmokeTitle => TitleLabel.Text;
    public string SmokeSubtitle => SubtitleLabel.Text;

    /// <summary>Every section, in registry order.</summary>
    public IReadOnlyList<EntitySection> Sections => sections;

    /// <summary>The open database's sections, in picker order.</summary>
    protected IReadOnlyList<EntitySection> Listed => listed;

    /// <summary>Build the frame, add <paramref name="pages"/> and show the default section. Called once from the window's BuildEditor.</summary>
    protected void BuildShell(IEnumerable<EntitySection> pages)
    {
        Shell.Changed += OnContextChanged;
        BuildFrame();
        foreach (var section in pages)
        {
            sections.Add(section);
            section.Visible = false;
            if (section.ScrollsItself)
            {
                section.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            }

            (section.ScrollsItself ? directHost : scrollHost).AddChild(section);
        }

        SyncListed();
        ShowSection(DefaultSection, null);
    }

    void BuildFrame()
    {
        undo = ButtonIn(Actions, "Undo", () => History(false));
        redo = ButtonIn(Actions, "Redo", () => History(true));
        var split = new HBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        split.AddThemeConstantOverride("separation", 10);
        Body.AddChild(split);
        picker = new ItemList
        {
            Name = "SectionPicker",
            CustomMinimumSize = new(210, 0),
            FixedIconSize = new(18, 18),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            AllowReselect = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        picker.ItemSelected += index => ShowSection(listed[(int)index].Id, null);
        split.AddChild(picker);
        var content = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        split.AddChild(content);
        scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        content.AddChild(scroll);
        scrollHost = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        scroll.AddChild(scrollHost);
        directHost = new VBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        content.AddChild(directHost);
    }

    public override void _Process(double delta)
    {
        if (Visible && Shell.Icons.Poll())
        {
            Shell.Redraw();
        }
    }

    /// <summary>Make the window visible on a section (the last one when null).</summary>
    protected void Show(string? section)
    {
        Open();
        ShowSection(section ?? Active?.Id ?? DefaultSection, null);
    }

    /// <summary>
    /// List the sections of the open database's dialect; the picker is rebuilt when the dialect
    /// changes (switching client). Sections keep their ids across dialects, so a request for
    /// "gossip" or "vendor" finds the dialect's own page.
    /// </summary>
    void SyncListed()
    {
        var dialect = Shell.Dialect;
        if (listedFor == dialect)
        {
            return;
        }

        listedFor = dialect;
        listed.Clear();
        listed.AddRange(sections.Where(s => s.Supports(dialect)));
        picker.Clear();
        foreach (var section in listed)
        {
            picker.AddItem(section.Title);
        }

        foreach (var section in sections.Where(s => !listed.Contains(s)))
        {
            section.Visible = false;
        }

        if (Active != null && !listed.Contains(Active))
        {
            Active = null;
        }
    }

    public void ShowSection(string id, object? argument)
    {
        SyncListed();
        var next = listed.FirstOrDefault(s => s.Id == id) ?? listed.FirstOrDefault();
        if (next == null)
        {
            return;
        }

        Active = next;
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

        foreach (var section in sections.Where(s => s != Active))
        {
            stale.Add(section);
        }

        RefreshActive();
        UpdateChrome();
    }

    void RefreshActive()
    {
        if (Active == null)
        {
            return;
        }

        stale.Remove(Active);
        Active.Refresh();
    }

    void UpdateChrome()
    {
        SyncListed();
        var (heading, under) = Header();
        SetHeading(heading, under);
        SetStatus(Shell.Status);
        for (int i = 0; i < listed.Count; i++)
        {
            var section = listed[i];
            picker.SetItemText(i, section.Badge.Length > 0 ? $"{section.Title}  ·  {section.Badge}" : section.Title);
            if (section.IconPath.Length > 0)
            {
                picker.SetItemIcon(i, Shell.Icons.Get(section.IconPath));
            }

            if (section.Tip.Length > 0)
            {
                picker.SetItemTooltip(i, section.Tip);
            }

            picker.SetItemCustomFgColor(i, section.HasContent ? ClientStyle.Body : ClientStyle.Faded);
            if (section == Active)
            {
                picker.Select(i);
            }
        }

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
        Shell.Status = Drafts.History(this, forward);
        Shell.Rebuild();
    }

    public override void _ExitTree()
    {
        Shell?.Cancel();
    }
}
