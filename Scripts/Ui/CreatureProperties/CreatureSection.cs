using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// One page of the creature properties window, listed in its left-hand picker. A section is a
/// content control that draws from the shared <see cref="CreatureContext"/>. To add a feature to the
/// creature window, derive from this class and add one line to <see cref="CreatureSectionRegistry"/>.
/// </summary>
public abstract partial class CreatureSection : VBoxContainer
{
    protected CreatureContext Ctx { get; }

    protected CreatureSection(CreatureContext ctx)
    {
        Ctx = ctx;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 6);
    }

    /// <summary>Stable key used to open the window on a section.</summary>
    public abstract string Id { get; }

    /// <summary>Caption in the section picker.</summary>
    public abstract string Title { get; }

    /// <summary>Client icon shown beside the title in the picker (empty for none).</summary>
    public virtual string IconPath => "";

    /// <summary>Picker suffix such as a count (empty for none).</summary>
    public virtual string Badge => "";

    /// <summary>
    /// False dims the picker entry: the creature has nothing for this section yet (it stays reachable
    /// because adding content happens inside it).
    /// </summary>
    public virtual bool HasContent => true;

    /// <summary>
    /// The section fills the window height and scrolls its own content, instead of the window
    /// scrolling it.
    /// </summary>
    public virtual bool ScrollsItself => false;

    /// <summary>
    /// Whether the section works with this dialect's database; the window lists only the sections of
    /// the open database. The AzerothCore sections are the default.
    /// </summary>
    public virtual bool Supports(WorldDialect dialect) => dialect == WorldDialect.AzerothCore;

    /// <summary>The section became the shown one; <paramref name="argument"/> comes from ShowSection.</summary>
    public virtual void OnShown(object? argument)
    {
    }

    /// <summary>The creature changed (or was cleared): drop state tied to the previous one.</summary>
    public virtual void OnCreatureChanged(CreatureSpawn? creature)
    {
    }

    /// <summary>Rebuild the content from the context. Cheap enough to redo after every change.</summary>
    public abstract void Refresh();

    /// <summary>The line shown while the creature's rows load, or when no creature is selected.</summary>
    protected Label Placeholder() => ClientStyle.Text(Ctx.Target != null ? "Loading…" : "Select a creature in the world.", 15, ClientStyle.Faded);

    /// <summary>Move a panel the context shares (behavior, quest) into <paramref name="parent"/>; it can only live in one place.</summary>
    protected static void Host(Control panel, Control parent)
    {
        if (panel.GetParent() != parent)
        {
            panel.GetParent()?.RemoveChild(panel);
            parent.AddChild(panel);
        }
    }

    /// <summary>
    /// One search box's term and last results, kept across redraws. <see cref="Reset"/> (creature
    /// changed) also drops results of a search still running.
    /// </summary>
    protected sealed class SearchState<T>
    {
        public string Term = "";
        public IReadOnlyList<T> Results = [];
        public int Generation;
        public bool Searching;

        public void Reset()
        {
            Generation++;
            Term = "";
            Results = [];
            Searching = false;
        }
    }

    /// <summary>
    /// A search box and button: the term is trimmed to 80 characters and <paramref name="run"/> fills
    /// <see cref="SearchState{T}.Results"/>, reporting "n {noun}(s) found." on the status line. The
    /// button is disabled while searching and, when <paramref name="needsClient"/>, without a client lookup.
    /// </summary>
    protected void SearchBox<T>(Control parent, SearchState<T> state, string name, string placeholder, string noun, bool needsClient, Func<string, Task<IReadOnlyList<T>>> run)
    {
        var box = new HBoxContainer();
        parent.AddChild(box);
        var term = new LineEdit { Name = name, Text = state.Term, PlaceholderText = placeholder, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var search = new Button { Text = "Search", Disabled = state.Searching || (needsClient && Ctx.Lookup() == null) };
        term.TextChanged += text => state.Term = text;
        async void Search()
        {
            if (state.Searching || (needsClient && Ctx.Lookup() == null)) return;
            string text = term.Text.Trim();
            if (text.Length == 0) return;
            text = text[..Math.Min(80, text.Length)];
            state.Term = text;
            int generation = ++state.Generation;
            state.Searching = true;
            Ctx.Status = $"Searching {noun}s…";
            Ctx.Redraw();
            try
            {
                var found = await run(text);
                if (generation != state.Generation) return;
                state.Results = found;
                Ctx.Status = $"{found.Count} {noun}(s) found.";
            }
            catch (Exception error)
            {
                if (generation != state.Generation) return;
                Ctx.Status = DbErrors.Safe(error);
            }
            if (generation != state.Generation) return;
            state.Searching = false;
            Ctx.Redraw();
        }
        search.Pressed += Search;
        term.TextSubmitted += _ => Search();
        box.AddChild(term);
        box.AddChild(search);
    }

    protected virtual void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }
}

/// <summary>
/// The sections of the creature properties window, in picker order (alphabetical by title). Adding a
/// feature to the window is one section class plus one line here; the window, context and tests need
/// no other change. The window lists the sections that support the open database's dialect
/// (<see cref="CreatureSection.Supports"/>): the AzerothCore pages, then the TrinityCore ones; Behavior
/// and Speech serve both.
/// </summary>
public static class CreatureSectionRegistry
{
    public static IReadOnlyList<Func<CreatureContext, CreatureSection>> Factories { get; } =
    [
        ctx => new BehaviorSection(ctx),
        ctx => new AcStatsSection(ctx),
        ctx => new AcGossipSection(ctx),
        ctx => new AcQuestsSection(ctx),
        ctx => new SpeechSection(ctx),
        ctx => new AcTemplateSection(ctx),
        ctx => new AcTrainerSection(ctx),
        ctx => new AcVendorSection(ctx),
        ctx => new TcStatsSection(ctx),
        ctx => new TcGossipSection(ctx),
        ctx => new TcQuestsSection(ctx),
        ctx => new TcTemplateSection(ctx),
        ctx => new TcTrainerSection(ctx),
        ctx => new TcVendorSection(ctx)
    ];

    public static List<CreatureSection> Create(CreatureContext ctx) => Factories.Select(factory => factory(ctx)).ToList();
}
