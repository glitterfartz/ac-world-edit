namespace ACWorldEdit.Foundation.Scripting;

/// <summary>One type's canvas identity: GraphEdit port integer, pin colour and base type.</summary>
public sealed record ScriptTypeInfo(
    ScriptValueType Type,
    int PortType,
    string Color,
    ScriptValueType? Base = null,
    bool IsObject = false,
    bool IsValue = false);

/// <summary>
/// The one mapping from semantic types to GraphEdit port integers and colours, plus the
/// assignability and variable-storage rules. The canvas and validator both read it.
/// </summary>
public sealed class ScriptTypeRegistry
{
    public static readonly ScriptTypeRegistry Default = new();

    static readonly ScriptValueType[] TypedIds =
    [
        ScriptValueType.CreatureEntry, ScriptValueType.GameObjectEntry, ScriptValueType.ItemEntry,
        ScriptValueType.SpellId, ScriptValueType.QuestId, ScriptValueType.MapId,
        ScriptValueType.InstanceId, ScriptValueType.PhaseMask, ScriptValueType.FactionId
    ];

    readonly Dictionary<ScriptValueType, ScriptTypeInfo> info;

    ScriptTypeRegistry()
    {
        info = new[]
        {
            new ScriptTypeInfo(ScriptValueType.Exec, 0, "#ffffff"),
            new ScriptTypeInfo(ScriptValueType.Bool, 1, "#d9534f", IsValue: true),
            new ScriptTypeInfo(ScriptValueType.Int, 2, "#5bc0de", IsValue: true),
            new ScriptTypeInfo(ScriptValueType.Float, 3, "#5cb85c", IsValue: true),
            new ScriptTypeInfo(ScriptValueType.String, 4, "#f0ad4e", IsValue: true),

            new ScriptTypeInfo(ScriptValueType.CreatureEntry, 10, "#9b59b6", IsValue: true),
            new ScriptTypeInfo(ScriptValueType.GameObjectEntry, 11, "#8e44ad", IsValue: true),
            new ScriptTypeInfo(ScriptValueType.ItemEntry, 12, "#af7ac5", IsValue: true),
            new ScriptTypeInfo(ScriptValueType.SpellId, 13, "#3498db", IsValue: true),
            new ScriptTypeInfo(ScriptValueType.QuestId, 14, "#2980b9", IsValue: true),
            new ScriptTypeInfo(ScriptValueType.MapId, 15, "#1f618d", IsValue: true),
            new ScriptTypeInfo(ScriptValueType.InstanceId, 16, "#5499c7", IsValue: true),
            new ScriptTypeInfo(ScriptValueType.PhaseMask, 17, "#a9cce3", IsValue: true),
            new ScriptTypeInfo(ScriptValueType.FactionId, 18, "#7fb3d5", IsValue: true),

            new ScriptTypeInfo(ScriptValueType.ObjectGuid, 20, "#00bcd4", IsValue: true),
            new ScriptTypeInfo(ScriptValueType.GuidLow, 21, "#0097a7", IsValue: true),

            new ScriptTypeInfo(ScriptValueType.Object, 30, "#7f8c8d", IsObject: true),
            new ScriptTypeInfo(ScriptValueType.WorldObject, 31, "#95a5a6", ScriptValueType.Object, IsObject: true),
            new ScriptTypeInfo(ScriptValueType.Unit, 32, "#16a085", ScriptValueType.WorldObject, IsObject: true),
            new ScriptTypeInfo(ScriptValueType.Creature, 33, "#27ae60", ScriptValueType.Unit, IsObject: true),
            new ScriptTypeInfo(ScriptValueType.Player, 34, "#2ecc71", ScriptValueType.Unit, IsObject: true),
            new ScriptTypeInfo(ScriptValueType.GameObject, 35, "#e67e22", ScriptValueType.WorldObject, IsObject: true),
            new ScriptTypeInfo(ScriptValueType.Item, 36, "#d35400", ScriptValueType.Object, IsObject: true),
            new ScriptTypeInfo(ScriptValueType.Spell, 37, "#8e44ad", ScriptValueType.Object, IsObject: true),
            new ScriptTypeInfo(ScriptValueType.SpellInfo, 38, "#b39ddb", ScriptValueType.Object, IsObject: true),
            new ScriptTypeInfo(ScriptValueType.Aura, 39, "#9575cd", ScriptValueType.Object, IsObject: true),
            new ScriptTypeInfo(ScriptValueType.Quest, 40, "#f1c40f", ScriptValueType.Object, IsObject: true),
            new ScriptTypeInfo(ScriptValueType.Map, 41, "#34495e", ScriptValueType.Object, IsObject: true),
            new ScriptTypeInfo(ScriptValueType.Group, 42, "#1abc9c", ScriptValueType.Object, IsObject: true),
            new ScriptTypeInfo(ScriptValueType.Guild, 43, "#48c9b0", ScriptValueType.Object, IsObject: true),
            new ScriptTypeInfo(ScriptValueType.WorldPacket, 44, "#e74c3c", ScriptValueType.Object, IsObject: true),
            new ScriptTypeInfo(ScriptValueType.AleQuery, 45, "#c0392b", ScriptValueType.Object, IsObject: true),
            new ScriptTypeInfo(ScriptValueType.ChatHandler, 46, "#e59866", ScriptValueType.Object, IsObject: true),

            new ScriptTypeInfo(ScriptValueType.List, 50, "#ff9800", IsValue: true),
            new ScriptTypeInfo(ScriptValueType.Table, 51, "#ffc107", IsValue: true),
            new ScriptTypeInfo(ScriptValueType.Any, 60, "#bdbdbd")
        }.ToDictionary(item => item.Type);
    }

    public IReadOnlyCollection<ScriptTypeInfo> All => info.Values;

    public ScriptTypeInfo Info(ScriptValueType type) => info[type];

    public int PortType(ScriptValueType type) => Info(type).PortType;

    public string Color(ScriptValueType type) => Info(type).Color;

    public bool IsObject(ScriptValueType type) => Info(type).IsObject;

    public bool IsTypedId(ScriptValueType type) => TypedIds.Contains(type);

    /// <summary>Object userdata must live in a value type (a GUID or an id) before it crosses a handler.</summary>
    public bool CanStoreInVariable(ScriptValueType type) => Info(type).IsValue;

    public bool IsAssignable(ScriptValueType from, ScriptValueType to)
    {
        if (from == to)
        {
            return true;
        }

        if (from == ScriptValueType.Any || to == ScriptValueType.Any)
        {
            return true;
        }

        if (from == ScriptValueType.Int && to == ScriptValueType.Float)
        {
            return true;
        }

        if ((IsTypedId(from) && to == ScriptValueType.Int) || (from == ScriptValueType.Int && IsTypedId(to)))
        {
            return true;
        }

        if (!IsObject(from) || !IsObject(to))
        {
            return false;
        }

        for (var baseType = Info(from).Base; baseType is { } current; baseType = Info(current).Base)
        {
            if (current == to)
            {
                return true;
            }
        }

        return false;
    }
}
