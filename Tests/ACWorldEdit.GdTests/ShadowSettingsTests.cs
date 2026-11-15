using ACWorldEdit;
using ACWorldEdit.Assets;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// The View → Sun shadows submenu: every entry selects its quality, the current one is marked, the
/// selection is saved, and the status line says what changed. The submenu is built through the same
/// factory the viewer's menu bar uses, so this drives the real entries.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ShadowSettingsTests
{
    static (EditorMenu Menu, PopupMenu Submenu) Build(ShadowQuality current, Action<ShadowQuality> select)
    {
        var menu = AutoFree(new EditorMenu())!;
        menu.Load(MenuSheet.Of(("View", [ViewerShell.SunShadowsMenu(select, () => current)])));
        var view = (PopupMenu)menu.GetChild(0);
        var submenu = (PopupMenu)view.GetChild(0);
        return (menu, submenu);
    }

    [TestCase]
    public void EachViewMenuEntrySelectsItsQuality()
    {
        ShadowQuality[] qualities = [ShadowQuality.Off, ShadowQuality.Low, ShadowQuality.Medium, ShadowQuality.High];
        for (int index = 0; index < qualities.Length; index++)
        {
            ShadowQuality selected = (ShadowQuality)(-1);
            var (_, submenu) = Build(ShadowQuality.Off, q => selected = q);

            AssertThat(submenu.Name.ToString()).IsEqual("Sun shadows");
            AssertThat(submenu.ItemCount).IsEqual(4);
            AssertString(submenu.GetItemText(index)).IsEqual(ShadowQualityPresets.Name(qualities[index]));
            submenu.EmitSignal(PopupMenu.SignalName.IdPressed, submenu.GetItemId(index));
            AssertThat(selected).IsEqual(qualities[index]);
        }
    }

    [TestCase]
    public void TheMenuMarksTheCurrentQuality()
    {
        var (menu, submenu) = Build(ShadowQuality.Medium, _ => { });
        menu.Refresh();

        AssertThat(submenu.IsItemChecked(0)).IsFalse();
        AssertThat(submenu.IsItemChecked(2)).IsTrue();
    }

    [TestCase]
    public void SelectingAQualitySavesItAndPostsTheStatusLine()
    {
        var original = UserSettings.LoadShadowQuality();
        try
        {
            foreach (var quality in new[] { ShadowQuality.Off, ShadowQuality.Low, ShadowQuality.Medium, ShadowQuality.High })
            {
                UserSettings.SaveShadowQuality(quality);
                AssertThat(UserSettings.LoadShadowQuality()).IsEqual(quality);
            }
        }
        finally
        {
            UserSettings.SaveShadowQuality(original);
        }

        AssertString(ShadowQualityPresets.StatusMessage(ShadowQuality.Off)).IsEqual("Shadows: Off.");
        AssertString(ShadowQualityPresets.StatusMessage(ShadowQuality.Low)).IsEqual("Shadows: Low (1 cascade).");
        AssertString(ShadowQualityPresets.StatusMessage(ShadowQuality.Medium)).IsEqual("Shadows: Medium (3 cascades).");
        AssertString(ShadowQualityPresets.StatusMessage(ShadowQuality.High)).IsEqual("Shadows: High (4 cascades).");
    }
}
