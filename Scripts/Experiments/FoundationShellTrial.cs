using Godot;
using ACWorldEdit.Foundation;
namespace ACWorldEdit.Experiments;

/// <summary>Matched fixture workflow for standalone/editor-dock trials. Not the production editor UI.</summary>
[Tool]
public partial class FoundationShellTrial : VBoxContainer
{
    DraftWorkspace? workspace;
    ItemList items = null!;
    Label result = null!;
    readonly Dictionary<string, Button> actions = [];
    public override void _Ready()
    {
        string folder = ProjectSettings.GlobalizePath("res://artifacts/shell-trial/" + Guid.NewGuid().ToString("N"));
        workspace = new DraftWorkspace(folder);
        AddChild(new Label { Text = "PHASE 0 SHELL TRIAL — synthetic placements only" });
        AddChild(new Label { Text = "Search 2843 → place → move +1 X → undo/redo → review → reopen" });
        var search = new LineEdit { Text = "2843", PlaceholderText = "Fixture template ID (2843)", CustomMinimumSize = new(280, 0) }; AddChild(search);
        items = new ItemList { CustomMinimumSize = new(280, 180) }; AddChild(items);
        var buttons = new HBoxContainer(); AddChild(buttons);
        void Button(string text, Action action)
        {
            var button = new Godot.Button { Text = text }; button.Pressed += () =>
            {
                try { action(); Refresh(); } catch (Exception e) { result.Text = e.Message; }
            }; buttons.AddChild(button); actions.Add(text, button);
        }
        Button("Place", () =>
        {
            if (search.Text != "2843") throw new ArgumentException("Only fixture template 2843 is configured.");
            workspace.Place(2843, new(0, -100, 200, 30, 0));
        });
        Button("Move", () => { var id = Selected(); var pose = workspace.Placements[id].Pose; workspace.Move(id, pose with { X = pose.X + 1 }); });
        Button("Undo", () => workspace.Undo()); Button("Redo", () => workspace.Redo());
        Button("Review", () => result.Text = System.Text.Json.JsonSerializer.Serialize(workspace.Placements.Values));
        Button("Reopen", () => { workspace.Checkpoint(); workspace.Dispose(); workspace = new DraftWorkspace(folder); });
        result = new Label { Text = "No source writes. Review displays draft state; SQL integrity is tested separately.", AutowrapMode = TextServer.AutowrapMode.WordSmart }; AddChild(result);
        Refresh();
    }
    public void RunSmoke()
    {
        foreach (string action in new[] { "Place", "Move", "Undo", "Redo", "Review", "Reopen" })
            actions[action].EmitSignal(Godot.Button.SignalName.Pressed);
        if (workspace!.Placements.Count != 1 || workspace.Placements.Values.Single().Pose.X != -99)
            throw new InvalidOperationException("Shell fixture workflow failed: " + result.Text);
        string before = System.Text.Json.JsonSerializer.Serialize(workspace.Placements);
        // A disposable projection has no authority over persistent domain entities.
        var projection = new Node3D(); AddChild(projection); RemoveChild(projection); projection.Free();
        if (before != System.Text.Json.JsonSerializer.Serialize(workspace.Placements))
            throw new InvalidOperationException("Projection unload changed draft state.");
    }
    Guid Selected()
    {
        var selected = items.GetSelectedItems();
        if (selected.Length == 0) throw new InvalidOperationException("Select a fixture placement first.");
        return Guid.Parse(items.GetItemMetadata(selected[0]).AsString());
    }
    void Refresh()
    {
        items.Clear();
        foreach (var placement in workspace!.Placements.Values.OrderBy(p => p.StableId))
        {
            int index = items.AddItem($"{placement.TemplateId}: X={placement.Pose.X:R}, map={placement.Pose.Map}");
            items.SetItemMetadata(index, placement.StableId.ToString());
        }
        if (items.ItemCount > 0) items.Select(0);
    }
    public override void _ExitTree() => workspace?.Dispose();
}
