using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>How a condition parameter (a ConditionValue column, or a source key column) is edited and shown.</summary>
public enum ConditionParamKind
{
    Number,
    Bool,
    Flags,
    Spell,
    Item,
    Quest,
    Creature,
    GameObject,
    Achievement,
    Faction,
    Zone,
    Area,
    Map,
    GameEvent,
    Title,
    Skill,
    Comparison
}

public sealed record ConditionParam(string Label, ConditionParamKind Kind);

/// <summary>Whether the server still accepts a condition type or source type.</summary>
public enum ConditionStatus
{
    Usable,
    /// <summary>Kept in the enum for old rows; the server rejects or ignores new ones.</summary>
    Deprecated,
    /// <summary>The server's own comment says not to use it for this client (for example terrain swaps on 3.3.5a).</summary>
    NotForClient
}

/// <summary>
/// One condition type (<c>ConditionTypeOrReference</c>). Sentence uses {1}..{3} for ConditionValue1..3 and is
/// the positive form; <see cref="ConditionCatalog.Describe"/> adds the negation. Help is the server comment.
/// </summary>
public sealed record ConditionType(int Id, string Code, string Name, string Sentence, string Help, ConditionParam[] Params, ConditionStatus Status = ConditionStatus.Usable);

/// <summary>
/// One condition source type (<c>SourceTypeOrReferenceId</c>). The labels say what SourceGroup, SourceEntry
/// and SourceId mean for it; an empty label means the column is not used and must be 0.
/// </summary>
public sealed record ConditionSource(int Id, string Code, string Name, string GroupLabel, string EntryLabel, string IdLabel, ConditionStatus Status = ConditionStatus.Usable);

/// <summary>
/// The condition types and source types of one dialect, from the server's ConditionMgr.h. Ids are sparse
/// (AzerothCore's own types start at 101; TrinityCore leaves source 20 unused), so lookups go by id.
/// </summary>
public sealed class ConditionCatalog
{
    public static readonly ConditionCatalog Ac = new(AcConditionTypes.Types, AcConditionTypes.Sources);
    public static readonly ConditionCatalog Trinity = new(TcConditionTypes.Types, TcConditionTypes.Sources);

    readonly Dictionary<int, ConditionType> types;
    readonly Dictionary<int, ConditionSource> sources;

    public ConditionCatalog(IReadOnlyList<ConditionType> types, IReadOnlyList<ConditionSource> sources)
    {
        Types = types;
        Sources = sources;
        this.types = types.ToDictionary(t => t.Id);
        this.sources = sources.ToDictionary(s => s.Id);
    }

    public IReadOnlyList<ConditionType> Types { get; }
    public IReadOnlyList<ConditionSource> Sources { get; }

    public ConditionType? Type(int id) => types.GetValueOrDefault(id);
    public ConditionSource? Source(int id) => sources.GetValueOrDefault(id);

    /// <summary>
    /// A condition as one sentence, such as "the target has aura 1459 (effect 0)" or "not: the target is in zone 12".
    /// <paramref name="negative"/> is the row's NegativeCondition. An unknown type reads as its raw id.
    /// </summary>
    public string Describe(int type, long value1, long value2, long value3, bool negative = false)
    {
        string text = Type(type) is { } info
            ? (info.Id == 0 ? "" : "the target ") + Fill(info.Sentence, value1, value2, value3)
            : $"condition type {type.ToString(CultureInfo.InvariantCulture)} ({value1}, {value2}, {value3})";
        return negative ? $"not: {text}" : text;
    }

    static string Fill(string sentence, long v1, long v2, long v3) => sentence
        .Replace("{1}", v1.ToString(CultureInfo.InvariantCulture))
        .Replace("{2}", v2.ToString(CultureInfo.InvariantCulture))
        .Replace("{3}", v3.ToString(CultureInfo.InvariantCulture));
}
