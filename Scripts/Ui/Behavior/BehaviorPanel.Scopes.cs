using System.Globalization;
using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>The scope picker and the AI banner: which rule set is shown and whether it runs.</summary>
public partial class BehaviorPanel
{
    /// <summary>
    /// The creature's scopes: entry, spawn (when opened from one), then every action list reachable
    /// from them through "Run action list", in discovery order.
    /// </summary>
    List<SmartScope> Scopes(RowSet rows)
    {
        var list = new List<SmartScope> { EntryScope };
        if (guid != 0)
        {
            list.Add(SpawnScope);
        }

        for (int i = 0; i < list.Count && list.Count < 64; i++)
        {
            foreach (var rule in RulesOf(rows, list[i]).Where(r => r.Action == SmartCatalog.RunActionList && r.ActionParam(1) != 0))
            {
                var next = new SmartScope((int)rule.ActionParam(1), SmartScope.ActionList);
                if (!list.Contains(next))
                {
                    list.Add(next);
                }
            }
        }

        if (scope is { IsActionList: true } shown && !list.Contains(shown))
        {
            list.Add(shown);
        }

        return list;
    }

    void ScopeButton(string text, SmartScope? value)
    {
        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            ButtonPressed = scope == value
        };
        button.Pressed += () => ShowScope(value);
        scopes.AddChild(button);
    }

    /// <summary>
    /// Which brain the creature uses, in words, with a one-click switch to SmartAI.
    /// </summary>
    void RenderAiBanner()
    {
        if (trigger is { } at)
        {
            string name = RunningScript(effective), wanted = SmartCatalog.SmartScriptName(at);
            string runs = name == wanted ? ""
                : name.Length == 0 ? $" · no script: the rules below do not run until {wanted} is set" : $" · C++ script {name}: the rules below are ignored";
            subtitle.Text = (at.IsEntity ? $"Spawned AreaTrigger {at.SpawnId} · template {entry}{(at.SourceType == SmartScope.AreaTriggerEntityCustom ? " (custom)" : "")}" : $"AreaTrigger {entry}") + runs;
            return;
        }

        string ai = CreatureAi(effective)?.T("AIName") ?? "";
        string warning = snapshot!.ScriptName.Length > 0 ? $" · C++ script {snapshot.ScriptName} (SmartAI rules below are ignored by the server)"
            : ai == "SmartAI" ? "" : ai.Length == 0 ? " · default AI: rules below do not run until SmartAI is enabled" : $" · {ai}: rules below do not run";
        subtitle.Text = $"Entry {entry}{(guid != 0 ? $" · spawn #{guid}" : "")}{warning}";
    }
}
