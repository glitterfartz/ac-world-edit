using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Opt-in helpers for --interaction-smoke. They act through the window's real controls (emitting
/// button, checkbox and line-edit signals) rather than calling commands directly.
/// </summary>
public partial class CreaturePropertiesWindow
{
    public string SmokeStatus => Context.Status;
    public uint SmokeCurrentMenu => (Section("gossip") as AcGossipSection)?.SmokeCurrentMenu ?? 0;
    public bool SmokeOnVendorPage => active?.Id == "vendor";
    public bool SmokeOnQuestDetail => active?.Id == "quests" && SmokeQuestDetail != 0;
    public uint SmokeQuestDetail => (Section("quests") as AcQuestsSection)?.SmokeQuestDetail ?? 0;
    public bool SmokeLoaded => Context.View != null && Context.Snapshot != null;
    AcCreatureView? view => Context.AcView;
    Node body => (Node?)active ?? this;

    IEnumerable<T> Live<T>(Node root) where T : Node => root.FindChildren("*", typeof(T).Name, true, false).OfType<T>().Where(n => !n.IsQueuedForDeletion() && !IsDying(n) && (n is not CanvasItem item || item.IsVisibleInTree()));

    static bool IsDying(Node node)
    {
        for (var n = node; n != null; n = n.GetParent())
        {
            if (n.IsQueuedForDeletion())
            {
                return true;
            }
        }

        return false;
    }

    public void SmokeShow(string name)
    {
        ShowSection(name.ToLowerInvariant(), null);
    }

    /// <summary>
    /// Press the first enabled live button whose text starts with the prefix.
    /// </summary>
    public bool SmokePress(string prefix)
    {
        var button = Live<Button>(this).FirstOrDefault(b => !b.Disabled && b.Text.StartsWith(prefix, StringComparison.Ordinal));
        button?.EmitSignal(BaseButton.SignalName.Pressed);
        return button != null;
    }

    /// <summary>
    /// Follow the first gossip option of a type (or with a submenu when type is null), as a click.
    /// </summary>
    public bool SmokeFollow(uint? type)
    {
        if (view == null || !view.Menus.TryGetValue(SmokeCurrentMenu, out var menu))
        {
            return false;
        }

        var option = menu.Options.FirstOrDefault(o => !o.HiddenByFlags && (type == null ? o.ActionMenu != 0 : o.Type == type));
        if (option == null)
        {
            return false;
        }

        return SmokePress(InteractionText.Render(option.Text));
    }

    public int SmokeIconsShown => Live<TextureRect>(body).Count(t => t.Texture != null && t.CustomMinimumSize.X >= 37);

    public bool SmokeSearch(string kind, string term)
    {
        var edit = Live<LineEdit>(body).FirstOrDefault(l => l.Name == "Search_" + kind);
        if (edit == null)
        {
            return false;
        }

        edit.Text = term;
        edit.EmitSignal(LineEdit.SignalName.TextSubmitted, term);
        return true;
    }

    public bool SmokeSetGreeting(string text)
    {
        var editor = Live<TextEdit>(body).FirstOrDefault();
        if (editor == null)
        {
            return false;
        }

        editor.Text = text;
        return SmokePress("Save text");
    }

    public bool SmokeToggleFlag(string name, bool on)
    {
        var check = Live<CheckBox>(body).FirstOrDefault(c => c.Text == name);
        if (check == null)
        {
            return false;
        }

        check.ButtonPressed = on;
        return true;
    }

    /// <summary>
    /// Press the ✕ on the vendor editor row of an item.
    /// </summary>
    public bool SmokeRemoveVendorItem(uint item)
    {
        // The item heading is nested in the card's title row, next to its Remove button.
        var row = Live<HBoxContainer>(body).FirstOrDefault(r => r.GetChildren().OfType<Button>().Any(b => b.Text == "Remove")
            && r.FindChildren("*", "Label", true, false).OfType<Label>().Any(l => l.Text.EndsWith($"({item})", StringComparison.Ordinal)));
        var remove = row?.GetChildren().OfType<Button>().FirstOrDefault(b => b.Text == "Remove");
        remove?.EmitSignal(BaseButton.SignalName.Pressed);
        return remove != null;
    }

    /// <summary>
    /// Close and reopen the draft file, then rebuild: edits must survive.
    /// </summary>
    public void SmokeReopenDraft()
    {
        Context.ReopenDraft();
    }

    /// <summary>
    /// Layout diagnostics: the widest descendants by combined minimum size.
    /// </summary>
    public string SmokeWidest() => string.Join(" | ", Live<Control>(this).OrderByDescending(c => c.GetCombinedMinimumSize().X).Take(6).Select(c => $"{c.GetType().Name}:{c.Name}:{c.GetCombinedMinimumSize()}:{(c is Label l ? l.Text[..Math.Min(30, l.Text.Length)] : c is Button b ? b.Text : "")}")) + $" | window size {Size}";
}
