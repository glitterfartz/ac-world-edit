using System.Text.Json;
using ACWorldEdit.Foundation.Scripting;
using Xunit;

/// <summary>
/// The `.acwscript` document round-trips through canonical JSON, preserves unknown fields and
/// unknown node/pin snapshots, and refuses a format it does not understand.
/// </summary>
public class ScriptDocumentTests
{
    static ScriptDocument Sample()
    {
        var document = ScriptDocument.Create("Deadmines Boss");
        document.Description = "Boss death demo";
        document.Variables.Add(new ScriptVariable
        {
            Id = "v1",
            Name = "phase",
            Type = ScriptValueType.Int,
            Default = JsonSerializer.SerializeToElement(0)
        });
        document.Functions.Add(new ScriptFunction
        {
            Id = "f1",
            Name = "SpawnWave",
            Parameters = [new ScriptParameter { Id = "p1", Name = "wave", Type = ScriptValueType.Int }],
            Returns = [new ScriptReturn { Name = "ok", Type = ScriptValueType.Bool }],
            Graph = new ScriptGraph()
        });
        document.Graph.Nodes.Add(new ScriptNode
        {
            Id = "n1",
            Kind = "event.creature.on_died",
            Position = new ScriptPosition { X = 10, Y = 20 },
            Properties = { ["entry"] = JsonSerializer.SerializeToElement(1234), ["shots"] = JsonSerializer.SerializeToElement(0) },
            Pins =
            [
                new ScriptPin { Id = "exec", Direction = ScriptPinDirection.Out, Type = ScriptValueType.Exec },
                new ScriptPin { Id = "creature", Direction = ScriptPinDirection.Out, Type = ScriptValueType.Creature },
                new ScriptPin { Id = "killer", Direction = ScriptPinDirection.Out, Type = ScriptValueType.Unit, Nullable = true }
            ]
        });
        document.Graph.Nodes.Add(new ScriptNode
        {
            Id = "n2",
            Kind = "unit.cast_spell",
            Position = new ScriptPosition { X = 320, Y = 0 },
            Pins =
            [
                new ScriptPin { Id = "exec", Direction = ScriptPinDirection.In, Type = ScriptValueType.Exec },
                new ScriptPin { Id = "self", Direction = ScriptPinDirection.In, Type = ScriptValueType.Unit },
                new ScriptPin { Id = "spell", Direction = ScriptPinDirection.In, Type = ScriptValueType.SpellId }
            ],
            Literals = { ["spell"] = JsonSerializer.SerializeToElement(133) }
        });
        document.Graph.Edges.Add(new ScriptEdge
        {
            Id = "e1",
            From = new ScriptPinRef { Node = "n1", Pin = "exec" },
            To = new ScriptPinRef { Node = "n2", Pin = "exec" }
        });
        document.Graph.Comments.Add(new ScriptComment
        {
            Id = "c1",
            Text = "Boss death",
            Position = new ScriptPosition { X = 0, Y = -80 },
            Size = new ScriptSize { X = 320, Y = 120 },
            Color = 0
        });
        return document;
    }

    [Fact]
    public void RoundTripPreservesTheDocument()
    {
        var document = Sample();
        var loaded = ScriptDocumentJson.Load(ScriptDocumentJson.Save(document));

        Assert.Equal(1, loaded.Format);
        Assert.Equal(document.Id, loaded.Id);
        Assert.Equal("Deadmines Boss", loaded.Name);
        Assert.Equal("Boss death demo", loaded.Description);
        Assert.Equal("ale-azerothcore", loaded.Host);
        Assert.Equal("compatibility", loaded.RuntimeMode);

        Assert.Single(loaded.Variables);
        Assert.Equal("phase", loaded.Variables[0].Name);
        Assert.Equal(ScriptValueType.Int, loaded.Variables[0].Type);
        Assert.Equal(0, loaded.Variables[0].Default!.Value.GetInt32());

        Assert.Single(loaded.Functions);
        Assert.Equal("SpawnWave", loaded.Functions[0].Name);
        Assert.Equal("wave", loaded.Functions[0].Parameters[0].Name);
        Assert.Equal(ScriptValueType.Bool, loaded.Functions[0].Returns[0].Type);

        Assert.Equal(2, loaded.Graph.Nodes.Count);
        var eventNode = loaded.Graph.Nodes.Single(n => n.Id == "n1");
        Assert.Equal("event.creature.on_died", eventNode.Kind);
        Assert.Equal(1234, eventNode.Properties["entry"].GetInt32());
        Assert.Equal(3, eventNode.Pins.Count);
        Assert.True(eventNode.Pins.Single(p => p.Id == "killer").Nullable);
        Assert.Equal(ScriptValueType.SpellId, loaded.Graph.Nodes.Single(n => n.Id == "n2").Pins.Single(p => p.Id == "spell").Type);

        Assert.Single(loaded.Graph.Edges);
        Assert.Equal("n1", loaded.Graph.Edges[0].From.Node);
        Assert.Equal("n2", loaded.Graph.Edges[0].To.Node);
        Assert.Single(loaded.Graph.Comments);
        Assert.Equal("Boss death", loaded.Graph.Comments[0].Text);
    }

