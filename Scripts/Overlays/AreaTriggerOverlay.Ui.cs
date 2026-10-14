using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// AreaTriggers tab: nearby list, read-only shape/link details and the entry point into the
/// behavior editor. Shapes and links are not edited here.
/// </summary>
public partial class AreaTriggerOverlay
{
    ItemList list = null!;
    LineEdit search = null!;
    Label status = null!, title = null!, identity = null!, details = null!;
    Button fly = null!, copyGo = null!, behavior = null!;

    public Control BuildPanel()
    {
        var page = InspectorUi.Page("AreaTriggers", out var scroll);
        page.AddChild(InspectorUi.Text("AreaTriggers", 20));
        page.AddChild(InspectorUi.Text("Server areatrigger volumes in the streamed region. Turn the layer on in Scene.", 12, true));
        search = new LineEdit
        {
            PlaceholderText = "ID, script, teleport or tavern name…",
            ClearButtonEnabled = true,
            MaxLength = 80
        };
        search.TextChanged += _ =>
        {
            query.MarkSearchChanged();
        };
        page.AddChild(search);
        list = new ItemList
        {
            CustomMinimumSize = new(0, 170),
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        list.ItemSelected += index => Select(uint.Parse(list.GetItemMetadata((int)index).AsString(), CultureInfo.InvariantCulture));
        page.AddChild(list);
        status = InspectorUi.Text(queryStatus, 12, true);
        page.AddChild(status);
        var row = new HBoxContainer();
        page.AddChild(row);
        row.AddChild(InspectorUi.Button("Refresh", Refresh));
        fly = InspectorUi.Button("Fly to selection", FlyToSelection);
        row.AddChild(fly);

        var card = InspectorUi.Card(page, "Selection");
        title = InspectorUi.Text("Select an AreaTrigger", 19);
        card.AddChild(title);
        identity = InspectorUi.Text("Click a volume or choose one above", 12, true);
        card.AddChild(identity);
        var actions = new HBoxContainer();
        card.AddChild(actions);
        behavior = InspectorUi.Button("Edit behavior", () =>
        {
            if (selected is uint entry && lookup.TryGetValue(entry, out var trigger))
            {
                BehaviorRequested?.Invoke(trigger);
            }
        });
        copyGo = InspectorUi.Button("Copy .go", () =>
        {
            if (GoCommand() is { } command)
            {
                DisplayServer.ClipboardSet(command);
                message = "Copied " + command + "  (GMs need .debug areatriggers to run scripts)";
                ShowSelection();
            }
        });
        actions.AddChild(behavior);
        actions.AddChild(copyGo);
        details = InspectorUi.Text("", 12, true);
        card.AddChild(details);

        var legend = InspectorUi.Card(page, "Colors");
        foreach (var (kind, text) in new[]
        {
            (AreaTriggerKind.SmartAI, "SmartAI rules (ScriptName SmartTrigger)"),
            (AreaTriggerKind.Script, "C++ AreaTrigger script"),
            (AreaTriggerKind.Teleport, "Teleport (areatrigger_teleport)"),
            (AreaTriggerKind.Quest, "Exploration quest credit"),
            (AreaTriggerKind.Tavern, "Tavern rest area"),
            (AreaTriggerKind.Unlinked, "No server link (client-only or unused)")
        })
        {
            var entry = InspectorUi.Text("■  " + text, 12);
            entry.Modulate = KindColors[kind].Lerp(Colors.White, 0.25f);
            legend.AddChild(entry);
        }

        legend.AddChild(InspectorUi.Text("The client decides when a trigger fires from its own AreaTrigger.dbc; the server re-checks these volumes and runs the linked behavior.", 11, true));
        ShowSelection();
        return scroll;
    }

    void PopulateList()
    {
        list.Clear();
        foreach (var trigger in triggers)
        {
            int index = list.AddItem($"{trigger.Entry}   ·   {Summary(trigger)}");
            list.SetItemMetadata(index, trigger.Entry.ToString(CultureInfo.InvariantCulture));
            list.SetItemCustomFgColor(index, KindColors[trigger.Kind].Lerp(Colors.White, 0.35f));
            list.SetItemTooltip(index, trigger.Shape);
            if (selected == trigger.Entry)
            {
                list.Select(index);
            }
        }
    }

    /// <summary>Refresh the selection card from the snapshot row.</summary>
    void ShowSelection()
    {
        if (selected is not uint entry || !lookup.TryGetValue(entry, out var t))
        {
            title.Text = "Select an AreaTrigger";
            identity.Text = "Click a volume or choose one above";
            details.Text = message;
            return;
        }

        title.Text = $"AreaTrigger {t.Entry}";
        identity.Text = $"Map {t.Map}   /   {Summary(t)}";
        List<string> lines =
        [
            t.Shape,
            FormattableString.Invariant($"Center X {t.X:F3}   Y {t.Y:F3}   Z {t.Z:F3}"),
            t.IsSphere ? "Orientation is unused for spheres." : FormattableString.Invariant($"Orientation {t.Orientation:0.###} rad ({Mathf.RadToDeg((float)t.Orientation):0.#}°); length runs along it"),
            "",
            "Server links",
            "  Script: " + (t.ScriptName.Length == 0 ? "none" : t.ScriptName == AreaTriggerRecord.SmartTrigger ? $"SmartTrigger (SmartAI, {t.SmartRows} rule rows)" : t.ScriptName + " (C++)"),
        ];
        if (t.ScriptName != AreaTriggerRecord.SmartTrigger && t.SmartRows > 0)
        {
            lines.Add($"  {t.SmartRows} SmartAI rows exist but do not run without ScriptName SmartTrigger.");
        }

        if (t.Teleport is { } teleport)
        {
            lines.Add(FormattableString.Invariant($"  Teleport: map {teleport.Map} at {teleport.X:F2}, {teleport.Y:F2}, {teleport.Z:F2}{(teleport.Name.Length > 0 ? " · " + teleport.Name : "")}"));
        }

        if (t.QuestId != 0)
        {
            lines.Add($"  Quest credit: {t.QuestId}{(t.QuestTitle.Length > 0 ? " · " + t.QuestTitle : "")}");
        }

        if (t.Tavern.Length > 0)
        {
            lines.Add("  Tavern: " + t.Tavern);
        }

        if (message.Length > 0)
        {
            lines.Add("");
            lines.Add(message);
        }

        details.Text = string.Join('\n', lines);
        for (int i = 0; i < list.ItemCount; i++)
        {
            if (list.GetItemMetadata(i).AsString() == t.Entry.ToString(CultureInfo.InvariantCulture))
            {
                list.Select(i);
                list.CallDeferred(ItemList.MethodName.EnsureCurrentIsVisible);
                break;
            }
        }
    }

    void UpdateActions()
    {
        fly.Disabled = !selected.HasValue;
        copyGo.Disabled = !selected.HasValue;
        // C++ scripts take priority over SmartTrigger (one ScriptName per trigger), but the editor
        // still opens so the user can see that and switch deliberately.
        behavior.Disabled = !selected.HasValue;
    }
}
