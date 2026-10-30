using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// One page of a properties window, listed in its left-hand picker. The window frame
/// (<see cref="PropertiesWindow"/>) only needs the members here; a kind of entity derives its own base
/// (<see cref="CreatureSection"/>) that adds the context and helpers its pages share.
/// </summary>
public abstract partial class EntitySection : VBoxContainer
{
    protected EntitySection()
    {
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
    /// False dims the picker entry: the entity has nothing for this section yet (it stays reachable
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

    /// <summary>Rebuild the content from the context. Cheap enough to redo after every change.</summary>
    public abstract void Refresh();
}
