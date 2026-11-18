using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACWorldEdit.Foundation.Scripting;

/// <summary>A user function: parameters, declared return slots and its own graph.</summary>
public sealed class ScriptFunction
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<ScriptParameter> Parameters { get; set; } = [];
    public List<ScriptReturn> Returns { get; set; } = [];
    public ScriptGraph Graph { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ScriptParameter
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ScriptValueType Type { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ScriptReturn
{
    public string Name { get; set; } = "";
    public ScriptValueType Type { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
