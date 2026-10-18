using Godot;

namespace ACWorldEdit;

/// <summary>Read-only GameObject inspector; filtering/source settings live on the shared Scene page.</summary>
public partial class GameObjectOverlay
{
    public Control BuildPanel()
    {
        var page = InspectorUi.Page("GameObjects", out var scroll);
        (search, list, status, fly) = InspectorUi.ListSection(page, "Nearby objects", "Doors, chests and props from the world database.",
            "Name, GUID or template…", queryStatus, Select, Refresh, FlyToSelection,
            () =>
            {
                Populate();
                query.MarkSearchChanged();
            }, 255, listFontSize: 14, listName: "GameObjectList", flyDisabled: true);
        var selection = InspectorUi.SelectionCard(page, "Select a GameObject", "Click a model or choose one above");
        var card = selection.Box;
        title = selection.Title;
        identity = selection.Identity;
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
