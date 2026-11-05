using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>
/// One dialect's SmartAI: its event, action and target types and the smart_scripts columns it has,
/// with catalog lookups, readable sentences, linked chains, new rows and the server loader's rules
/// (SmartScriptMgr.cpp) as errors/warnings. <see cref="Ac"/> and <see cref="Trinity"/> are the
/// instances; the ID ranges differ, so nothing is shared by ID.
/// </summary>
public sealed partial class SmartCatalog
{
    /// <summary>The 3.3.5 AzerothCore catalog (event_param1-6, action_param1-6).</summary>
    public static readonly SmartCatalog Ac = new(WorldDialect.AzerothCore, AcSmartTypes.Events, AcSmartTypes.Actions, AcSmartTypes.Targets, 6, 6, AcTables.Instance, AcTables.SmartScripts, AcTables.CreatureText, AcTables.CreatureAI, AcTables.AreaTriggerScripts, null,
        [(0x1, "Only once"), (0x2, "Normal (10)"), (0x4, "Heroic (25)"), (0x8, "Heroic 10"), (0x10, "Heroic 25"), (0x80, "Debug builds only"), (0x100, "Keep after reset"), (0x200, "Also while charmed")], 0);

    /// <summary>
    /// The TrinityCore master catalog (event_param1-5, action_param1-7, string parameters and a
    /// Difficulties list instead of the difficulty flags).
    /// </summary>
    public static readonly SmartCatalog Trinity = new(WorldDialect.TrinityCore, TcSmartTypes.Events, TcSmartTypes.Actions, TcSmartTypes.Targets, 5, 7, TcTables.Instance, TcTables.SmartScripts, TcTables.CreatureText, TcTables.CreatureAI, TcTables.AreaTriggerScripts, TcTables.AreaTriggerAI,
        [(0x1, "Only once"), (0x20, "Action list waits for it"), (0x80, "Debug builds only"), (0x100, "Keep after reset"), (0x200, "Also while charmed")], 0x1E);

    public static SmartCatalog For(WorldDialect dialect) => dialect switch
    {
        WorldDialect.AzerothCore => Ac,
        WorldDialect.TrinityCore => Trinity,
        _ => throw new InvalidDataException($"The {dialect} world database is not supported yet.")
    };

    readonly SmartType[] events, actions, targets;
    readonly Dictionary<int, SmartType> eventById, actionById, targetById;

    SmartCatalog(WorldDialect dialect, SmartType[] events, SmartType[] actions, SmartType[] targets, int eventParams, int actionParams, ITableCatalog tables, TableSchema scripts, TableSchema text, TableSchema ai, TableSchema triggerScripts, TableSchema? triggerAi, (uint Bit, string Name)[] eventFlags, uint deprecatedFlags)
    {
        Dialect = dialect;
        Tables = tables;
        Scripts = scripts;
        CreatureText = text;
        CreatureAi = ai;
        AreaTriggerScripts = triggerScripts;
        AreaTriggerAi = triggerAi;
        this.events = events;
        this.actions = actions;
        this.targets = targets;
        EventParamCount = eventParams;
        ActionParamCount = actionParams;
        EventFlags = eventFlags;
        DeprecatedEventFlags = deprecatedFlags;
        eventById = events.Where(t => !IsMarker(t)).ToDictionary(t => t.Id);
        actionById = actions.Where(t => !IsMarker(t)).ToDictionary(t => t.Id);
        targetById = targets.Where(t => !IsMarker(t)).ToDictionary(t => t.Id);
    }

    public WorldDialect Dialect { get; }

    /// <summary>The dialect's table catalog and the three tables the behavior editor edits.</summary>
    public ITableCatalog Tables { get; }
    public TableSchema Scripts { get; }
    public TableSchema CreatureText { get; }

    /// <summary>The creature's AIName, which must be SmartAI for the rules to run.</summary>
    public TableSchema CreatureAi { get; }

