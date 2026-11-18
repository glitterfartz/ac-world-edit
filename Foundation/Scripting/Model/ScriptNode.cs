using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACWorldEdit.Foundation.Scripting;

/// <summary>
/// One graph node. <see cref="Kind"/> is a semantic catalog id. The pin list is a snapshot so a
/// removed or unknown node kind still renders and keeps its edges; the catalog refreshes it when
/// the kind is known. Scope values (<c>entry</c>, <c>shots</c>) live in <see cref="Properties"/>,
/// literal input values in <see cref="Literals"/>.
/// </summary>
public sealed class ScriptNode
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public ScriptPosition Position { get; set; } = new();
    public Dictionary<string, JsonElement> Properties { get; set; } = [];
    public List<ScriptPin> Pins { get; set; } = [];
    public Dictionary<string, JsonElement> Literals { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
