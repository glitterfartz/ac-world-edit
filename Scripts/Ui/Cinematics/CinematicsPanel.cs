using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Cinematics editor body shared by the two dialects: the title and subtitle, the tab bar, the
/// scrolling page, the status line and the draft history hooks. TrinityCore fills the tabs (player
/// choices now, conversations and scenes in later stages); AzerothCore shows why nothing applies.
/// </summary>
public abstract partial class CinematicsPanel : ContentPanel
{
    public enum Tab
    {
        Conversations,
        Scenes,
        PlayerChoices
    }

    /// <summary>The database dialect this panel edits.</summary>
    public abstract WorldDialect Dialect { get; }
    public Tab CurrentTab { get; protected set; } = Tab.PlayerChoices;
    /// <summary>The choice, conversation or scene open, or null when nothing is.</summary>
    public uint? CurrentId { get; protected set; }
    public bool HasItem => CurrentId.HasValue;
    /// <summary>Raised after every redraw so a host can refresh its Duplicate/Undo/Redo state.</summary>
    public event Action? Rendered;
    /// <summary>Raised when the tab changes so the host clears results that belong to the old tab.</summary>
    public event Action? TabChanged;

    protected Label title = null!, subtitle = null!, statusLabel = null!;
    protected VBoxContainer page = null!;
    HBoxContainer tabs = null!;

    public override void _Ready() => Build();

    protected abstract void Build();

    /// <summary>The title, tabs, page and status line every dialect panel draws above its own content.</summary>
    protected void BuildChrome()
    {
        AddThemeConstantOverride("separation", 4);
        title = ClientStyle.Text("", 19, ClientStyle.Header);
        AddChild(title);
        subtitle = ClientStyle.Text("", 12, ClientStyle.Faded);
        AddChild(subtitle);
        tabs = new HBoxContainer();
        AddChild(tabs);
        foreach (var (tab, label) in new[] { (Tab.Conversations, "Conversations"), (Tab.Scenes, "Scenes"), (Tab.PlayerChoices, "Player choices") })
        {
            var button = ButtonIn(tabs, label, () => ShowTab(tab));
            button.Name = "Tab" + tab;
            button.TooltipText = tab switch
            {
                Tab.Conversations => "Conversation templates, actors and line overrides.",
                Tab.Scenes => "Scene templates and their client script packages.",
                _ => "Player choices: the question card, its answers and rewards."
            };
        }

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        AddChild(scroll);
        page = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        page.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(page);
        AddChild(new HSeparator());
        statusLabel = ClientStyle.Text("", 12, ClientStyle.Warning);
        AddChild(statusLabel);
    }

    public void ShowStatus(string text)
    {
        status = text;
        UpdateStatus();
    }

    protected void UpdateStatus()
    {
        if (statusLabel != null)
        {
            statusLabel.Text = status;
            statusLabel.Visible = status.Length > 0;
        }
    }

    public virtual void ShowTab(Tab tab)
    {
        if (CurrentTab == tab)
        {
            return;
        }

        CurrentTab = tab;
        TabChanged?.Invoke();
        RenderTab();
    }

    /// <summary>Draw the current tab (or hide the body behind a one-line explanation).</summary>
    protected abstract void RenderTab();

    public abstract void Open(uint id);
    public abstract Task Load(uint id);
    public abstract Task NewItem(uint? copyFrom);
    public abstract Task<IReadOnlyList<(uint Id, string Name)>> Search(string term);
    protected abstract void Rebuild();
    protected abstract void ReloadAfterHistory();

    public void History(bool forward) => ApplyHistory(forward, Rebuild, ReloadAfterHistory);

    protected void Updated() => Rendered?.Invoke();
}
