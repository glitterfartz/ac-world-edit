using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>
/// Names for IDs shown in readable text (spells, creatures, objects, quests, items, creature_text).
/// Null means unknown; validation treats a non-zero unknown reference as missing.
/// </summary>
public interface IContentNames
{
    string? Spell(uint id);
    string? Creature(uint id);
    string? GameObject(uint id);
    string? Quest(uint id);
    string? Item(uint id);
    /// <summary>
    /// First creature_text line of a group for a creature entry.
    /// </summary>
    string? Text(uint entry, uint group);
    bool CreatureSpawnExists(uint guid);
    string? Emote(uint id);
    string? TextEmote(uint id);
    string? FactionTemplate(uint id);
    /// <summary>
    /// Faction.dbc (reputation factions, as in quest rewards).
    /// </summary>
    string? Faction(uint id);
}

/// <summary>
/// Which script a rule belongs to: a creature entry (all spawns), one spawn (negative GUID), an
/// AreaTrigger (source_type 2) or a timed action list (source_type 9) started by "Run action list".
/// </summary>
public readonly record struct SmartScope(int EntryOrGuid, uint SourceType)
{
    public const uint Creature = 0, AreaTrigger = 2, ActionList = 9;
    public bool IsSpawn => SourceType == Creature && EntryOrGuid < 0;
    public bool IsAreaTrigger => SourceType == AreaTrigger;
    public bool IsActionList => SourceType == ActionList;
    public string Describe(string creature) => IsActionList ? $"Action list {EntryOrGuid}" : IsAreaTrigger ? $"AreaTrigger {EntryOrGuid}" : IsSpawn ? $"This spawn only (#{-EntryOrGuid})" : $"All {creature} spawns";
}

/// <summary>
/// smart_scripts row with named accessors. Parameters are 1-based to match the catalog sentences.
/// </summary>
public sealed class SmartRule(Row row)
{
    public Row Row => row;
    public int EntryOrGuid => row.I("entryorguid");
    public uint SourceType => row.U("source_type");
    public uint Id => row.U("id");
    public uint Link => row.U("link");
    public uint Event => row.U("event_type");
    public uint Phases => row.U("event_phase_mask");
    public uint Chance => row.U("event_chance");
    public uint Flags => row.U("event_flags");
    public uint Action => row.U("action_type");
    public uint Target => row.U("target_type");
    public string Comment => row.T("comment");
    public uint EventParam(int i) => row.U($"event_param{i}");
    public uint ActionParam(int i) => row.U($"action_param{i}");
    public uint TargetParam(int i) => row.U($"target_param{i}");
    public float X => row.F("target_x");
    public float Y => row.F("target_y");
    public float Z => row.F("target_z");
    public float O => row.F("target_o");
    public SmartScope Scope => new(EntryOrGuid, SourceType);
    public string Key => InteractionTables.SmartScripts.KeyOf(row);
}

/// <summary>
/// SmartAI editing helpers: catalog lookups, readable sentences, linked chains, new rows and the
/// server loader's rules (SmartScriptMgr.cpp) as errors/warnings.
/// </summary>
public static partial class SmartCatalog
{
    /// <summary>
    /// smart_scripts event_flags bits shown as checkboxes (SmartEventFlags).
    /// </summary>
    public static readonly (uint Bit, string Name)[] EventFlags =
    [
        (0x1, "Only once"), (0x2, "Normal (10)"), (0x4, "Heroic (25)"), (0x8, "Heroic 10"), (0x10, "Heroic 25"), (0x80, "Debug builds only"), (0x100, "Keep after reset"), (0x200, "Also while charmed")
    ];

    public const uint AllEventFlags = 0x3FF, AllPhases = 0xFFF;
    public const uint LinkEvent = 61, RunActionList = 80, Talk = 1;
    public const uint AreaTriggerEvent = 46, SelfTarget = 1;