    [Fact]
    public void CanonicalHashSurvivesSaveAndReload()
    {
        var document = Sample();
        string hash = ScriptDocumentJson.CanonicalHash(document);

        var loaded = ScriptDocumentJson.Load(ScriptDocumentJson.Save(document));
        Assert.Equal(hash, ScriptDocumentJson.CanonicalHash(loaded));

        loaded.Graph.Nodes[0].Kind = "event.creature.on_spawn";
        Assert.NotEqual(hash, ScriptDocumentJson.CanonicalHash(loaded));
    }

    [Fact]
    public void CanonicalJsonSortsObjectKeys()
    {
        string canonical = ScriptDocumentJson.Canonical(Sample());
        Assert.StartsWith("{\"description\":", canonical);
        Assert.DoesNotContain(": ", canonical);
        Assert.DoesNotContain("\n", canonical);
    }

    [Fact]
    public void UnknownFieldsNodesAndPinsArePreserved()
    {
        const string json = """
        {
          "format": 1,
          "id": "doc1",
          "name": "Future",
          "host": "ale-azerothcore",
          "runtimeMode": "compatibility",
          "futureTop": { "x": 1 },
          "variables": [],
          "functions": [],
          "graph": {
            "nodes": [
              {
                "id": "n1",
                "kind": "future.node",
                "position": { "x": 1, "y": 2 },
                "properties": { "custom": 7 },
                "pins": [ { "id": "p1", "direction": "out", "type": "int", "futurePin": true } ],
                "futureNode": [1, 2]
              }
            ],
            "edges": [],
            "comments": []
          }
        }
        """;

        var loaded = ScriptDocumentJson.Load(json);
        Assert.Equal("future.node", loaded.Graph.Nodes[0].Kind);
        Assert.Equal(7, loaded.Graph.Nodes[0].Properties["custom"].GetInt32());
        Assert.Equal(ScriptValueType.Int, loaded.Graph.Nodes[0].Pins[0].Type);

        var saved = ScriptDocumentJson.Save(loaded);
        using var savedJson = JsonDocument.Parse(saved);
        Assert.Equal(1, savedJson.RootElement.GetProperty("futureTop").GetProperty("x").GetInt32());
        var node = savedJson.RootElement.GetProperty("graph").GetProperty("nodes")[0];
        Assert.Equal(2, node.GetProperty("futureNode")[1].GetInt32());
        Assert.True(node.GetProperty("pins")[0].GetProperty("futurePin").GetBoolean());
    }

    [Fact]
    public void NewerFormatsAreRejectedWithoutRewriting()
    {
        const string json = """{ "format": 2, "id": "doc1", "graph": { "nodes": [], "edges": [], "comments": [] } }""";
        var error = Assert.Throws<ScriptFormatException>(() => ScriptDocumentJson.Load(json));
        Assert.Contains("format 2", error.Message);
        Assert.Contains("version 1", error.Message);
    }

    [Fact]
    public void MissingFormatLoadsAsVersionOne()
    {
        const string json = """{ "id": "doc1", "name": "N", "graph": { "nodes": [], "edges": [], "comments": [] } }""";
        var loaded = ScriptDocumentJson.Load(json);
        Assert.Equal(1, loaded.Format);
        Assert.Equal("doc1", loaded.Id);
    }

    [Fact]
    public void CreateGivesAStableUniqueId()
    {
        var first = ScriptDocument.Create("One");
        var second = ScriptDocument.Create("One");
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(32, first.Id.Length);
        Assert.Equal("compatibility", first.RuntimeMode);
    }
}
