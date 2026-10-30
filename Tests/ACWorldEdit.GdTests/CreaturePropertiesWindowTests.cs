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
        // Every registered section exists; the picker lists the ones of the open database's dialect (AzerothCore here).
        AssertThat(window.Sections.Count).IsEqual(CreatureSectionRegistry.Factories.Count);
        var listed = window.Sections.Where(s => s.Supports(window.Context.Dialect)).ToArray();
        AssertThat(picker.ItemCount).IsEqual(listed.Length);
        AssertThat(listed.Select(s => s.Id).ToArray()).IsEqual(new[] { "behavior", "stats", "gossip", "quests", "speech", "template", "trainer", "vendor" });
        AssertThat(window.ActiveSection).IsEqual(CreaturePropertiesWindow.DefaultSectionId);
    }

    [TestCase]
    public void ShowingASectionHidesTheOthers()
    {
        var window = Spawn();
        window.ShowSection("vendor", null);
        AssertThat(window.ActiveSection).IsEqual("vendor");
        foreach (var section in window.Sections)
        {
            AssertThat(section.Visible).IsEqual(section.Id == "vendor" && section.Supports(window.Context.Dialect));
        }

        window.ShowSection("speech", null);
        AssertThat(window.ActiveSection).IsEqual("speech");
        AssertThat(window.Section("speech")!.ScrollsItself).IsTrue();
    }
}
