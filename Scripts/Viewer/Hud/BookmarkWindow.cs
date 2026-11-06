using Godot;

namespace ACWorldEdit;

/// <summary>
/// The camera bookmarks window (toolbar → Bookmarks): Save location stores where the camera is,
/// and each row names its map, zone and position with Open (teleport there) and ✕ (delete) buttons.
/// Bookmarks live in <see cref="BookmarkStore"/>; the viewer supplies the file, the capture and the
/// teleport.
/// </summary>
public partial class BookmarkWindow : ContentEditorWindow
{
    /// <summary>Where the bookmarks file is (per client profile); smoke runs get a throwaway path.</summary>
    public Func<string> FilePath { get; set; } = () => "";
    /// <summary>Snapshot the camera's current map, position, angles and display names.</summary>
    public Func<CameraBookmark> Capture { get; set; } = () => new(0, 0, 0, 0, 0, 0, "", "");
    /// <summary>Whether saving makes sense (a map is open); otherwise Save location is disabled.</summary>
    public Func<bool> CanCapture { get; set; } = () => true;
    /// <summary>Teleport the camera to a bookmark; returns a status line.</summary>
    public Func<CameraBookmark, Task<string>> OpenBookmark { get; set; } = _ => Task.FromResult("");

    Label statusLabel = null!;
    VBoxContainer body = null!;
    Button saveButton = null!;

    public BookmarkWindow() : base("Bookmarks")
    {
        Size = new(640, 420);
    }

    public override void _Ready()
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);
        var header = AddHeader(root, "Bookmarks");
        var save = ButtonIn(header.Row, "Save location", SaveCurrent);
        save.Name = "BookmarkSave";
        save.TooltipText = "Save the camera's current map and position.";
        saveButton = save;
        var close = ButtonIn(header.Row, "✕", () => Visible = false);
        close.TooltipText = "Close the bookmarks window.";
        body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        root.AddChild(body);
        statusLabel = ClientStyle.Text("", 12, ClientStyle.Faded);
        root.AddChild(statusLabel);
        Rebuild();
    }

    /// <summary>Show the window with a fresh read of the bookmarks file.</summary>
    public void Open()
    {
        Visible = true;
        PlaceOnFirstOpen(0, 80);
        Rebuild();
    }

    void SaveCurrent()
    {
        var bookmarks = BookmarkStore.Load(FilePath());
        bookmarks.Add(Capture());
        BookmarkStore.Save(FilePath(), bookmarks);
        status = $"Saved {bookmarks.Count} bookmark{(bookmarks.Count == 1 ? "" : "s")}.";
        Rebuild();
    }

    void RemoveAt(int index)
    {
        var bookmarks = BookmarkStore.Load(FilePath());
        if (index < 0 || index >= bookmarks.Count)
        {
            return;
        }

        bookmarks.RemoveAt(index);
        BookmarkStore.Save(FilePath(), bookmarks);
        status = bookmarks.Count == 0 ? "No bookmarks saved yet." : $"{bookmarks.Count} bookmark{(bookmarks.Count == 1 ? "" : "s")}.";
        Rebuild();
    }

    async void OpenAt(int index)
    {
        var bookmarks = BookmarkStore.Load(FilePath());
        if (index < 0 || index >= bookmarks.Count)
        {
            return;
        }

        status = $"Opening {bookmarks[index].MapName}…";
        SyncStatus();
        status = await OpenBookmark(bookmarks[index]);
        SyncStatus();
    }

    void Rebuild()
    {
        if (body == null)
        {
            return;
        }

        saveButton.Disabled = !CanCapture();
        foreach (var child in body.GetChildren())
        {
            child.QueueFree();
        }

        var bookmarks = BookmarkStore.Load(FilePath());
        if (bookmarks.Count == 0 && status.Length == 0)
        {
            status = "No bookmarks saved yet.";
        }

        var grid = new GridContainer { Columns = 5, Name = "BookmarkTable" };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 6);
        body.AddChild(grid);
        foreach (string heading in new[] { "Map", "Zone", "Position", "", "" })
        {
            grid.AddChild(ClientStyle.Text(heading, 12, ClientStyle.Faded));
        }

        for (int i = 0; i < bookmarks.Count; i++)
        {
            var bookmark = bookmarks[i];
            int row = i;
            grid.AddChild(ClientStyle.Text(bookmark.MapName, 13));
            var zone = ClientStyle.Text(bookmark.Zone, 13);
            zone.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            grid.AddChild(zone);
            grid.AddChild(ClientStyle.Text($"({bookmark.X:0.#}, {bookmark.Y:0.#}, {bookmark.Z:0.#})", 13));
            var open = new Button { Text = "Open", Name = "BookmarkOpen_" + row, TooltipText = $"Teleport to {bookmark.MapName} ({bookmark.X:0.#}, {bookmark.Y:0.#}, {bookmark.Z:0.#})." };
            open.Pressed += () => OpenAt(row);
            grid.AddChild(open);
            var remove = new Button { Text = "✕", Name = "BookmarkDelete_" + row, TooltipText = "Delete this bookmark." };
            remove.Pressed += () => RemoveAt(row);
            grid.AddChild(remove);
        }

        SyncStatus();
    }

    void SyncStatus() => statusLabel.Text = status;

    /// <summary>Row count, for tests.</summary>
    public int SmokeRows => BookmarkStore.Load(FilePath()).Count;

    /// <summary>The status line, for tests.</summary>
    public string SmokeStatus => status;
}
