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
    public void ForRangeEmitsAnInclusiveLuaLoop()
    {
        var document = Document(Event(), new ScriptNode
        {
            Id = "n2",
            Kind = "flow.for_range",
            Literals = { ["from"] = Json(1), ["to"] = Json(3) }
        }, new ScriptNode
        {
            Id = "n3",
            Kind = "unit.set_health"
        });
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n2", "exec"));
        document.Graph.Edges.Add(Edge("e2", "n2", "exec_out", "n3", "exec"));
        document.Graph.Edges.Add(Edge("e3", "n2", "index", "n3", "health"));
        document.Graph.Edges.Add(Edge("e4", "n1", "creature", "n3", "self"));

        var result = Compiler.Compile(document);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains("for i = 1, 3 do", result.LuaText);
        Assert.Contains("creature:SetHealth(i)", result.LuaText);
        Assert.Contains("end", result.LuaText);
    }

    [Fact]
    public void FunctionBodiesLowerParameterGetAndReturn()
    {
        var document = Document();
        document.Functions.Add(new ScriptFunction
        {
            Id = "f1",
            Name = "twice",
            Parameters = [new ScriptParameter { Id = "p1", Name = "value", Type = ScriptValueType.Float }],
            Returns = [new ScriptReturn { Name = "result", Type = ScriptValueType.Float }],
            Graph = new ScriptGraph
            {
                Nodes =
                {
                    new ScriptNode { Id = "n1", Kind = "flow.parameter_get", Properties = { ["parameter"] = Json("p1") } },
                    new ScriptNode { Id = "n2", Kind = "math.multiply", Literals = { ["b"] = Json(2) } },
                    new ScriptNode
                    {
                        Id = "n3",
                        Kind = "flow.return",
                        Pins = [new ScriptPin { Id = "result", Direction = ScriptPinDirection.In, Type = ScriptValueType.Float }]
                    }
                },
                Edges =
                {
                    Edge("e1", "n1", "value", "n2", "a"),
                    Edge("e2", "n2", "result", "n3", "result")
                }
            }
        });

        var result = Compiler.Compile(document);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains("function __fns.twice(value)", result.LuaText);
        Assert.Contains("return value * 2", result.LuaText);
    }

    [Fact]
    public void FunctionExecChainsAreLoweredInOrder()
    {
        var document = Document();
        document.Functions.Add(new ScriptFunction
        {
            Id = "f1",
            Name = "heal",
            Parameters =
            [
                new ScriptParameter { Id = "p1", Name = "unit", Type = ScriptValueType.Unit },
                new ScriptParameter { Id = "p2", Name = "amount", Type = ScriptValueType.Int }
            ],
            Graph = new ScriptGraph
            {
                Nodes =
                {
                    new ScriptNode { Id = "n1", Kind = "flow.parameter_get", Properties = { ["parameter"] = Json("p1") } },
                    new ScriptNode { Id = "n2", Kind = "flow.parameter_get", Properties = { ["parameter"] = Json("p2") } },
                    new ScriptNode { Id = "n3", Kind = "unit.set_health" },
                    new ScriptNode
                    {
                        Id = "n4",
                        Kind = "flow.return",
                        Pins = [new ScriptPin { Id = "exec", Direction = ScriptPinDirection.In, Type = ScriptValueType.Exec }]
                    }
                },
                Edges =
                {
                    Edge("e1", "n1", "value", "n3", "self"),
                    Edge("e2", "n2", "value", "n3", "health"),
                    Edge("e3", "n3", "exec_out", "n4", "exec")
                }
            }
        });

        var result = Compiler.Compile(document);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains("unit:SetHealth(amount)", result.LuaText);
        Assert.Contains("return", result.LuaText);
    }

    [Fact]
    public void TimerOnceEmitsARegistrationAndContinuation()
    {
        var document = Document(Event(), new ScriptNode
        {
            Id = "n2",
            Kind = "timer.once",
            Literals = { ["delay_ms"] = Json(1000) }
        }, new ScriptNode
        {
            Id = "n3",
            Kind = "unit.send_unit_say",
            Literals = { ["message"] = Json("tick"), ["language"] = Json(0) }
        }, new ScriptNode { Id = "n4", Kind = "timer.object" });
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n2", "exec"));
        document.Graph.Edges.Add(Edge("e2", "n1", "creature", "n2", "self"));
        document.Graph.Edges.Add(Edge("e3", "n2", "exec_out", "n3", "exec"));
        document.Graph.Edges.Add(Edge("e4", "n4", "object", "n3", "self"));

        var result = Compiler.Compile(document);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains("creature:RegisterEvent(timer, 1000, 1)", result.LuaText);
        Assert.Contains("timerObject:SendUnitSay(\"tick\"", result.LuaText);
        Assert.Contains("local function timer(eventId, delay, repeats, timerObject)", result.LuaText);
    }

    [Fact]
    public void TimerRepeatUsesTheLiteralRepeats()
    {
        var document = Document(Event(), new ScriptNode
        {
            Id = "n2",
            Kind = "timer.repeat",
            Literals = { ["delay_ms"] = Json(500), ["repeats"] = Json(5) }
        });
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n2", "exec"));
        document.Graph.Edges.Add(Edge("e2", "n1", "creature", "n2", "self"));

        var result = Compiler.Compile(document);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains("creature:RegisterEvent(timer, 500, 5)", result.LuaText);
    }

    [Fact]
    public void MapLookupFeedsInstanceDataSaves()
    {
        var document = Document(new ScriptNode
        {
            Id = "n1",
            Kind = "event.instance.on_initialize",
            Properties = { ["instanceId"] = Json(1) }
        }, new ScriptNode
        {
            Id = "n2",
            Kind = "map.get_by_id",
            Literals = { ["map"] = Json(1) }
        }, new ScriptNode { Id = "n3", Kind = "instance.save" });
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n3", "exec"));
        document.Graph.Edges.Add(Edge("e2", "n2", "result", "n3", "self"));

        var result = Compiler.Compile(document);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains("GetMapById(1)", result.LuaText);
        Assert.Contains(":SaveInstanceData()", result.LuaText);
    }

    [Fact]
    public void AListLoopsWithForEach()
    {
        var document = Document(Event(), new ScriptNode
        {
            Id = "n2",
            Kind = "list.create",
            Literals = { ["a"] = Json("one"), ["b"] = Json("two") }
        }, new ScriptNode
        {
            Id = "n3",
            Kind = "flow.foreach"
        }, new ScriptNode
        {
            Id = "n4",
            Kind = "unit.send_unit_say",
            Literals = { ["language"] = Json(0) }
        });
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n3", "exec"));
        document.Graph.Edges.Add(Edge("e2", "n2", "result", "n3", "list"));
        document.Graph.Edges.Add(Edge("e3", "n3", "exec_out", "n4", "exec"));
        document.Graph.Edges.Add(Edge("e4", "n1", "creature", "n4", "self"));
        document.Graph.Edges.Add(Edge("e5", "n3", "item", "n4", "message"));

        var result = Compiler.Compile(document);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains("for _, item in ipairs({\"one\", \"two\"}) do", result.LuaText);
        Assert.Contains("creature:SendUnitSay(item, 0)", result.LuaText);
    }

    [Fact]
    public void ForwardFunctionCallsResolveTheirNames()
    {
        var document = Document();
        document.Functions.Add(new ScriptFunction
        {
            Id = "f1",
            Name = "first",
            Graph = new ScriptGraph
            {
                Nodes = { new ScriptNode { Id = "n1", Kind = "flow.call_function", Properties = { ["function"] = Json("f2") } } }
            }
        });
        document.Functions.Add(new ScriptFunction { Id = "f2", Name = "second" });

        var result = Compiler.Compile(document);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains("__fns.second()", result.LuaText);
    }

    [Fact]
    public void EventReturnSlotsUseTheirWiredValues()
    {
        var document = Document(new ScriptNode
        {
            Id = "n1",
            Kind = "event.player.on_chat",
            Literals = { ["suppress"] = Json(true), ["newMessage"] = Json("shown") }
        });

        var result = Compiler.Compile(document);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains("return true, \"shown\"", result.LuaText);
    }

    [Fact]
    public void FunctionCallResultsCanFeedExpressions()
    {
        var document = Document(new ScriptNode
        {
            Id = "n1",
            Kind = "event.creature.on_died",
            Properties = { ["entry"] = Json(1234) }
        }, new ScriptNode
        {
            Id = "n2",
            Kind = "flow.call_function",
            Properties = { ["function"] = Json("f1") }
        }, new ScriptNode { Id = "n3", Kind = "flow.branch" });
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n3", "exec"));
        document.Graph.Edges.Add(Edge("e2", "n2", "result", "n3", "condition"));
        document.Functions.Add(new ScriptFunction
        {
            Id = "f1",
            Name = "zero",
            Returns = [new ScriptReturn { Name = "result", Type = ScriptValueType.Bool }],
            Graph = new ScriptGraph
            {
                Nodes =
                {
                    new ScriptNode { Id = "n1", Kind = "flow.equal", Literals = { ["a"] = Json(1), ["b"] = Json(2) } },
                    new ScriptNode
                    {
                        Id = "n2",
                        Kind = "flow.return",
                        Pins = [new ScriptPin { Id = "result", Direction = ScriptPinDirection.In, Type = ScriptValueType.Bool }]
                    }
                },
                Edges = { Edge("e1", "n1", "result", "n2", "result") }
            }
        });

        var result = Compiler.Compile(document);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains("if __fns.zero() then", result.LuaText);
    }

    [Fact]
    public void StringLengthEmitsTheLengthOperator()
    {
        var document = Document();
        document.Functions.Add(new ScriptFunction
        {
            Id = "f1",
            Name = "length",
            Returns = [new ScriptReturn { Name = "result", Type = ScriptValueType.Int }],
            Graph = new ScriptGraph
            {
                Nodes =
                {
                    new ScriptNode { Id = "n1", Kind = "string.length", Literals = { ["text"] = Json("abc") } },
                    new ScriptNode
                    {
                        Id = "n2",
                        Kind = "flow.return",
                        Pins = [new ScriptPin { Id = "result", Direction = ScriptPinDirection.In, Type = ScriptValueType.Int }]
                    }
                },
                Edges = { Edge("e1", "n1", "result", "n2", "result") }
            }
        });

        var result = Compiler.Compile(document);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains("return #\"abc\"", result.LuaText);
    }

    [Fact]
    public void NullableReceiverWritesAreGuarded()
    {
        var document = Document(Event(), new ScriptNode
        {
            Id = "n2",
            Kind = "player.get_by_guid",
            Literals = { ["guid"] = Json(42) }
        }, new ScriptNode
        {
            Id = "n3",
            Kind = "player.add_item",
            Literals = { ["item"] = Json(123) }
        });
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n3", "exec"));
        document.Graph.Edges.Add(Edge("e2", "n2", "player", "n3", "self"));

        var result = Compiler.Compile(document);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains("~= nil then", result.LuaText);
        Assert.Contains(":AddItem(123", result.LuaText);
    }

    [Fact]
    public void NullableReadsKeepFalseResults()
    {
        var document = Document(Event(), new ScriptNode
        {
            Id = "n2",
            Kind = "unit.has_aura",
            Literals = { ["spell"] = Json(1) }
        }, new ScriptNode { Id = "n3", Kind = "flow.not_equal", Literals = { ["b"] = Json(false) } }, new ScriptNode
        {
            Id = "n4",
            Kind = "flow.branch"
        });
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n4", "exec"));
        document.Graph.Edges.Add(Edge("e2", "n1", "killer", "n2", "self"));
        document.Graph.Edges.Add(Edge("e3", "n2", "hasAura", "n3", "a"));
        document.Graph.Edges.Add(Edge("e4", "n3", "result", "n4", "condition"));

        var result = Compiler.Compile(document);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains("return killer:HasAura(1)", result.LuaText);
        Assert.Contains("~= false", result.LuaText);
        Assert.DoesNotContain("and killer:HasAura", result.LuaText);
    }

    [Fact]
    public void OutputNameUsesTheSlugAndShortId()
    {
        var document = ScriptDocument.Create("Creature On Death");
        document.Id = "abcdef0123456789abcdef0123456789";
        Assert.Equal("acw_creature-on-death_abcdef01.lua", ScriptCompiler.OutputName(document));
    }
}
