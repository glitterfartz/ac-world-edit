using ACWorldEdit;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// The editor theme keeps highlighted and hovered entries readable: light text on a dark highlight.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ClientStyleTests
{
    const double MinContrast = 4.5;

    static Color Fill(Theme theme, string name, string type) => ((StyleBoxFlat)theme.GetStylebox(name, type)).BgColor;

    [TestCase]
    public void SelectedAndHoveredListEntriesAreReadable()
    {
        var theme = ClientStyle.Theme();
        foreach (string state in new[] { "selected", "selected_focus", "hovered", "hovered_selected" })
        {
            string font = state switch { "selected" or "selected_focus" => "font_selected_color", "hovered" => "font_hovered_color", _ => "font_hovered_selected_color" };
            double ratio = ClientStyle.Contrast(theme.GetColor(font, "ItemList"), Fill(theme, state, "ItemList"));
            AssertThat(ratio >= MinContrast).IsTrue();
        }
    }

    [TestCase]
    public void OptionButtonAndDropdownStatesAreReadable()
    {
        var theme = ClientStyle.Theme();
        foreach (string state in new[] { "hover", "pressed", "hover_pressed", "normal", "focus" })
        {
            string font = state switch { "hover" => "font_hover_color", "pressed" => "font_pressed_color", "hover_pressed" => "font_hover_pressed_color", "focus" => "font_focus_color", _ => "font_color" };
            AssertThat(ClientStyle.Contrast(theme.GetColor(font, "OptionButton"), Fill(theme, state, "OptionButton")) >= MinContrast).IsTrue();
        }

        AssertThat(ClientStyle.Contrast(theme.GetColor("font_hover_color", "PopupMenu"), Fill(theme, "hover", "PopupMenu")) >= MinContrast).IsTrue();
        AssertThat(ClientStyle.Contrast(theme.GetColor("font_color", "PopupMenu"), Fill(theme, "panel", "PopupMenu")) >= MinContrast).IsTrue();
    }
}
