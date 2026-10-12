using Godot;
using ACWorldEdit.Data;

namespace ACWorldEdit;
/// <summary>Focused entity page; scene visibility and external connection settings live on the Scene tab.</summary>
public partial class CreatureOverlay
{
    Button fly = null!, undo = null!, redo = null!;
    public Control BuildPanel()
    {
        var page = InspectorUi.Page("Creatures", out var scroll);
        page.AddChild(InspectorUi.Text("Nearby creatures", 20));
        page.AddChild(InspectorUi.Text("Search filters the scene before the result limit.", 12, true));
        config = new LineEdit
        {
            Text = System.Environment.GetEnvironmentVariable("ACWORLD_SERVER_CONFIG") ?? CreatureSource.DefaultConfigPath,
            PlaceholderText = "External worldserver.conf path"
        };
        filter = new LineEdit
        {
            Name = "CreatureSearch",
            PlaceholderText = "Name, GUID or template…",
            ClearButtonEnabled = true,
            MaxLength = 80
        };
        page.AddChild(filter);
        filter.TextChanged += _ =>
        {
            PopulateList();
            searchPending = true;
            searchChanged = System.Diagnostics.Stopwatch.GetTimestamp();
        };
        list = new ItemList
        {
            CustomMinimumSize = new(0, 255),
            ClipContents = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        list.AddThemeFontSizeOverride("font_size", 14);
        page.AddChild(list);
        list.ItemSelected += index => Select(uint.Parse(list.GetItemMetadata((int)index).AsString()));
        status = InspectorUi.Text("Waiting for a world…", 12, true);
        page.AddChild(status);
        var actions = new HBoxContainer();
        page.AddChild(actions);
        actions.AddChild(InspectorUi.Button("Refresh", Refresh));
        fly = InspectorUi.Button("Fly to selection", () =>
        {
            if (selectedGuid.HasValue && markers.TryGetValue(selectedGuid.Value, out var marker))
            {
                FocusRequested?.Invoke(marker.Spawn with { Pose = PoseOf(marker.Spawn) });
            }
        });
        fly.Disabled = true;
        actions.AddChild(fly);
        var card = InspectorUi.Card(page, "Selection");
        selectionName = InspectorUi.Text("Select a creature", 19);
        card.AddChild(selectionName);
        selectionMeta = InspectorUi.Text("Click a model or choose one above", 12, true);
        card.AddChild(selectionMeta);
        details = InspectorUi.Text("Selection details appear here.", 13, true);
        card.AddChild(details);
        card.AddChild(new HSeparator());
        card.AddChild(InspectorUi.Text("Animation preview", 12, true));
        animation = new OptionButton
        {
            FitToLongestItem = false,
            Disabled = true
        };
        animation.AddItem("Stand / idle", 0);
        card.AddChild(animation);
        animation.ItemSelected += index =>
        {
            previewAnimation = (ushort)animation.GetItemId((int)index);
            modelUpdate = 0;
        };
        var history = new HBoxContainer();
        card.AddChild(history);
        undo = InspectorUi.Button("Undo move", () => History(false));
        redo = InspectorUi.Button("Redo move", () => History(true));
        history.AddChild(undo);
        history.AddChild(redo);
        card.AddChild(InspectorUi.Text("Drag a colored axis to move. Changes save to your local draft.", 12, true));
        return scroll;
    }

    void UpdateInspectorActions()
    {
        fly.Disabled = !LayerEnabled || !selectedGuid.HasValue || !markers.ContainsKey(selectedGuid.Value);
        undo.Disabled = draft == null || !draft.CanUndo;
        redo.Disabled = draft == null || !draft.CanRedo;
    }
}