    /// <summary>
    /// Events the loader accepts for an AreaTrigger script: On trigger and Then (SmartAIEventMask,
    /// SmartScriptMgr.h; checked in SmartAIMgr::IsEventValid).
    /// </summary>
    public static bool AllowedForAreaTrigger(uint eventType) => eventType is AreaTriggerEvent or LinkEvent;

    /// <summary>
    /// The script name that makes an AreaTrigger's rules run, with a fix row when it is missing.
    /// Another name means a C++ script owns the trigger, so its SmartAI rows are ignored; that is a
    /// deliberate choice and gets a warning only (one ScriptName per trigger).
    /// </summary>
    public static IEnumerable<InteractionIssue> CheckAreaTriggerScript(int trigger, Row? script, bool hasRules)
    {
        string name = script?.T("ScriptName") ?? "";
        if (!hasRules || name == AreaTriggerRecord.SmartTrigger)
        {
            yield break;
        }

        if (name.Length == 0)
        {
            yield return new(false, "The AreaTrigger has no script, so these rules never run.", [EnableSmartTrigger(trigger, null)]);
        }
        else
        {
            yield return new(false, $"The AreaTrigger runs the C++ script {name}, so these rules never run.", [EnableSmartTrigger(trigger, script)]);
        }
    }

    public static RowEdit EnableSmartTrigger(int trigger, Row? script)
    {
        string key = trigger.ToString(CultureInfo.InvariantCulture);
        return new(InteractionTables.AreaTriggerScripts.Name, key, script, script?.With("ScriptName", AreaTriggerRecord.SmartTrigger) ?? Row.Of(("entry", trigger), ("ScriptName", AreaTriggerRecord.SmartTrigger)));
    }

    // Built lazily: the generated arrays live in the other partial file, and static initializer
    // order across partial files is undefined.
    static Dictionary<int, SmartType>? events, actions, targets;

    // Range markers are not types: looking one up returns null, so validation reports it unknown.
    public static SmartType? Event(uint id) => (events ??= Events.Where(t => !IsMarker(t)).ToDictionary(t => t.Id)).GetValueOrDefault((int)id);
    public static SmartType? Action(uint id) => (actions ??= Actions.Where(t => !IsMarker(t)).ToDictionary(t => t.Id)).GetValueOrDefault((int)id);
    public static SmartType? Target(uint id) => (targets ??= Targets.Where(t => !IsMarker(t)).ToDictionary(t => t.Id)).GetValueOrDefault((int)id);

    /// <summary>
    /// A complete smart_scripts row with every whitelisted column, defaults for a new rule.
    /// </summary>
    public static Row NewRow(SmartScope scope, uint id, uint eventType, uint action, uint target, string comment = "")
    {
        var values = new List<(string, object)>
        {
            ("entryorguid", scope.EntryOrGuid), ("source_type", scope.SourceType), ("id", id), ("link", 0u), ("event_type", eventType),
            ("event_phase_mask", 0u), ("event_chance", 100u), ("event_flags", 0u), ("action_type", action), ("target_type", target),
            ("target_x", 0f), ("target_y", 0f), ("target_z", 0f), ("target_o", 0f), ("comment", comment)
        };
        for (int i = 1; i <= 6; i++)
        {
            values.Add(($"event_param{i}", 0u));
            values.Add(($"action_param{i}", 0u));
        }

        for (int i = 1; i <= 4; i++)
        {
            values.Add(($"target_param{i}", 0u));
        }

        return Row.Of(values.ToArray());
    }

