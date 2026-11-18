namespace ACWorldEdit.Foundation.Scripting;

/// <summary>
/// A semantic pin type. Object types follow ALE's userdata hierarchy and are valid only while the
/// handler that produced them runs; typed IDs are numbers with a picker and validation.
/// </summary>
public enum ScriptValueType
{
    Exec,
    Bool,
    Int,
    Float,
    String,
    CreatureEntry,
    GameObjectEntry,
    ItemEntry,
    SpellId,
    QuestId,
    MapId,
    InstanceId,
    PhaseMask,
    FactionId,
    ObjectGuid,
    GuidLow,
    Object,
    WorldObject,
    Unit,
    Creature,
    Player,
    GameObject,
    Item,
    Spell,
    SpellInfo,
    Aura,
    Quest,
    Map,
    Group,
    Guild,
    WorldPacket,
    AleQuery,
    ChatHandler,
    List,
    Table,
    Any
}

/// <summary>The stable JSON spelling for each type: primitives lowercase, everything else by name.</summary>
public static class ScriptValueTypeNames
{
    public static string ToJson(ScriptValueType type) => type switch
    {
        ScriptValueType.Exec => "exec",
        ScriptValueType.Bool => "bool",
        ScriptValueType.Int => "int",
        ScriptValueType.Float => "float",
        ScriptValueType.String => "string",
        ScriptValueType.AleQuery => "ALEQuery",
        _ => type.ToString()
    };

    public static ScriptValueType Parse(string value)
    {
        string text = value.Trim();
        return text.ToLowerInvariant() switch
        {
            "exec" => ScriptValueType.Exec,
            "bool" => ScriptValueType.Bool,
            "int" => ScriptValueType.Int,
            "float" => ScriptValueType.Float,
            "string" => ScriptValueType.String,
            "alequery" => ScriptValueType.AleQuery,
            "objectguid" => ScriptValueType.ObjectGuid,
            "guidlow" => ScriptValueType.GuidLow,
            "list" => ScriptValueType.List,
            "table" => ScriptValueType.Table,
            "any" => ScriptValueType.Any,
            _ when Enum.TryParse<ScriptValueType>(text, ignoreCase: true, out var parsed) => parsed,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown script value type.")
        };
    }
}
