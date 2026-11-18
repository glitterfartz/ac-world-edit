using System.Text.Json;

namespace ACWorldEdit.Foundation.Scripting;

/// <summary>Literal and pin compatibility rules. Assignability itself lives in the type registry.</summary>
public static class ScriptTypeChecker
{
    static readonly ScriptTypeRegistry Types = ScriptTypeRegistry.Default;

    public static bool LiteralMatches(JsonElement literal, ScriptValueType type, bool nullable)
    {
        if (literal.ValueKind == JsonValueKind.Null)
        {
            return nullable || type == ScriptValueType.Any || Types.IsObject(type)
                || type is ScriptValueType.List or ScriptValueType.Table;
        }

        return type switch
        {
            ScriptValueType.Any => true,
            ScriptValueType.Bool => literal.ValueKind is JsonValueKind.True or JsonValueKind.False,
            ScriptValueType.Int or ScriptValueType.ObjectGuid or ScriptValueType.GuidLow =>
                literal.ValueKind == JsonValueKind.Number && literal.TryGetInt64(out _),
            ScriptValueType.Float => literal.ValueKind == JsonValueKind.Number,
            ScriptValueType.String => literal.ValueKind == JsonValueKind.String,
            ScriptValueType.List or ScriptValueType.Table =>
                literal.ValueKind is JsonValueKind.Array or JsonValueKind.Object,
            _ when Types.IsTypedId(type) => literal.ValueKind == JsonValueKind.Number && literal.TryGetInt64(out _),
            _ => false
        };
    }

    public static bool PinAccepts(ScriptPin from, ScriptPin to) => Types.IsAssignable(from.Type, to.Type);

    public static bool IsLargeInteger(JsonElement literal) =>
        literal.ValueKind == JsonValueKind.Number
        && decimal.TryParse(literal.GetRawText(), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var number)
        && Math.Abs(number) > 9_007_199_254_740_992m;

    public static string KindName(JsonElement literal) => literal.ValueKind switch
    {
        JsonValueKind.Null => "nil",
        JsonValueKind.True or JsonValueKind.False => "bool",
        JsonValueKind.Number => "number",
        JsonValueKind.String => "string",
        JsonValueKind.Array => "list",
        JsonValueKind.Object => "table",
        _ => "value"
    };
}