    public static string FormatValue(SmartParamKind kind, uint value, IContentNames names, uint entry = 0)
    {
        string Named(string? name, string fallback) => name != null ? name : fallback;
        return kind switch
        {
            SmartParamKind.Ms => value == 0 ? "0 s" : value % 1000 == 0 ? $"{value / 1000} s" : value >= 1000 ? (value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " s" : $"{value} ms",
            SmartParamKind.Percent => $"{value}%",
            SmartParamKind.Yards => $"{value} yd",
            SmartParamKind.Bool => value != 0 ? "yes" : "no",
            SmartParamKind.Flags => $"0x{value:X}",
            SmartParamKind.Spell => value == 0 ? "any spell" : Named(names.Spell(value), $"spell {value}"),
            SmartParamKind.Creature => value == 0 ? "any creature" : Named(names.Creature(value), $"creature {value}"),
            SmartParamKind.GameObject => value == 0 ? "any object" : Named(names.GameObject(value), $"object {value}"),
            SmartParamKind.Quest => value == 0 ? "any quest" : Named(names.Quest(value), $"quest {value}"),
            SmartParamKind.Item => Named(names.Item(value), $"item {value}"),
            SmartParamKind.TextGroup => names.Text(entry, value) is { } line ? $"\"{line}\"" : $"text group {value}",
            SmartParamKind.ActionList => $"#{value}",
            SmartParamKind.Emote => value == 0 ? "none" : Named(names.Emote(value), $"emote {value}"),
            SmartParamKind.TextEmote => value == 0 ? "any emote" : Named(names.TextEmote(value), $"emote {value}"),
            SmartParamKind.FactionTemplate => value == 0 ? "its default faction" : Named(names.FactionTemplate(value), $"faction {value}"),
            _ => value.ToString(CultureInfo.InvariantCulture)
        };
    }

    static string Fill(SmartType? type, Func<int, uint> param, IContentNames names, uint entry, SmartRule? rule = null)
    {
        if (type == null)
        {
            return "(unknown)";
        }

        // Uncurated types: the name plus the first parameter, so the key value is still visible.
        string text = type.Sentence.Length > 0 ? type.Sentence : type.Name.ToLowerInvariant() + (type.Params.Length > 0 && param(1) != 0 ? " {1}" : "");
        for (int i = 1; i <= 6; i++)
        {
            var kind = i <= type.Params.Length ? type.Params[i - 1].Kind : SmartParamKind.Number;
            text = text.Replace("{" + i + "}", FormatValue(kind, param(i), names, entry));
        }

        if (rule != null)
        {
            text = text.Replace("{x}", rule.X.ToString("0.##", CultureInfo.InvariantCulture)).Replace("{y}", rule.Y.ToString("0.##", CultureInfo.InvariantCulture)).Replace("{z}", rule.Z.ToString("0.##", CultureInfo.InvariantCulture));
        }

        return text;
    }

    /// <summary>
    /// One-line description: "When it enters combat: cast Fireball on its victim (30% chance)".
    /// Linked rows read "Then: …". Unknown types show their IDs so nothing is hidden.
    /// </summary>
    public static string Describe(SmartRule rule, IContentNames names, uint entry)
    {
        var e = Event(rule.Event);
        var a = Action(rule.Action);
        var t = Target(rule.Target);
        string when = rule.Event == LinkEvent ? "Then" : e == null ? $"On event {rule.Event}" : Timer(rule) ?? Capitalize(Fill(e, rule.EventParam, names, entry));
        string what = a == null ? $"action {rule.Action}" : Fill(a, rule.ActionParam, names, entry);
        string target = t == null ? $"target type {rule.Target}" : Fill(t, i => i <= 4 ? rule.TargetParam(i) : 0, names, entry, rule);
        string on = target.Length == 0 || rule.Target == 1 && (what.Contains("itself", StringComparison.Ordinal) || SelfImplied.Contains(rule.Action)) ? "" : $" → {target}";
        string extras = (rule.Chance is > 0 and < 100 ? $" ({rule.Chance}% chance)" : "") + ((rule.Flags & 1) != 0 ? " (once)" : "") + (rule.Phases != 0 ? $" (phases {PhaseList(rule.Phases)})" : "");
        return $"{when}: {what}{on}{extras}";
    }

    /// <summary>
    /// Actions that always act on the creature itself in practice (talk, emotes, phases, action
    /// lists, despawn, evade), where "→ itself" would only add noise.
    /// </summary>
    static readonly HashSet<uint> SelfImplied = [0, Talk, 5, 17, 22, 23, 24, 41, RunActionList];

    static string Range(uint min, uint max) => min == max ? FormatValue(SmartParamKind.Ms, min, NoNames.Instance) : $"{FormatValue(SmartParamKind.Ms, min, NoNames.Instance)}–{FormatValue(SmartParamKind.Ms, max, NoNames.Instance)}";

    /// <summary>
    /// Timer events (0 in combat, 1 out of combat, 60 always) read naturally: "Every 8–12 s in
    /// combat (first after 3 s)" or "Once, after 5 s"; in action lists the timer is the delay after
    /// the previous step: "After 2 s".
    /// </summary>
    static string? Timer(SmartRule rule)
    {
        if (rule.Event is not (0 or 1 or 60))
        {
            return null;
        }

        bool noFirst = rule.EventParam(1) == 0 && rule.EventParam(2) == 0;
        string first = Range(rule.EventParam(1), rule.EventParam(2));
        if (rule.Scope.IsActionList)
        {
            return noFirst ? "Right away" : $"After {first}";
        }

        string when = rule.Event == 0 ? " in combat" : rule.Event == 1 ? " out of combat" : "";
        if (rule.EventParam(3) == 0 && rule.EventParam(4) == 0)
        {
            return noFirst ? $"Once, right away{when}" : $"Once, after {first}{when}";
        }

        return $"Every {Range(rule.EventParam(3), rule.EventParam(4))}{when}" + (noFirst ? "" : $" (first after {first})");
    }

    /// <summary>
    /// Name source that knows nothing; for values that never need names (timers).
    /// </summary>
    sealed class NoNames : IContentNames
    {
        public static readonly NoNames Instance = new();
        public string? Spell(uint id) => null;
        public string? Creature(uint id) => null;
        public string? GameObject(uint id) => null;
        public string? Quest(uint id) => null;
        public string? Item(uint id) => null;
        public string? Text(uint entry, uint group) => null;
        public bool CreatureSpawnExists(uint guid) => false;
        public string? Emote(uint id) => null;
        public string? TextEmote(uint id) => null;
        public string? FactionTemplate(uint id) => null;
        public string? Faction(uint id) => null;
    }

    static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    public static string PhaseList(uint mask) => string.Join(",", Enumerable.Range(1, 12).Where(p => (mask & (1u << (p - 1))) != 0));

    /// <summary>
    /// Server-style comment "Name - On event - Do action" used when the comment is left empty.
    /// </summary>
    public static string AutoComment(SmartRule rule, string creature) => $"{creature} - {(rule.Event == LinkEvent ? "Linked" : Event(rule.Event)?.Name ?? $"Event {rule.Event}")} - {Action(rule.Action)?.Name ?? $"Action {rule.Action}"}";

    /// <summary>
    /// Rules grouped into chains: each root (not a linked "then" row) followed by the rows it links
    /// to, in id order. Orphan linked rows (never reached) are listed as their own chains.
    /// </summary>
    public static List<List<SmartRule>> Chains(IEnumerable<SmartRule> rules)
    {
        var byId = rules.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        var linked = rules.Where(r => r.Link != 0).Select(r => r.Link).ToHashSet();
        var used = new HashSet<uint>();
        var chains = new List<List<SmartRule>>();
        foreach (var root in rules.OrderBy(r => r.Id).Where(r => !(r.Event == LinkEvent && linked.Contains(r.Id))))
        {
            var chain = new List<SmartRule>();
            for (var current = root; current != null && used.Add(current.Id); current = current.Link != 0 ? byId.GetValueOrDefault(current.Link) : null)
            {
                chain.Add(current);
            }

            if (chain.Count > 0)
            {
                chains.Add(chain);
            }
        }

        return chains;
    }

    /// <summary>
    /// Loader rules for one scope's rules. Errors are what the server skips; warnings are what it
    /// loads but never runs. Fix rows enable SmartAI on the creature when needed.
    /// </summary>
    public static IEnumerable<InteractionIssue> Check(SmartScope scope, IReadOnlyList<SmartRule> rules, IContentNames names, Row? creatureAi, uint entry)
    {
        var ids = rules.Select(r => r.Id).ToHashSet();
        var linkedTo = rules.Where(r => r.Link != 0).Select(r => r.Link).ToHashSet();
        foreach (var rule in rules)
        {
            string where = $"Rule {rule.Id}";
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

            if (rule.Link != 0)
            {
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

            if (scope.IsAreaTrigger)
            {
                if (!AllowedForAreaTrigger(rule.Event))
                {
                    yield return new(true, $"{where}: AreaTrigger scripts only accept \"On trigger\" and \"Then\" events; the server skips this rule.");
                }
                else if (rule.Event == AreaTriggerEvent && rule.EventParam(1) != 0 && rule.EventParam(1) != (uint)scope.EntryOrGuid)
                {
                    yield return new(false, $"{where} only fires for AreaTrigger {rule.EventParam(1)}, not this one ({scope.EntryOrGuid}); use 0 or {scope.EntryOrGuid}.");
                }

                // An AreaTrigger script has no owner object: Self selects nothing (SmartScript
                // GetTargets with a null base object), Talk needs a creature target to speak, and
                // Run action list starts the list on its SmartAI creature/GameObject target.
                if (rule.Target == SelfTarget)
                {
                    yield return new(false, $"{where} targets Self, but an AreaTrigger has no object of its own; pick the invoker or a creature.");
                }
                else if (rule.Action == Talk && Target(rule.Target)?.Code is { } targetCode && !targetCode.Contains("CREATURE", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new(false, $"{where}: Talk from an AreaTrigger only speaks when the target is a creature.");
                }
                else if (rule.Action == RunActionList && Target(rule.Target)?.Code is { } listTarget && !listTarget.Contains("CREATURE", StringComparison.OrdinalIgnoreCase) && !listTarget.Contains("GAMEOBJECT", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new(false, $"{where}: an AreaTrigger runs action lists on a SmartAI creature or GameObject target, not the player.");
                }
            }

            if (rule.Event == LinkEvent && !linkedTo.Contains(rule.Id) && !scope.IsActionList)
            {
                yield return new(false, $"{where} is a \"Then\" rule that no other rule leads to; it never runs.");
            }

            foreach (var (type, param, label) in new[] { (Event(rule.Event), (Func<int, uint>)rule.EventParam, "event"), (Action(rule.Action), rule.ActionParam, "action"), (Target(rule.Target), i => i <= 4 ? rule.TargetParam(i) : 0, "target") })
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

                    bool missing = type.Params[i - 1].Kind switch
                    {
                        SmartParamKind.Spell => names.Spell(value) == null,
                        SmartParamKind.Creature => names.Creature(value) == null,
                        SmartParamKind.GameObject => names.GameObject(value) == null,
                        SmartParamKind.Quest => names.Quest(value) == null,
                        SmartParamKind.Item => names.Item(value) == null,
                        _ => false
                    };
                    if (missing)
                    {
                        yield return new(true, $"{where}: {label} {type.Params[i - 1].Label.ToLowerInvariant()} {value} does not exist.");
                    }
                }
            }
        }

        if (scope.IsSpawn && !names.CreatureSpawnExists((uint)-scope.EntryOrGuid))
        {
            yield return new(true, $"Spawn #{-scope.EntryOrGuid} does not exist; the server skips this script.");
        }

        if (!scope.IsActionList && !scope.IsAreaTrigger && rules.Count > 0 && creatureAi != null && creatureAi.T("AIName") != "SmartAI")
        {
            string current = creatureAi.T("AIName");
            yield return new(false, $"The creature's AI is {(current.Length == 0 ? "the default" : current)}, so these rules never run.", [new RowEdit(InteractionTables.CreatureAI.Name, creatureAi["entry"], creatureAi, creatureAi.With("AIName", "SmartAI"))]);
        }
    }
}
