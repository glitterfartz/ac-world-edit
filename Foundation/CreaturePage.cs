namespace ACWorldEdit.Foundation;

/// <summary>How a field is drawn and edited.</summary>
public enum FieldKind
{
    Text,
    Whole,
    Decimal,
    Choice,
    Flags,
    Check
}

/// <summary>Which row a field edits: the creature template or the difficulty row picked in the page.</summary>
public enum RowBinding
{
    Template,
    Difficulty
}

/// <summary>One named bit of a flag column; an empty list for a column means only the raw value is editable.</summary>
public sealed record FlagBit(ulong Bit, string Name, string Tip = "");

/// <summary>
/// One field of a creature page. Signedness, width and text length come from the bound
/// <see cref="TableSchema"/> column, so the descriptor never restates them. <see cref="Display"/>
/// formats the read-only value (raw column text in, shown text out); <see cref="Raw"/> adds the raw
/// value box under a flag set.
/// </summary>
public sealed record FieldSpec(
    string Group,
    TableSchema Table,
    string Column,
    string Label,
    string Tip,
    FieldKind Kind,
    RowBinding Row = RowBinding.Template,
    (int Value, string Name)[]? Options = null,
    FlagBit[]? Flags = null,
    bool AllowEmpty = false,
    bool Raw = true,
    ulong Max = 0,
    Func<string, string>? Display = null);

/// <summary>A titled section of the page; a group with no fields for a dialect is not drawn.</summary>
public sealed record GroupSpec(string Id, string Title, string? Hint = null);

/// <summary>The per-difficulty rows some fields bind to (TrinityCore's creature_template_difficulty).</summary>
public sealed record DifficultySpec(TableSchema Table, string EntryColumn, string IdColumn, Func<int, string> Name);

/// <summary>
/// The template page of one dialect: the shared, ordered groups and the fields each dialect puts in
/// them. One renderer draws any page, so both dialects keep the same structure.
/// </summary>
public sealed record CreaturePage(GroupSpec[] Groups, FieldSpec[] Fields, DifficultySpec? Difficulty = null)
{
    public IEnumerable<FieldSpec> In(string group) => Fields.Where(f => f.Group == group);

    /// <summary>The groups shared by every dialect, in display order.</summary>
    public static GroupSpec[] StandardGroups(string? movementHint = null, string? flagsHint = null) =>
    [
        new("identity", "Identity"),
        new("interaction", "Interaction"),
        new("combat", "Combat"),
        new("movement", "Movement", movementHint),
        new("loot", "Loot"),
        new("links", "Links and scripting"),
        new("flags", "Flags", flagsHint)
    ];
}
