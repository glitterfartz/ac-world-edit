namespace ACWorldEdit.Foundation;

/// <summary>How one Data field of a GameObject type is read and edited.</summary>
public enum GameObjectFieldKind
{
    /// <summary>A plain whole number.</summary>
    Number,
    /// <summary>Zero or one; drawn as a check box.</summary>
    Bool,
    /// <summary>One of the named values in <see cref="GameObjectField.Options"/>.</summary>
    Choice,
    /// <summary>A whole number of milliseconds.</summary>
    Milliseconds,
    /// <summary>A whole number that names a row in another table or client file.</summary>
    Reference
}

/// <summary>
/// One Data field of one GameObject type. The index is the physical Data column; the
/// physical signedness and text length still come from the bound <see cref="TableSchema"/>.
/// Scale is only for fields the server stores scaled (AzerothCore's autoCloseTime stores
/// seconds times 65536); every other field uses 1.
/// </summary>
public sealed record GameObjectField(
    int Index,
    string Code,
    string Label,
    string Tip,
    GameObjectFieldKind Kind,
    int Min = int.MinValue,
    int Max = 0,
    int Default = 0,
    (int Value, string Name)[]? Options = null,
    string? Reference = null,
    double Scale = 1,
    bool Unused = false)
{
    /// <summary>True when the field has a meaningful upper bound (0 means unbounded).</summary>
    public bool HasMax => Max > 0;

    /// <summary>The field as the page shows it, including a unit when one is known.</summary>
    public string Display(string raw)
    {
        if (Kind == GameObjectFieldKind.Milliseconds)
        {
            return raw + " ms";
        }

        return raw;
    }
}

/// <summary>One `gameobject_template.type`: its name and the Data fields it reads.</summary>
public sealed record GameObjectType(
    int Id,
    string Code,
    string Name,
    string Hint,
    GameObjectField[] Fields)
{
    public GameObjectField? Field(int index) => Fields.FirstOrDefault(f => f.Index == index);

    /// <summary>The fields the page shows unless the user asks for unused ones.</summary>
    public IEnumerable<GameObjectField> Used => Fields.Where(f => !f.Unused);

    public IEnumerable<GameObjectField> UnusedFields => Fields.Where(f => f.Unused);
}

/// <summary>
/// The per-dialect GameObject type catalogue. The generated arrays are the source of truth;
/// this class only indexes them and supplies the fallbacks for an unknown or newer type.
/// </summary>
public sealed class GameObjectTypeCatalog
{
    readonly Dictionary<int, GameObjectType> byId;

    GameObjectTypeCatalog(WorldDialect dialect, GameObjectType[] types)
    {
        Dialect = dialect;
        Types = types.OrderBy(t => t.Id).ToArray();
        byId = Types.ToDictionary(t => t.Id);
    }

    public WorldDialect Dialect { get; }

    public IReadOnlyList<GameObjectType> Types { get; }

    /// <summary>The highest type with a catalogue entry (35 on AzerothCore, 66 on TrinityCore).</summary>
    public int MaxType => Types.Count == 0 ? -1 : Types[^1].Id;

    public static GameObjectTypeCatalog Ac { get; } = new(WorldDialect.AzerothCore, AcGameObjectTypes.Types);

    public static GameObjectTypeCatalog Trinity { get; } = new(WorldDialect.TrinityCore, TcGameObjectTypes.Types);

    public static GameObjectTypeCatalog For(WorldDialect dialect) => dialect == WorldDialect.TrinityCore ? Trinity : Ac;

    public GameObjectType? Find(int type) => byId.GetValueOrDefault(type);

    /// <summary>The type's name, or "Type N" for a type this server does not know.</summary>
    public string Name(int type) => Find(type)?.Name ?? $"Type {type}";
}
