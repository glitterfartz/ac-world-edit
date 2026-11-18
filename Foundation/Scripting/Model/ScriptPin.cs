using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACWorldEdit.Foundation.Scripting;

public enum ScriptPinDirection
{
    In,
    Out
}

/// <summary>A semantic pin: an id, a direction and a type snapshot. Edges reference the id.</summary>
public sealed class ScriptPin
{
    public string Id { get; set; } = "";
    public ScriptPinDirection Direction { get; set; }
    public ScriptValueType Type { get; set; }
    public bool Nullable { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
