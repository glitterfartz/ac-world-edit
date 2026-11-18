using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACWorldEdit.Foundation.Scripting;

/// <summary>A pin-to-pin connection; both ends are semantic pin ids, never GraphEdit slot indices.</summary>
public sealed class ScriptEdge
{
    public string Id { get; set; } = "";
    public ScriptPinRef From { get; set; } = new();
    public ScriptPinRef To { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ScriptPinRef
{
    public string Node { get; set; } = "";
    public string Pin { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
