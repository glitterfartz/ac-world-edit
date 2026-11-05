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
        (search, list, status, fly) = InspectorUi.ListSection(page, "AreaTriggers", "AreaTrigger volumes in the streamed region. Turn the layer on in Scene.",
            "ID, spawn, script, teleport or tavern name…", queryStatus, id => Select(AreaTriggerKey.FromListId(id)), Refresh, FlyToSelection, query.MarkSearchChanged, 170);
        var selection = InspectorUi.SelectionCard(page, "Select an AreaTrigger", "Click a volume or choose one above");
        var card = selection.Box;
        title = selection.Title;
        identity = selection.Identity;
        var actions = new HBoxContainer();
        card.AddChild(actions);
        behavior = InspectorUi.Button("Edit behavior", () =>
        {
            if (Selected is { } trigger)
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
            (AreaTriggerKind.SmartAI, "SmartAI rules (SmartTrigger, or SmartAreaTriggerAI for a spawn)"),
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

        legend.AddChild(InspectorUi.Text("The client decides when a client trigger fires from its own AreaTrigger data; the server re-checks these volumes and runs the linked behavior. Spawned triggers (TrinityCore) are server objects that run their own script.", 11, true));
        ShowSelection();
        return scroll;
    }

    void PopulateList()
    {
        list.Clear();
        foreach (var trigger in triggers)
        {
            string id = trigger.IsSpawn ? $"Spawn {trigger.SpawnId}" : trigger.Entry.ToString(CultureInfo.InvariantCulture);
            int index = list.AddItem($"{id}   ·   {Summary(trigger)}");
            list.SetItemMetadata(index, trigger.Key.ListId.ToString(CultureInfo.InvariantCulture));
            list.SetItemCustomFgColor(index, KindColors[trigger.Kind].Lerp(Colors.White, 0.35f));
            list.SetItemTooltip(index, trigger.Comment.Length > 0 ? trigger.Shape + "\n" + trigger.Comment : trigger.Shape);
            if (selected == trigger.Key)
            {
                list.Select(index);
            }
        }
    }

    /// <summary>What the orientation does for this shape, for the inspector.</summary>
    static string OrientationLine(AreaTriggerRecord t) => t.Volume switch
    {
        AreaTriggerShape.Sphere or AreaTriggerShape.Cylinder or AreaTriggerShape.Disk => "Orientation is unused for this shape.",
        AreaTriggerShape.Polygon => t.IsSpawn ? FormattableString.Invariant($"Orientation {t.Orientation:0.###} rad turns the corners") : "Orientation is unused for polygons.",
        AreaTriggerShape.BoundedPlane => FormattableString.Invariant($"Orientation {t.Orientation:0.###} rad ({Mathf.RadToDeg((float)t.Orientation):0.#}°); the plane stands across it"),
        _ => FormattableString.Invariant($"Orientation {t.Orientation:0.###} rad ({Mathf.RadToDeg((float)t.Orientation):0.#}°); length runs along it")
    };

    /// <summary>Refresh the selection card from the snapshot row.</summary>
    void ShowSelection()
    {
        if (Selected is not { } t)
        {
            title.Text = "Select an AreaTrigger";
            identity.Text = "Click a volume or choose one above";
            details.Text = message;
            return;
        }

        title.Text = DisplayName(t);
        identity.Text = $"Map {t.Map}   /   {Summary(t)}";
        List<string> lines =
        [
            t.Shape,
            FormattableString.Invariant($"Center X {t.X:F3}   Y {t.Y:F3}   Z {t.Z:F3}"),
            OrientationLine(t),
            ""
        ];
        if (t.IsSpawn)
        {
            lines.Add($"Server spawn (areatrigger){(t.Comment.Length > 0 ? " · " + t.Comment : "")}");
            lines.Add($"  Template {t.Entry}{(t.IsCustom ? " (custom)" : "")} · create properties {t.CreatePropertiesId}");
            lines.Add("  Script: " + (t.ScriptName.Length == 0 ? "none" : t.ScriptName == AreaTriggerRecord.SmartEntity ? $"SmartAreaTriggerAI (SmartAI, {t.SmartRows} rule rows)" : t.ScriptName + " (C++)"));
            if (t.ScriptName != AreaTriggerRecord.SmartEntity && t.SmartRows > 0)
            {
                lines.Add($"  {t.SmartRows} SmartAI rows exist but do not run without ScriptName SmartAreaTriggerAI.");
            }
        }
        else
        {
            lines.Add("Server links");
            lines.Add("  Script: " + (t.ScriptName.Length == 0 ? "none" : t.ScriptName == AreaTriggerRecord.SmartTrigger ? $"SmartTrigger (SmartAI, {t.SmartRows} rule rows)" : t.ScriptName + " (C++)"));
            if (t.ScriptName != AreaTriggerRecord.SmartTrigger && t.SmartRows > 0)
            {
                lines.Add($"  {t.SmartRows} SmartAI rows exist but do not run without ScriptName SmartTrigger.");
            }
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
        InspectorUi.SelectRow(list, t.Key.ListId);
    }

    void UpdateActions()
    {
        fly.Disabled = !selected.HasValue;
        copyGo.Disabled = !selected.HasValue;
        // C++ scripts take priority over the SmartAI script (one ScriptName per trigger), but the
        // editor still opens so the user can see that and switch deliberately.
        behavior.Disabled = !selected.HasValue;
    }
}
