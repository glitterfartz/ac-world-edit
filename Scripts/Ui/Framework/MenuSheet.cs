using Godot;

namespace ACWorldEdit;

/// <summary>
/// One entry of a menu sheet. A sheet is plain data, so the menu bar is built once from a value the
/// tests can inspect instead of from a chain of imperative calls.
/// </summary>
public abstract record MenuEntry;

public sealed record MenuCommand(string Label, Action Run, Func<bool>? Enabled = null, Key Accelerator = Key.None) : MenuEntry;

public sealed record MenuCheck(string Label, Action Toggle, Func<bool> Checked, Key Accelerator = Key.None) : MenuEntry;

public sealed record MenuSeparator : MenuEntry;

public sealed record MenuSubmenu(string Label, IReadOnlyList<MenuEntry> Items) : MenuEntry;

/// <summary>Top-level menus, in display order, for <see cref="EditorMenu.Load"/>.</summary>
public static class MenuSheet
{
    public static IReadOnlyList<(string Title, IReadOnlyList<MenuEntry> Items)> Of(
        params (string Title, MenuEntry[] Items)[] menus) =>
        menus.Select(menu => (menu.Title, (IReadOnlyList<MenuEntry>)menu.Items)).ToArray();
}
