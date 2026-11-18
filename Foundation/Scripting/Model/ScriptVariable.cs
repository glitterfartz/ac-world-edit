using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACWorldEdit.Foundation.Scripting;

/// <summary>
/// A module-scope variable. Defaults are literals only, so a variable can never depend on
/// event-time state; object userdata is rejected because it is invalid after the handler returns.
/// </summary>
public sealed class ScriptVariable
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ScriptValueType Type { get; set; }
    public JsonElement? Default { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
