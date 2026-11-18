using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACWorldEdit.Foundation.Scripting;

/// <summary>
/// One `.acwscript` document. Property names are the on-disk JSON shape and stay stable; unknown
/// fields are kept in <see cref="Extra"/> so a newer writer's data survives a round trip.
/// </summary>
public sealed class ScriptDocument
{
    public int Format { get; set; } = ScriptDocumentJson.CurrentFormat;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Host { get; set; } = "ale-azerothcore";
    public string RuntimeMode { get; set; } = "compatibility";
    public List<ScriptVariable> Variables { get; set; } = [];
    public List<ScriptFunction> Functions { get; set; } = [];
    public ScriptGraph Graph { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public static ScriptDocument Create(string name) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name
    };
}

/// <summary>The nodes, edges and comments of one event graph or user-function graph.</summary>
public sealed class ScriptGraph
{
    public List<ScriptNode> Nodes { get; set; } = [];
    public List<ScriptEdge> Edges { get; set; } = [];
    public List<ScriptComment> Comments { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>A resizable titled comment box behind the nodes.</summary>
public sealed class ScriptComment
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public ScriptPosition Position { get; set; } = new();
    public ScriptSize Size { get; set; } = new();
    public int Color { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ScriptPosition
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class ScriptSize
{
    public double X { get; set; }
    public double Y { get; set; }
}
