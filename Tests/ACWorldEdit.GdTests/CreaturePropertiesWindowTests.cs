using ACWorldEdit;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// The creature properties frame, headless: the registry fills the left-hand picker in order and
/// choosing an entry swaps the visible section. No creature is loaded, so nothing touches the database.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class CreaturePropertiesWindowTests
{
    static CreaturePropertiesWindow Spawn()
    {
        string path = Path.Combine(Path.GetTempPath(), "acwe-gdtest-" + Guid.NewGuid().ToString("N") + ".json");
        var window = new CreaturePropertiesWindow
        {
            Drafts = new Foundation.ContentDrafts(path)
        };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(window);
        AutoFree(window);
        return window;
    }

    [TestCase]
    public void PickerListsEverySectionInRegistryOrder()
    {
        var window = Spawn();
        var picker = (ItemList)window.FindChild("SectionPicker", true, false)!;
        AssertThat(picker.ItemCount).IsEqual(CreatureSectionRegistry.Factories.Count);
        AssertThat(window.Sections.Select(s => s.Id).ToArray()).IsEqual(new[] { "gossip", "quests", "vendor", "trainer", "behavior", "speech", "info" });
        AssertThat(window.ActiveSection).IsEqual(CreaturePropertiesWindow.DefaultSection);
    }

    [TestCase]
    public void ShowingASectionHidesTheOthers()
    {
        var window = Spawn();
        window.ShowSection("vendor", null);
        AssertThat(window.ActiveSection).IsEqual("vendor");
        foreach (var section in window.Sections)
        {
            AssertThat(section.Visible).IsEqual(section.Id == "vendor");
        }

        window.ShowSection("speech", null);
        AssertThat(window.ActiveSection).IsEqual("speech");
        AssertThat(window.Section("speech")!.ScrollsItself).IsTrue();
    }
}
