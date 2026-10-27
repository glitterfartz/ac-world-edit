using Godot;
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
/// (<see cref="CreatureSection.Supports"/>): the AzerothCore pages, then the TrinityCore ones.
/// </summary>
public static class CreatureSectionRegistry
{
    public static IReadOnlyList<Func<CreatureContext, CreatureSection>> Factories { get; } =
    [
        ctx => new AcBehaviorSection(ctx),
        ctx => new AcStatsSection(ctx),
        ctx => new AcGossipSection(ctx),
        ctx => new AcQuestsSection(ctx),
        ctx => new AcSpeechSection(ctx),
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