    /// <summary>areatrigger_scripts: the script a client AreaTrigger runs (SmartTrigger for its rules).</summary>
    public TableSchema AreaTriggerScripts { get; }

    /// <summary>A spawned AreaTrigger's ScriptName (TrinityCore `areatrigger`), null where spawns do not exist.</summary>
    public TableSchema? AreaTriggerAi { get; }

    /// <summary>Every type of the server's enums, including range markers and removed slots (see <see cref="IsMarker"/>).</summary>
    public IReadOnlyList<SmartType> Events => events;
    public IReadOnlyList<SmartType> Actions => actions;
    public IReadOnlyList<SmartType> Targets => targets;

    /// <summary>Numbered parameter columns an event (event_paramN) or action (action_paramN) has; targets have four.</summary>
    public int EventParamCount { get; }
    public int ActionParamCount { get; }
    public const int TargetParamCount = 4;

    /// <summary>True when smart_scripts has the string parameter columns and the Difficulties list.</summary>
    public bool HasStringParams => Dialect == WorldDialect.TrinityCore;

    /// <summary>
    /// smart_scripts event_flags bits shown as checkboxes (SmartEventFlags).
    /// </summary>
    public (uint Bit, string Name)[] EventFlags { get; }

    /// <summary>Flag bits the server still reads but only warns about (TrinityCore's per-difficulty flags, replaced by Difficulties).</summary>
    public uint DeprecatedEventFlags { get; }

    public const uint AllEventFlags = 0x3FF, AllPhases = 0xFFF;
    public const uint LinkEvent = 61, RunActionList = 80, Talk = 1;
    public const uint AreaTriggerEvent = 46, SelfTarget = 1;

    /// <summary>TrinityCore's "Area trigger left" (SMART_EVENT_AREATRIGGER_EXIT); AzerothCore has no such event.</summary>
    public const uint AreaTriggerExitEvent = 88;

    /// <summary>
    /// Events the loader accepts for an AreaTrigger script (SmartAIEventMask, SmartScriptMgr.h; checked
    /// in SmartAIMgr::IsEventValid). AzerothCore: On trigger and Then. TrinityCore client triggers add
    /// "Area trigger left"; its spawned triggers also run timed updates (UPDATE_OOC 1, UPDATE 60).
    /// </summary>
    public bool AllowedForAreaTrigger(SmartScope scope, uint eventType) => Dialect == WorldDialect.TrinityCore
        ? eventType is AreaTriggerEvent or AreaTriggerExitEvent or LinkEvent || (scope.IsTriggerEntity && eventType is 1 or 60)
        : eventType is AreaTriggerEvent or LinkEvent;

    /// <summary>The script name that makes this trigger's rules run.</summary>
    public static string SmartScriptName(AreaTriggerOwner trigger) => trigger.IsEntity ? AreaTriggerRecord.SmartEntity : AreaTriggerRecord.SmartTrigger;

    /// <summary>
    /// The script name that makes an AreaTrigger's rules run, with a fix row when it is missing. A client
    /// trigger's name is its areatrigger_scripts row; a spawn's is its own `areatrigger.ScriptName`, or
    /// its create properties' (<paramref name="fallback"/>) when that is empty (AreaTrigger::GetScriptId).
    /// Another name means a C++ script owns the trigger, so its SmartAI rows are ignored; that is a
    /// deliberate choice and gets a warning only (one ScriptName per trigger).
    /// </summary>
    public IEnumerable<InteractionIssue> CheckAreaTriggerScript(AreaTriggerOwner trigger, Row? script, string fallback, bool hasRules)
    {
        string own = script?.T("ScriptName") ?? "";
        string name = own.Length > 0 ? own : fallback;
        string wanted = SmartScriptName(trigger);
        if (!hasRules || name == wanted)
        {
            yield break;
        }

        // A spawn's row is always read with it; without one there is nothing to switch.
        RowEdit[]? fix = trigger.IsEntity && script == null ? null : [EnableTriggerScript(trigger, script)];
        string what = trigger.IsEntity ? "The spawned AreaTrigger" : "The AreaTrigger";
        yield return name.Length == 0
            ? new(false, $"{what} has no script, so these rules never run.", fix)
            : new(false, $"{what} runs the C++ script {name}, so these rules never run.", fix);
    }

