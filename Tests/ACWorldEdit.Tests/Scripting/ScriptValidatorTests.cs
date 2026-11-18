using System.Text.Json;
using ACWorldEdit.Foundation.Scripting;
using Xunit;

/// <summary>One case per structural, event, variable, return and limit rule the validator owns.</summary>
public class ScriptValidatorTests
{
    static readonly ScriptHostProfile Host = AcScriptHosts.Profile;
    static readonly ScriptValidator Validator = new(Host);

    static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);

    static ScriptDocument Document(params ScriptNode[] nodes)
    {
        var document = ScriptDocument.Create("Validator");
        document.Graph.Nodes.AddRange(nodes);
        return document;
    }

    static ScriptNode Event(string id = "n1", string kind = "event.creature.on_died", int entry = 1234) => new()
    {
        Id = id,
        Kind = kind,
        Properties = { ["entry"] = Json(entry) }
    };

    static ScriptNode Say(string id = "n2", string message = "Hello") => new()
    {
        Id = id,
        Kind = "unit.send_unit_say",
        Literals = { ["message"] = Json(message), ["language"] = Json(0) }
    };

    static ScriptNode Node(string id, string kind) => new() { Id = id, Kind = kind };

    static ScriptEdge Edge(string id, string fromNode, string fromPin, string toNode, string toPin) => new()
    {
        Id = id,
        From = new ScriptPinRef { Node = fromNode, Pin = fromPin },
        To = new ScriptPinRef { Node = toNode, Pin = toPin }
    };

    static IReadOnlyList<ScriptDiagnostic> Validate(ScriptDocument document) => Validator.Validate(document);

    static void AddSayChain(ScriptDocument document, string eventNode = "n1", string sayNode = "n2")
    {
        document.Graph.Edges.Add(Edge("e1", eventNode, "exec", sayNode, "exec"));
        document.Graph.Edges.Add(Edge("e2", eventNode, "creature", sayNode, "self"));
    }

    [Fact]
    public void AValidCreatureDeathGraphHasNoErrors()
    {
        var document = Document(Event(), Say());
        AddSayChain(document);

        var diagnostics = Validate(document);

        Assert.DoesNotContain(diagnostics, item => item.Severity == ScriptSeverity.Error);
    }

    [Fact]
    public void UnknownNodeKindIsAnError()
    {
        var diagnostics = Validate(Document(Node("n1", "future.node")));
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.UnknownNode && item.IsError);
    }

    [Fact]
    public void UnimplementedHostEventIsAnError()
    {
        var definition = Host.Events.Values.First(item => item.Availability == ScriptAvailability.Unimplemented);
        var node = new ScriptNode { Id = "n1", Kind = definition.Id };
        foreach (var scope in definition.ScopeParams)
        {
            node.Properties[scope.Name] = Json(1);
        }

        var diagnostics = Validate(Document(node));
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.NotAvailable && item.IsError);
    }

    [Fact]
    public void MissingRequiredInputIsAnError()
    {
        var document = Document(Event(), Say());
        document.Graph.Nodes[1].Literals.Clear();
        AddSayChain(document);

        var diagnostics = Validate(document);
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.MissingInput && item.PinId == "message");
    }

    [Fact]
    public void WrongPinTypeIsAnError()
    {
        var document = Document(Event(), Say());
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n2", "exec"));
        document.Graph.Edges.Add(Edge("e2", "n1", "creature", "n2", "self"));
        document.Graph.Edges.Add(Edge("e3", "n1", "creature", "n2", "message"));

        var diagnostics = Validate(document);
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.WrongType && item.PinId == "message");
    }

    [Fact]
    public void TwoConnectionsToOneDataInputIsAnError()
    {
        var document = Document(Event(), Say());
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n2", "exec"));
        document.Graph.Edges.Add(Edge("e2", "n1", "creature", "n2", "self"));
        document.Graph.Edges.Add(Edge("e3", "n1", "killer", "n2", "self"));

        var diagnostics = Validate(document);
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.MultipleDataInputs);
    }

    [Fact]
    public void ExecFanOutIsAnError()
    {
        var document = Document(Event(), Say("n2"), Say("n3"));
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n2", "exec"));
        document.Graph.Edges.Add(Edge("e2", "n1", "exec", "n3", "exec"));
        document.Graph.Edges.Add(Edge("e3", "n1", "creature", "n2", "self"));
        document.Graph.Edges.Add(Edge("e4", "n1", "creature", "n3", "self"));

        var diagnostics = Validate(document);
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.ExecFanOut);
    }

    [Fact]
    public void ExecAndDataPinsCannotBeConnected()
    {
        var document = Document(Event(), Say());
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n2", "exec"));
        document.Graph.Edges.Add(Edge("e2", "n1", "creature", "n2", "self"));
        document.Graph.Edges.Add(Edge("e3", "n1", "creature", "n2", "exec"));

        var diagnostics = Validate(document);
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.ExecDataMismatch);
    }

    [Fact]
    public void ExecCycleIsAnError()
    {
        var document = Document(Event(), Say("n2"), Say("n3"));
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n2", "exec"));
        document.Graph.Edges.Add(Edge("e2", "n2", "exec_out", "n3", "exec"));
        document.Graph.Edges.Add(Edge("e3", "n3", "exec_out", "n2", "exec"));
        document.Graph.Edges.Add(Edge("e4", "n1", "creature", "n2", "self"));
        document.Graph.Edges.Add(Edge("e5", "n1", "creature", "n3", "self"));

        var diagnostics = Validate(document);
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.ExecCycle);
    }

    [Fact]
    public void DataCycleIsAnError()
    {
        var document = Document(Node("n1", "object.to_player"), Node("n2", "object.to_player"));
        document.Graph.Edges.Add(Edge("e1", "n1", "player", "n2", "self"));
        document.Graph.Edges.Add(Edge("e2", "n2", "player", "n1", "self"));

        var diagnostics = Validate(document);
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.DataCycle);
    }

    [Fact]
    public void MissingEventScopeIsAnError()
    {
        var node = new ScriptNode { Id = "n1", Kind = "event.creature.on_died" };
        var diagnostics = Validate(Document(node));
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.MissingScope);
    }

    [Fact]
    public void DuplicateRegistrationIsAWarning()
    {
        var document = Document(Event("n1"), Event("n2"));
        var diagnostics = Validate(document);
        Assert.Contains(diagnostics, item =>
            item.Code == ScriptDiagnosticCodes.DuplicateRegistration && item.Severity == ScriptSeverity.Warning);
    }

    [Fact]
    public void ReturnOutsideAnEventChainIsAnError()
    {
        var node = Node("n1", ScriptBuiltinCatalog.Return);
        node.Pins = [new ScriptPin { Id = "exec", Direction = ScriptPinDirection.In, Type = ScriptValueType.Exec }];
        var diagnostics = Validate(Document(node));
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.ReturnContext);
    }

    [Fact]
    public void ReturnSlotsMustMatchTheEventReturns()
    {
        var definition = Host.Events["event.player.on_chat"];
        var document = Document(
            new ScriptNode { Id = "n1", Kind = definition.Id },
            new ScriptNode
            {
                Id = "n2",
                Kind = ScriptBuiltinCatalog.Return,
                Pins =
                [
                    new ScriptPin { Id = "exec", Direction = ScriptPinDirection.In, Type = ScriptValueType.Exec },
                    new ScriptPin { Id = "handled", Direction = ScriptPinDirection.In, Type = ScriptValueType.Bool }
                ]
            });
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n2", "exec"));

        var diagnostics = Validate(document);
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.ReturnContext);
    }

    [Fact]
    public void UnknownVariableIsAnError()
    {
        var node = Node("n1", ScriptBuiltinCatalog.VariableGet);
        node.Properties["variable"] = Json("missing");
        var diagnostics = Validate(Document(node));
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.UnknownVariable);
    }

    [Fact]
    public void ObjectVariablesAreAnError()
    {
        var document = Document();
        document.Variables.Add(new ScriptVariable { Id = "v1", Name = "player", Type = ScriptValueType.Player });
        var diagnostics = Validate(document);
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.ObjectVariable);
    }

    [Fact]
    public void UnreachableWritesAreAWarning()
    {
        var diagnostics = Validate(Document(Say()));
        Assert.Contains(diagnostics, item =>
            item.Code == ScriptDiagnosticCodes.Unreachable && item.Severity == ScriptSeverity.Warning);
    }

    [Fact]
    public void LargeIntegerLiteralsAreAWarning()
    {
        var document = Document(Event(), new ScriptNode
        {
            Id = "n2",
            Kind = "unit.set_health",
            Literals = { ["health"] = Json(9_007_199_254_740_993L) }
        });
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n2", "exec"));
        document.Graph.Edges.Add(Edge("e2", "n1", "creature", "n2", "self"));

        var diagnostics = Validate(document);
        Assert.Contains(diagnostics, item =>
            item.Code == ScriptDiagnosticCodes.LargeInteger && item.Severity == ScriptSeverity.Warning);
    }

    [Fact]
    public void FunctionCallArityIsChecked()
    {
        var document = Document(Event(), new ScriptNode
        {
            Id = "n2",
            Kind = ScriptBuiltinCatalog.CallFunction,
            Properties = { ["function"] = Json("f1") }
        });
        document.Functions.Add(new ScriptFunction
        {
            Id = "f1",
            Name = "SpawnWave",
            Parameters = [new ScriptParameter { Id = "p1", Name = "wave", Type = ScriptValueType.Int }]
        });
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "n2", "exec"));

        var diagnostics = Validate(document);
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.CallArity && item.PinId == "wave");
    }

    [Fact]
    public void UnknownFunctionIsAnError()
    {
        var node = new ScriptNode
        {
            Id = "n1",
            Kind = ScriptBuiltinCatalog.CallFunction,
            Properties = { ["function"] = Json("missing") }
        };
        var diagnostics = Validate(Document(node));
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.UnknownFunction);
    }

    [Fact]
    public void RecursionIsAnError()
    {
        var document = Document();
        document.Functions.Add(new ScriptFunction
        {
            Id = "f1",
            Name = "Loop",
            Graph =
            {
                Nodes =
                {
                    new ScriptNode
                    {
                        Id = "c1",
                        Kind = ScriptBuiltinCatalog.CallFunction,
                        Properties = { ["function"] = Json("f1") }
                    }
                }
            }
        });

        var diagnostics = Validate(document);
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.Recursion);
    }

    [Fact]
    public void EdgeToMissingPinIsAnError()
    {
        var document = Document(Event());
        document.Graph.Edges.Add(Edge("e1", "n1", "exec", "missing", "exec"));
        var diagnostics = Validate(document);
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.BadEdge);
    }

    [Fact]
    public void DuplicateNodeIdsAreAnError()
    {
        var diagnostics = Validate(Document(Event("dup"), Event("dup")));
        Assert.Contains(diagnostics, item => item.Code == ScriptDiagnosticCodes.DuplicateNode);
    }
}
