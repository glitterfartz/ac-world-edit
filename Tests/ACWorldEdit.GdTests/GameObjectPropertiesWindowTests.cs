using ACWorldEdit;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// The GameObject properties frame, headless: the registry fills the picker and choosing a
/// section swaps the visible page. No template is loaded, so nothing touches the database.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GameObjectPropertiesWindowTests
{
    static GameObjectPropertiesWindow Spawn(WorldDialect dialect = WorldDialect.AzerothCore)
    {
        string path = Path.Combine(Path.GetTempPath(), "acwe-gdtest-" + Guid.NewGuid().ToString("N") + ".json");
        var window = new GameObjectPropertiesWindow
        {
            Drafts = new Foundation.ContentDrafts(path),
            Connection = () => WorldConnection.DefaultFor(dialect)
        };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(window);
        AutoFree(window);
        return window;
    }

    [TestCase]
    public void PickerListsTheGameObjectPages()
    {
        foreach (var dialect in new[] { WorldDialect.AzerothCore, WorldDialect.TrinityCore })
        {
            var window = Spawn(dialect);
            var picker = (ItemList)window.FindChild("SectionPicker", true, false)!;
            AssertThat(window.Sections.Count).IsEqual(GameObjectSectionRegistry.Factories.Count);
            var listed = window.Sections.Where(s => s.Supports(dialect)).ToArray();
            AssertThat(picker.ItemCount).IsEqual(listed.Length);
            AssertThat(string.Join(",", listed.Select(s => s.Id))).IsEqual("template,type,quests,spawns");
            AssertThat(window.ActiveSection).IsEqual(GameObjectPropertiesWindow.DefaultSectionId);
            AssertThat(listed.All(s => s is GameObjectSection)).IsTrue();
        }
    }

    [TestCase]
    public void ShowingASectionHidesTheOthers()
    {
        var window = Spawn();
        window.ShowSection("type", null);
        AssertThat(window.ActiveSection).IsEqual("type");
        foreach (var section in window.Sections)
        {
            AssertThat(section.Visible).IsEqual(section.Id == "type");
        }

        window.ShowSection("spawns", null);
        AssertThat(window.ActiveSection).IsEqual("spawns");
        AssertThat(window.Section("spawns")).IsNotNull();
    }
}
