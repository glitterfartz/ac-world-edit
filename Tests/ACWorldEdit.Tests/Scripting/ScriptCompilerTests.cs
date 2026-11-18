using System.Text.Json;
using ACWorldEdit.Foundation.Scripting;
using Xunit;

/// <summary>Compiler behaviour beyond the template goldens: determinism, defaults, variables, escaping.</summary>
public class ScriptCompilerTests
{
    static readonly ScriptCompiler Compiler = new(AcScriptHosts.Profile);

    static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);

    static ScriptDocument Document(params ScriptNode[] nodes)
    {
        var document = ScriptDocument.Create("Test");
        document.Graph.Nodes.AddRange(nodes);
        return document;
    }

    static ScriptNode Event(string id = "n1") => new()
    {
        Id = id,
        Kind = "event.creature.on_died",
        Properties = { ["entry"] = Json(1234) }
    };

    static ScriptEdge Edge(string id, string fromNode, string fromPin, string toNode, string toPin) => new()
    {
        Id = id,
        From = new ScriptPinRef { Node = fromNode, Pin = fromPin },
        To = new ScriptPinRef { Node = toNode, Pin = toPin }
    };

    [Fact]
    public void CompilingTheSameDocumentTwiceIsByteIdentical()
    {
        var document = ScriptTemplateLibrary.Load("creature-death");
        var first = Compiler.Compile(document, "creature-death.acwscript");
        var second = Compiler.Compile(document, "creature-death.acwscript");

        Assert.True(first.Success);
        Assert.Equal(first.LuaText, second.LuaText);
        Assert.Equal(first.SourceHash, second.SourceHash);
        Assert.Equal(first.CatalogHash, second.CatalogHash);
    }

    [Fact]
    public void ErrorsBlockLuaText()
    {
        var result = Compiler.Compile(Document(new ScriptNode { Id = "n1", Kind = "future.node" }));

        Assert.False(result.Success);
        Assert.Null(result.LuaText);
        Assert.Contains(result.Errors, error => error.Code == ScriptDiagnosticCodes.UnknownNode);
    }

    [Fact]
    public void OmittedOptionalArgumentsUseTheHostDefault()
    {
        var document = Document(Event(), new ScriptNode
        {
            Id = "n2",
            Kind = "unit.cast_spell",
            Literals = { ["spell"] = Json(133) }
        });
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n2", "exec"));
        document.Graph.Edges.Add(Edge("e2", "n1", "creature", "n2", "self"));
        document.Graph.Edges.Add(Edge("e3", "n1", "creature", "n2", "target"));

        var result = Compiler.Compile(document);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => $"{error.Code} {error.Message}")));
        Assert.Contains("creature:CastSpell(creature, 133, false)", result.LuaText);
    }

    [Fact]
    public void StringLiteralsAreEscaped()
    {
        var document = Document(Event(), new ScriptNode
        {
            Id = "n2",
            Kind = "unit.send_unit_say",
            Literals = { ["message"] = Json("He said \"hi\"\nnext"), ["language"] = Json(0) }
        });
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n2", "exec"));
        document.Graph.Edges.Add(Edge("e2", "n1", "creature", "n2", "self"));

        var result = Compiler.Compile(document);

        Assert.True(result.Success);
        Assert.Contains("He said \\\"hi\\\"\\nnext", result.LuaText);
    }

    [Fact]
    public void ModuleVariablesBecomeTheVarsTable()
    {
        var document = Document();
        document.Variables.Add(new ScriptVariable
        {
            Id = "v1",
            Name = "phase",
            Type = ScriptValueType.Int,
            Default = Json(0)
        });

        var result = Compiler.Compile(document);

        Assert.True(result.Success);
        Assert.Contains("local __vars = {\n    phase = 0\n}", result.LuaText);
        Assert.Contains("local __fns = {}", result.LuaText);
    }

    [Fact]
    public void OutputNameUsesTheSlugAndShortId()
    {
        var document = ScriptDocument.Create("Creature On Death");
        document.Id = "abcdef0123456789abcdef0123456789";
        Assert.Equal("acw_creature-on-death_abcdef01.lua", ScriptCompiler.OutputName(document));
    }
}
