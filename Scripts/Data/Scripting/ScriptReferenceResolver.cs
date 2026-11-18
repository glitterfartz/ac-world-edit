using System.Globalization;
using ACWorldEdit.Foundation;
using ACWorldEdit.Foundation.Scripting;

namespace ACWorldEdit.Data.Scripting;

/// <summary>The row kinds a script can reference by id and a live database can confirm.</summary>
public enum ScriptReferenceKind
{
    Creature,
    GameObject,
    Item,
    Quest,
    Spell
}

/// <summary>One live lookup: true/false when the database answered, null when it was not consulted.</summary>
public sealed record ScriptReferenceLookup(bool? Exists, string? Name = null);

public interface IScriptReferenceSource
{
    Task<ScriptReferenceLookup> LookupAsync(
        ScriptReferenceKind kind, long id, CancellationToken token = default);
}

/// <summary>One reference found in a document; a confirmed miss blocks export.</summary>
public sealed record ScriptReferenceIssue(
    string Code, ScriptReferenceKind Kind, long Id, string Message, bool BlocksExport);

/// <summary>
/// Confirms the typed ids in a document (event scope values and literals) against a live database.
/// A source that answers "missing" produces a blocking <c>SC0601</c>; no source, or a lookup that
/// could not answer, leaves the value unchecked.
/// </summary>
public sealed class ScriptReferenceResolver
{
    readonly IScriptReferenceSource? source;
    readonly Dictionary<(ScriptReferenceKind Kind, long Id), ScriptReferenceLookup> cache = [];

    public ScriptReferenceResolver(IScriptReferenceSource? source = null) => this.source = source;

    public async Task<IReadOnlyList<ScriptReferenceIssue>> CheckAsync(
        ScriptDocument document, ScriptHostProfile host, CancellationToken token = default)
    {
        if (source is null)
        {
            return [];
        }

        var issues = new List<ScriptReferenceIssue>();
        foreach (var node in document.Graph.Nodes)
        {
            if (host.Event(node.Kind) is { } definition)
            {
                foreach (var scope in definition.ScopeParams)
                {
                    if (KindOf(scope.Type) is not { } scopeKind
                        || !node.Properties.TryGetValue(scope.Name, out var scopeValue)
                        || !TryId(scopeValue, out long scopeId))
                    {
                        continue;
                    }

                    await CheckOne(scopeKind, scopeId, node.Id, issues, token);
                }
            }

            var pins = ScriptPins.Build(document, host, node)
                .GroupBy(pin => pin.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            foreach (var (pinId, literal) in node.Literals)
            {
                if (!pins.TryGetValue(pinId, out var pin)
                    || KindOf(pin.Type) is not { } literalKind
                    || !TryId(literal, out long literalId))
                {
                    continue;
                }

                await CheckOne(literalKind, literalId, node.Id, issues, token);
            }
        }

        return issues;
    }

    async Task CheckOne(
        ScriptReferenceKind kind, long id, string nodeId, List<ScriptReferenceIssue> issues, CancellationToken token)
    {
        if (!cache.TryGetValue((kind, id), out var lookup))
        {
            lookup = await source!.LookupAsync(kind, id, token);
            cache[(kind, id)] = lookup;
        }

        if (lookup.Exists == false)
        {
            issues.Add(new ScriptReferenceIssue(
                ScriptDiagnosticCodes.MissingEntry,
                kind,
                id,
                $"{kind} {id} does not exist in the connected database.",
                BlocksExport: true));
        }
    }

    public static ScriptReferenceKind? KindOf(ScriptValueType type) => type switch
    {
        ScriptValueType.CreatureEntry => ScriptReferenceKind.Creature,
        ScriptValueType.GameObjectEntry => ScriptReferenceKind.GameObject,
        ScriptValueType.ItemEntry => ScriptReferenceKind.Item,
        ScriptValueType.QuestId => ScriptReferenceKind.Quest,
        ScriptValueType.SpellId => ScriptReferenceKind.Spell,
        _ => null
    };

    static bool TryId(System.Text.Json.JsonElement value, out long id)
    {
        if (value.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            return value.TryGetInt64(out id);
        }

        id = 0;
        return false;
    }

    /// <summary>A live source backed by the editor's <see cref="IDBSource"/> search.</summary>
    public sealed class DbSource : IScriptReferenceSource
    {
        readonly Func<SmartParamKind, string, CancellationToken, Task<IReadOnlyList<(uint Id, string Name)>>> search;

        public DbSource(IDBSource database) => search = database.SearchAsync;

        public DbSource(
            Func<SmartParamKind, string, CancellationToken, Task<IReadOnlyList<(uint Id, string Name)>>> search) =>
            this.search = search;

        public async Task<ScriptReferenceLookup> LookupAsync(
            ScriptReferenceKind kind, long id, CancellationToken token = default)
        {
            var searchKind = kind switch
            {
                ScriptReferenceKind.Creature => SmartParamKind.Creature,
                ScriptReferenceKind.GameObject => SmartParamKind.GameObject,
                ScriptReferenceKind.Item => SmartParamKind.Item,
                ScriptReferenceKind.Quest => SmartParamKind.Quest,
                _ => SmartParamKind.Spell
            };

            IReadOnlyList<(uint Id, string Name)> hits;
            try
            {
                hits = await search(searchKind, id.ToString(CultureInfo.InvariantCulture), token);
            }
            catch (Exception error) when (error is NotSupportedException or ArgumentOutOfRangeException)
            {
                // This database cannot answer for the kind (AzerothCore spells live in client data).
                return new ScriptReferenceLookup(null);
            }

            var hit = hits.FirstOrDefault(entry => entry.Id == id);
            return new ScriptReferenceLookup(hit.Id == id, string.IsNullOrWhiteSpace(hit.Name) ? null : hit.Name);
        }
    }
}
