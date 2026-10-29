using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// "Events": how game events decide which spawns show. The mode items carry their <see cref="EventPreviewMode"/>
/// as the item ID, and the picker appears only for the mode that uses it.
/// </summary>
public sealed partial class VisibilityPreviewPanel
{
    const int MaxChosenEvents = 128;

    OptionButton eventMode = null!;
    MenuButton eventPicker = null!;
    readonly HashSet<int> selectedEvents = [];
    string eventSignature = "";

    EventPreviewMode EventMode => (EventPreviewMode)eventMode.GetItemId(eventMode.Selected);

    void BuildEventsSection()
    {
        Section("Events");
        eventMode = new OptionButton
        {
            Name = "EventPreview",
            FitToLongestItem = false,
            TooltipText = "Seasonal and holiday spawns belong to a game event. No event is started on the server."
        };
        eventMode.AddItem("Follow the database schedule", (int)EventPreviewMode.Scheduled);
        eventMode.AddItem("No events", (int)EventPreviewMode.None);
        eventMode.AddItem("Only the events I choose", (int)EventPreviewMode.Selected);
        eventMode.AddItem("All events", (int)EventPreviewMode.All);
        AddChild(eventMode);
        eventMode.ItemSelected += _ =>
        {
            ShowEventPicker();
            Edited();
        };
        eventPicker = new MenuButton
        {
            Name = "EventPicker",
            Text = "Choose events…",
            CustomMinimumSize = new(0, 32),
            TooltipText = "Preview chosen event IDs independently of their database dates."
        };
        AddChild(eventPicker);
        var menu = eventPicker.GetPopup();
        menu.HideOnCheckableItemSelection = false;
        menu.MaxSize = new(380, 500);
        menu.IdPressed += id =>
        {
            int key = (int)id;
            if (!selectedEvents.Contains(key) && selectedEvents.Count >= MaxChosenEvents)
            {
                validation.Text = $"Choose up to {MaxChosenEvents} events, or use All events.";
                return;
            }

            if (!selectedEvents.Add(key))
            {
                selectedEvents.Remove(key);
            }

            menu.SetItemChecked(menu.GetItemIndex(key), selectedEvents.Contains(key));
            ShowEventPicker();
            Edited();
        };
    }

    void SyncEvents(SpawnPreview value)
    {
        eventMode.Select(eventMode.GetItemIndex((int)value.Events));
        ShowEventPicker();
    }

    /// <summary>The button reads as the choice made, and says why there is nothing to choose when the area has no events.</summary>
    void ShowEventPicker()
    {
        eventPicker.Visible = EventMode == EventPreviewMode.Selected;
        eventPicker.Disabled = eventSignature.Length == 0;
        eventPicker.Text = eventSignature.Length == 0 ? "No events in this area"
            : selectedEvents.Count == 0 ? "Choose events…"
            : selectedEvents.Count == 1 ? "1 event chosen" : $"{selectedEvents.Count} events chosen";
    }

    /// <summary>List the events the loaded spawns belong to. The list is left alone while the picker is open.</summary>
    public void SetEventCatalog(WorldEventInfo[] catalog)
    {
        string signature = string.Join(',', catalog.Select(e => e.Id));
        var menu = eventPicker.GetPopup();
        if (signature == eventSignature || menu.Visible)
        {
            return;
        }

        eventSignature = signature;
        menu.Clear();
        foreach (var entry in catalog)
        {
            menu.AddCheckItem($"{entry.Id}  ·  {entry.Name}", entry.Id);
            menu.SetItemChecked(menu.ItemCount - 1, selectedEvents.Contains(entry.Id));
        }

        ShowEventPicker();
    }
}