    /// <summary>The row edit that sets the trigger's (or spawn's) script to the SmartAI one.</summary>
    public RowEdit EnableTriggerScript(AreaTriggerOwner trigger, Row? script)
    {
        string wanted = SmartScriptName(trigger);
        if (trigger.IsEntity)
        {
            var table = AreaTriggerAi ?? throw new InvalidDataException("This database has no spawned AreaTriggers.");
            string spawn = trigger.SpawnId.ToString(CultureInfo.InvariantCulture);
            return new(table.Name, spawn, script, (script ?? Row.Of(("SpawnId", trigger.SpawnId), ("ScriptName", ""))).With("ScriptName", wanted));
        }

        string key = trigger.Id.ToString(CultureInfo.InvariantCulture);
        return new(AreaTriggerScripts.Name, key, script, script?.With("ScriptName", wanted) ?? Row.Of(("entry", (int)trigger.Id), ("ScriptName", wanted)));
    }

    // Range markers and removed slots are not types: looking one up returns null, so validation
    // reports it unknown.
    public SmartType? Event(uint id) => eventById.GetValueOrDefault((int)id);
    public SmartType? Action(uint id) => actionById.GetValueOrDefault((int)id);
    public SmartType? Target(uint id) => targetById.GetValueOrDefault((int)id);

    /// <summary>
    /// A complete smart_scripts row with every whitelisted column, defaults for a new rule.
    /// </summary>
    public Row NewRow(SmartScope scope, uint id, uint eventType, uint action, uint target, string comment = "")
    {
        var values = new List<(string, object)>
        {
            ("entryorguid", scope.EntryOrGuid), ("source_type", scope.SourceType), ("id", id), ("link", 0u), ("event_type", eventType),
            ("event_phase_mask", 0u), ("event_chance", 100u), ("event_flags", 0u), ("action_type", action), ("target_type", target),
            ("target_x", 0f), ("target_y", 0f), ("target_z", 0f), ("target_o", 0f), ("comment", comment)
        };
        for (int i = 1; i <= EventParamCount; i++)
        {
            values.Add(($"event_param{i}", 0u));
        }

        for (int i = 1; i <= ActionParamCount; i++)
        {
            values.Add(($"action_param{i}", 0u));
        }

        for (int i = 1; i <= TargetParamCount; i++)
        {
            values.Add(($"target_param{i}", 0u));
        }

        if (HasStringParams)
        {
            values.AddRange([("Difficulties", ""), ("event_param_string", ""), ("action_param_string", ""), ("target_param_string", "")]);
        }

        return Row.Of(values.ToArray());
    }

    /// <summary>
    /// Loader rules for one scope's rules. Errors are what the server skips; warnings are what it
    /// loads but never runs. Fix rows enable SmartAI on the creature when needed.
    /// </summary>
    public IEnumerable<InteractionIssue> Check(SmartScope scope, IReadOnlyList<SmartRule> rules, IContentNames names, Row? creatureAi, uint entry)
    {
        var ids = rules.Select(r => r.Id).ToHashSet();
        var linkedTo = rules.Where(r => r.Link != 0).Select(r => r.Link).ToHashSet();
        foreach (var rule in rules)
        {
            string where = $"Rule {rule.Id}";
            foreach (var issue in CheckTypes(rule, where))
            {
                yield return issue;
            }

            foreach (var issue in CheckRanges(rule, where))
            {
                yield return issue;
            }

            foreach (var issue in CheckLink(rule, where, ids, rules))
            {
                yield return issue;
            }

            foreach (var issue in CheckAreaTrigger(rule, where, scope))
            {
                yield return issue;
            }

            foreach (var issue in CheckOrphanLink(rule, where, linkedTo, scope))
            {
                yield return issue;
            }

            foreach (var issue in CheckNamedParameters(rule, where, names))
            {
                yield return issue;
            }
        }

        foreach (var issue in CheckScope(scope, rules, names, creatureAi))
        {
            yield return issue;
        }
    }

