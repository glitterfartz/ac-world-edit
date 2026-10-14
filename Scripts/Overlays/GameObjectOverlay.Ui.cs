using Godot;

namespace ACWorldEdit;

/// <summary>Read-only GameObject inspector; filtering/source settings live on the shared Scene page.</summary>
public partial class GameObjectOverlay
{
    public Control BuildPanel()
    {
        var page = InspectorUi.Page("GameObjects", out var scroll);
        page.AddChild(InspectorUi.Text("Nearby objects", 20));
        page.AddChild(InspectorUi.Text("Doors, chests and props from the world database.", 12, true));
        search = new LineEdit
        {
            PlaceholderText = "Name, GUID or template…",
            ClearButtonEnabled = true,
            MaxLength = 80
        };
        page.AddChild(search);
        search.TextChanged += _ =>
        {
            Populate();
            query.MarkSearchChanged();
        };
        list = new ItemList
        {
            Name = "GameObjectList",
            CustomMinimumSize = new(0, 255),
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        list.AddThemeFontSizeOverride("font_size", 14);
        page.AddChild(list);
        list.ItemSelected += index => Select(uint.Parse(list.GetItemMetadata((int)index).AsString()));
        status = InspectorUi.Text(queryStatus, 12, true);
        page.AddChild(status);
        var row = new HBoxContainer();
        page.AddChild(row);
        row.AddChild(InspectorUi.Button("Refresh", Refresh));
        fly = InspectorUi.Button("Fly to selection", FlyToSelection);
        fly.Disabled = true;
        row.AddChild(fly);
        var card = InspectorUi.Card(page, "Selection");
        title = InspectorUi.Text("Select a GameObject", 19);
        card.AddChild(title);
        identity = InspectorUi.Text("Click a model or choose one above", 12, true);
        card.AddChild(identity);
        details = InspectorUi.Text("Selection details appear here.", 13, true);
        card.AddChild(details);
        card.AddChild(new HSeparator());
        card.AddChild(InspectorUi.Text("Object previews are read-only. Open/close and script behavior are not simulated.", 12, true));
        return scroll;
    }

    /// <summary>
    /// Move the camera near the selected GameObject's source pose.
    /// </summary>
    public void FlyToSelection()
    {
        if (selected.HasValue && lookup.TryGetValue(selected.Value, out var item))
        {
            FocusRequested?.Invoke(item.Pose);
        }
    }
}
