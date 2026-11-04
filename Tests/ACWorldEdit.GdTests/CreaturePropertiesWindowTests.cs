using ACWorldEdit;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
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
    static CreaturePropertiesWindow Spawn(WorldDialect dialect = WorldDialect.AzerothCore)
    {
        string path = Path.Combine(Path.GetTempPath(), "acwe-gdtest-" + Guid.NewGuid().ToString("N") + ".json");
        var window = new CreaturePropertiesWindow
        {
            Drafts = new Foundation.ContentDrafts(path),
            Connection = () => WorldConnection.DefaultFor(dialect)
        };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(window);
        AutoFree(window);
        return window;
    }

    [TestCase]
    public void PickerListsEachDialectsSectionsInRegistryOrder()
    {
        AssertSections(WorldDialect.AzerothCore,
            "behavior,stats,gossip,quests,speech,template,trainer,vendor,appearance",
            typeof(TcStatsSection),
            typeof(AcStatsSection), typeof(AcGossipSection), typeof(AcTemplateSection), typeof(AcTrainerSection), typeof(AcVendorSection));
        AssertSections(WorldDialect.TrinityCore,
            "behavior,quests,speech,stats,gossip,template,trainer,vendor,appearance",
            typeof(AcStatsSection),
            typeof(TcStatsSection), typeof(TcGossipSection), typeof(TcTemplateSection), typeof(TcTrainerSection), typeof(TcVendorSection));
    }

    static void AssertSections(WorldDialect dialect, string expected, Type absent, params Type[] pages)
    {
        var window = Spawn(dialect);
        var picker = (ItemList)window.FindChild("SectionPicker", true, false)!;
        // Every registered section exists; the picker lists the ones of the open database's dialect.
        AssertThat(window.Sections.Count).IsEqual(CreatureSectionRegistry.Factories.Count);
        var listed = window.Sections.Where(s => s.Supports(dialect)).ToArray();
        AssertThat(picker.ItemCount).IsEqual(listed.Length);
        AssertThat(string.Join(",", listed.Select(s => s.Id))).IsEqual(expected);
        AssertThat(listed.Length).IsEqual(expected.Split(',').Length);
        foreach (var page in pages)
        {
            AssertThat(listed.Any(s => s.GetType() == page)).IsTrue();
        }

        AssertThat(listed.Any(s => s.GetType() == absent)).IsFalse();
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