    /// <summary>The event, action and target types must be in this server's enums.</summary>
    IEnumerable<InteractionIssue> CheckTypes(SmartRule rule, string where)
    {
        if (Event(rule.Event) == null)
        {
            yield return new(true, $"{where}: event type {rule.Event} is unknown to this server.");
        }

        if (Action(rule.Action) == null)
        {
            yield return new(true, $"{where}: action type {rule.Action} is unknown to this server.");
        }

        if (Target(rule.Target) == null)
        {
            yield return new(true, $"{where}: target type {rule.Target} is unknown to this server.");
        }
    }

    /// <summary>Chance, phase mask and event flags (including flags the server now clears).</summary>
    IEnumerable<InteractionIssue> CheckRanges(SmartRule rule, string where)
    {
        if (rule.Chance > 100)
        {
            yield return new(true, $"{where}: chance must be 0-100%.");
        }

        if (rule.Phases > AllPhases)
        {
            yield return new(true, $"{where}: only phases 1-12 exist.");
        }

        if ((rule.Flags & ~AllEventFlags) != 0)
        {
            yield return new(true, $"{where}: unknown event flags 0x{rule.Flags & ~AllEventFlags:X}.");
        }
        else if ((rule.Flags & DeprecatedEventFlags) != 0)
        {
            yield return new(false, $"{where}: the difficulty flags 0x{rule.Flags & DeprecatedEventFlags:X} are no longer read; the server clears them. Use the Difficulties list.");
        }
    }

    /// <summary>A linked rule must exist, be a "Then" row and not link to itself.</summary>
    static IEnumerable<InteractionIssue> CheckLink(SmartRule rule, string where, HashSet<uint> ids, IReadOnlyList<SmartRule> rules)
    {
        if (rule.Link == 0)
        {
            yield break;
        }

        if (rule.Link == rule.Id)
        {
            yield return new(true, $"{where} links to itself (infinite loop).");
        }
        else if (!ids.Contains(rule.Link))
        {
            yield return new(true, $"{where} links to rule {rule.Link}, which does not exist.");
        }
        else if (rules.First(r => r.Id == rule.Link).Event != LinkEvent)
        {
            yield return new(true, $"{where} links to rule {rule.Link}, which is not a \"Then (linked)\" rule.");
        }
    }

