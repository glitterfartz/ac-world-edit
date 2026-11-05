using System.Globalization;

namespace ACWorldEdit.Foundation;

// Sentences, chains and the auto-comment. Kept apart from the catalog's types, lookups and loader
// rules so the formatting can be read without the validation beside it.
public sealed partial class SmartCatalog
{
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

    string Fill(SmartType? type, Func<int, uint> param, IContentNames names, uint entry, SmartRule? rule = null)
    {
        if (type == null)
        {
            return "(unknown)";
        }

        // Uncurated types: the name plus the first parameter, so the key value is still visible.
        string text = type.Sentence.Length > 0 ? type.Sentence : type.Name.ToLowerInvariant() + (type.Params.Length > 0 && param(1) != 0 ? " {1}" : "");
        for (int i = 1; i <= 7; i++)
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
    public string Describe(SmartRule rule, IContentNames names, uint entry)
    {
        var e = Event(rule.Event);
        var a = Action(rule.Action);
        var t = Target(rule.Target);
        string when = rule.Event == LinkEvent ? "Then" : e == null ? $"On event {rule.Event}" : Timer(rule) ?? Capitalize(Fill(e, rule.EventParam, names, entry));
        string what = a == null ? $"action {rule.Action}" : Fill(a, rule.ActionParam, names, entry);
        string target = t == null ? $"target type {rule.Target}" : Fill(t, i => i <= TargetParamCount ? rule.TargetParam(i) : 0, names, entry, rule);
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
        public bool CreatureSpawnExists(ulong guid) => false;
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
    public string AutoComment(SmartRule rule, string creature) => $"{creature} - {(rule.Event == LinkEvent ? "Linked" : Event(rule.Event)?.Name ?? $"Event {rule.Event}")} - {Action(rule.Action)?.Name ?? $"Action {rule.Action}"}";

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
}
