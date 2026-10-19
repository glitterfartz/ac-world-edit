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

    protected virtual void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }
}
