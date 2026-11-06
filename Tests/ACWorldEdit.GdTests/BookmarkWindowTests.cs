using ACWorldEdit;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>The bookmarks window saves the camera, lists one row per bookmark and opens or deletes it.</summary>
[TestSuite]
[RequireGodotRuntime]
public class BookmarkWindowTests
{
    static (BookmarkWindow Window, List<CameraBookmark> Opened, string File) Spawn()
    {
        string file = Path.Combine(Path.GetTempPath(), "acwe-gdtest-" + Guid.NewGuid().ToString("N") + ".json");
        var opened = new List<CameraBookmark>();
        var window = new BookmarkWindow
        {
            Drafts = new Foundation.ContentDrafts(Path.Combine(Path.GetTempPath(), "acwe-gdtest-" + Guid.NewGuid().ToString("N") + ".json")),
            FilePath = () => file,
            Capture = () => new CameraBookmark(0, 1.5, 2.5, 3.5, 0.5f, 0.1f, "Eastern Kingdoms", "Goldshire"),
            CanCapture = () => true,
            OpenBookmark = bookmark => { opened.Add(bookmark); return Task.FromResult("opened"); }
        };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(window);
        AutoFree(window);
        window.Open();
        return (window, opened, file);
    }

    [TestCase]
    public void SaveListsOpenAndDelete()
    {
        var (window, opened, file) = Spawn();
        AssertThat(window.SmokeRows).IsEqual(0);
        AssertThat(File.Exists(file)).IsFalse();

        ((Button)window.FindChild("BookmarkSave", true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(window.SmokeRows).IsEqual(1);
        AssertThat(File.Exists(file)).IsTrue();
        var saved = BookmarkStore.Load(file);
        AssertThat(saved.Count).IsEqual(1);
        AssertThat(saved[0].MapName).IsEqual("Eastern Kingdoms");
        AssertThat(saved[0].Zone).IsEqual("Goldshire");

        ((Button)window.FindChild("BookmarkOpen_0", true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(opened.Count).IsEqual(1);
        AssertThat(opened[0].Map).IsEqual(0u);
        AssertThat(window.SmokeStatus).IsEqual("opened");

        ((Button)window.FindChild("BookmarkDelete_0", true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
        AssertThat(window.SmokeRows).IsEqual(0);
        AssertThat(BookmarkStore.Load(file).Count).IsEqual(0);
        // The rebuilt table holds only its five headings (the stale grid is queued, so take the newest).
        var grids = window.FindChildren("BookmarkTable", owned: false).OfType<GridContainer>().ToList();
        AssertThat(grids.Last().GetChildCount()).IsEqual(5);
    }

    [TestCase]
    public void CorruptFileReadsAsEmpty()
    {
        var (window, _, file) = Spawn();
        File.WriteAllText(file, "not json");
        window.Open();
        AssertThat(window.SmokeRows).IsEqual(0);
        AssertThat(window.SmokeStatus).IsEqual("No bookmarks saved yet.");
    }
}