    /// <summary>The loader's extra rules for a client or spawned AreaTrigger script.</summary>
    IEnumerable<InteractionIssue> CheckAreaTrigger(SmartRule rule, string where, SmartScope scope)
    {
        if (!scope.IsAreaTrigger)
        {
            yield break;
        }

        if (!AllowedForAreaTrigger(scope, rule.Event))
        {
            string accepted = string.Join(", ", events.Where(e => !IsMarker(e) && AllowedForAreaTrigger(scope, (uint)e.Id)).Select(e => $"\"{e.Name}\""));
            yield return new(true, $"{where}: this AreaTrigger script only accepts {accepted} events; the server skips this rule.");
        }
        else if (rule.Event == AreaTriggerEvent && Event(AreaTriggerEvent)?.Params.Length > 0 && rule.EventParam(1) != 0 && rule.EventParam(1) != (uint)scope.EntryOrGuid)
        {
            // AzerothCore's On trigger names the trigger it fires for; TrinityCore's has no parameter.
            yield return new(false, $"{where} only fires for AreaTrigger {rule.EventParam(1)}, not this one ({scope.EntryOrGuid}); use 0 or {scope.EntryOrGuid}.");
        }

        // A client AreaTrigger's script has no owner object: Self selects nothing (SmartScript
        // GetTargets with a null base object), Talk needs a creature target to speak, and
        // Run action list starts the list on its SmartAI creature/GameObject target. A spawned
        // trigger is an object (SmartAreaTriggerAI), so Self and its own action lists work.
        if (rule.Target == SelfTarget && !scope.IsTriggerEntity)
        {
            yield return new(false, $"{where} targets Self, but an AreaTrigger has no object of its own; pick the invoker or a creature.");
        }
        else if (rule.Action == Talk && Target(rule.Target)?.Code is { } targetCode && !targetCode.Contains("CREATURE", StringComparison.OrdinalIgnoreCase))
        {
            yield return new(false, $"{where}: Talk from an AreaTrigger only speaks when the target is a creature.");
        }
        else if (!scope.IsTriggerEntity && rule.Action == RunActionList && Target(rule.Target)?.Code is { } listTarget && !listTarget.Contains("CREATURE", StringComparison.OrdinalIgnoreCase) && !listTarget.Contains("GAMEOBJECT", StringComparison.OrdinalIgnoreCase))
        {
            yield return new(false, $"{where}: an AreaTrigger runs action lists on a SmartAI creature or GameObject target, not the player.");
        }
    }

    /// <summary>A "Then" rule nothing leads to never runs (action lists are entered at any step).</summary>
    static IEnumerable<InteractionIssue> CheckOrphanLink(SmartRule rule, string where, HashSet<uint> linkedTo, SmartScope scope)
    {
        if (rule.Event == LinkEvent && !linkedTo.Contains(rule.Id) && !scope.IsActionList)
        {
            yield return new(false, $"{where} is a \"Then\" rule that no other rule leads to; it never runs.");
        }
    }

    /// <summary>Named parameters (spell, creature, object, quest, item) must resolve.</summary>
    IEnumerable<InteractionIssue> CheckNamedParameters(SmartRule rule, string where, IContentNames names)
    {
        foreach (var (type, param, label) in new[] { (Event(rule.Event), (Func<int, uint>)rule.EventParam, "event"), (Action(rule.Action), rule.ActionParam, "action"), (Target(rule.Target), i => i <= TargetParamCount ? rule.TargetParam(i) : 0, "target") })
        {
            if (type == null)
            {
                continue;
            }

            for (int i = 1; i <= type.Params.Length; i++)
            {
                uint value = param(i);
                if (value == 0)
                {
                    continue;
                }

                var kind = type.Params[i - 1].Kind;
                if (ContentNamesExtensions.IsNamedReference(kind) && !names.TryName(kind, value, out _))
                {
                    yield return new(true, $"{where}: {label} {type.Params[i - 1].Label.ToLowerInvariant()} {value} does not exist.");
                }
            }
        }
    }

    /// <summary>The script's spawn must exist, and the creature's AIName must be SmartAI for the rules to run.</summary>
    IEnumerable<InteractionIssue> CheckScope(SmartScope scope, IReadOnlyList<SmartRule> rules, IContentNames names, Row? creatureAi)
    {
        if (scope.IsSpawn && !names.CreatureSpawnExists((uint)-scope.EntryOrGuid))
        {
            yield return new(true, $"Spawn #{-scope.EntryOrGuid} does not exist; the server skips this script.");
        }

        if (!scope.IsActionList && !scope.IsAreaTrigger && rules.Count > 0 && creatureAi != null && creatureAi.T("AIName") != "SmartAI")
        {
            string current = creatureAi.T("AIName");
            yield return new(false, $"The creature's AI is {(current.Length == 0 ? "the default" : current)}, so these rules never run.", [new RowEdit(CreatureAi.Name, creatureAi["entry"], creatureAi, creatureAi.With("AIName", "SmartAI"))]);
        }
    }
}
