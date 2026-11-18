using System.Text.Json;

namespace ACWorldEdit.Foundation.Scripting;

public sealed partial class ScriptValidator
{
    sealed class NodeInfo
    {
        public required ScriptNode Node { get; init; }
        public required IReadOnlyList<ScriptPin> Pins { get; init; }
        public required Dictionary<string, ScriptPin> PinById { get; init; }
        public ScriptCapability? Capability { get; init; }
        public ScriptEventDefinition? Event { get; init; }
        public bool IsEvent => Event is not null;
        public bool IsReturn => Node.Kind == ScriptBuiltinCatalog.Return;
        public bool IsWrite => Capability?.Effect == ScriptEffect.Write
            || Node.Kind is ScriptBuiltinCatalog.VariableSet or ScriptBuiltinCatalog.CallFunction;
    }

    readonly record struct EdgeInfo(NodeInfo From, ScriptPin FromPin, NodeInfo To, ScriptPin ToPin);

    void ValidateGraph(
        ScriptDocument document,
        ScriptGraph graph,
        IReadOnlyList<ScriptReturn>? returnContext,
        IReadOnlyDictionary<string, ScriptVariable> variables,
        IReadOnlyDictionary<string, ScriptFunction> functions,
        List<ScriptDiagnostic> diagnostics)
    {
        var infos = BuildInfos(document, graph, diagnostics);
        var edges = ValidateEdges(graph, infos, diagnostics);

        if (HasCycle(edges.Where(edge => edge.FromPin.Type == ScriptValueType.Exec)))
        {
            diagnostics.Add(Error(ScriptDiagnosticCodes.ExecCycle, "Exec connections form a cycle."));
        }

        if (HasCycle(edges.Where(edge => edge.FromPin.Type != ScriptValueType.Exec)))
        {
            diagnostics.Add(Error(ScriptDiagnosticCodes.DataCycle, "Data connections form a cycle."));
        }

        var roots = CollectRoots(infos, edges, diagnostics);
        ValidateReturns(infos, roots, returnContext, diagnostics);
        ValidateInputs(infos, edges, returnContext is not null, diagnostics);
        ValidateScope(infos, diagnostics);
        ValidateReferences(infos, edges, variables, functions, diagnostics);
        ValidateReachability(infos, edges, returnContext, diagnostics);
        ValidateLargeIntegers(infos, diagnostics);
    }

    Dictionary<string, NodeInfo> BuildInfos(ScriptDocument document, ScriptGraph graph, List<ScriptDiagnostic> diagnostics)
    {
        var infos = new Dictionary<string, NodeInfo>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id) || infos.ContainsKey(node.Id))
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.DuplicateNode, $"Node id '{node.Id}' is blank or duplicated."));
                continue;
            }

            var pins = ScriptPins.Build(document, host, node);
            var pinById = new Dictionary<string, ScriptPin>(StringComparer.Ordinal);
            foreach (var pin in pins)
            {
                if (!pinById.TryAdd(pin.Id, pin))
                {
                    diagnostics.Add(Error(ScriptDiagnosticCodes.DuplicatePin, $"Node '{node.Id}' has duplicate pin '{pin.Id}'.", node.Id, pin.Id));
                }
            }

            var capability = host.Capability(node.Kind);
            var definition = host.Event(node.Kind);
            if (capability is null && definition is null && ScriptBuiltinCatalog.Find(node.Kind) is null)
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.UnknownNode, $"Unknown node kind '{node.Kind}'.", node.Id));
            }

            if (capability?.Availability == ScriptAvailability.Unimplemented
                || definition?.Availability == ScriptAvailability.Unimplemented)
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.NotAvailable, $"'{node.Kind}' is not available on {host.Label}.", node.Id));
            }

            if (definition?.Performance == ScriptPerformance.HighFrequency)
            {
                diagnostics.Add(Info(ScriptDiagnosticCodes.HighFrequency,
                    $"'{definition.Display}' is a high-frequency event; keep the handler small.", node.Id));
            }

            infos[node.Id] = new NodeInfo
            {
                Node = node,
                Pins = pins,
                PinById = pinById,
                Capability = capability,
                Event = definition
            };
        }

        return infos;
    }

    static List<EdgeInfo> ValidateEdges(ScriptGraph graph, Dictionary<string, NodeInfo> infos, List<ScriptDiagnostic> diagnostics)
    {
        var edges = new List<EdgeInfo>();
        var outExec = new Dictionary<(string Node, string Pin), int>();
        var inData = new Dictionary<(string Node, string Pin), int>();
        foreach (var edge in graph.Edges)
        {
            if (!infos.TryGetValue(edge.From.Node, out var from) || !infos.TryGetValue(edge.To.Node, out var to))
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.BadEdge, "Edge references a missing node.", edgeId: edge.Id));
                continue;
            }

            if (!from.PinById.TryGetValue(edge.From.Pin, out var fromPin) || !to.PinById.TryGetValue(edge.To.Pin, out var toPin))
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.BadEdge, "Edge references a missing pin.", edgeId: edge.Id));
                continue;
            }

            if (fromPin.Direction != ScriptPinDirection.Out || toPin.Direction != ScriptPinDirection.In)
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.ExecDataMismatch, "An edge must run from an output pin to an input pin.", edgeId: edge.Id));
                continue;
            }

            bool fromExec = fromPin.Type == ScriptValueType.Exec;
            bool toExec = toPin.Type == ScriptValueType.Exec;
            if (fromExec != toExec)
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.ExecDataMismatch, "Exec and data pins cannot be connected.", edgeId: edge.Id));
                continue;
            }

            if (!ScriptTypeChecker.PinAccepts(fromPin, toPin))
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.WrongType,
                    $"{ScriptValueTypeNames.ToJson(fromPin.Type)} cannot connect to {ScriptValueTypeNames.ToJson(toPin.Type)}.",
                    to.Node.Id, toPin.Id));
            }

            if (fromExec)
            {
                outExec[(from.Node.Id, fromPin.Id)] = outExec.GetValueOrDefault((from.Node.Id, fromPin.Id)) + 1;
            }
            else
            {
                inData[(to.Node.Id, toPin.Id)] = inData.GetValueOrDefault((to.Node.Id, toPin.Id)) + 1;
            }

            edges.Add(new EdgeInfo(from, fromPin, to, toPin));
        }

        foreach (var (pin, count) in inData.Where(pair => pair.Value > 1))
        {
            diagnostics.Add(Error(ScriptDiagnosticCodes.MultipleDataInputs,
                $"Data input '{pin.Pin}' on '{pin.Node}' has {count} connections; only one is allowed.", pin.Node, pin.Pin));
        }

        foreach (var (pin, count) in outExec.Where(pair => pair.Value > 1))
        {
            diagnostics.Add(Error(ScriptDiagnosticCodes.ExecFanOut,
                $"Exec output '{pin.Pin}' on '{pin.Node}' has {count} connections; an exec chain cannot fan out.", pin.Node, pin.Pin));
        }

        return edges;
    }

    static bool HasCycle(IEnumerable<EdgeInfo> edges)
    {
        var adjacency = edges
            .GroupBy(edge => edge.From.Node.Id)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.To.Node.Id).ToList());
        var state = new Dictionary<string, int>(StringComparer.Ordinal);

        bool Visit(string id)
        {
            int current = state.GetValueOrDefault(id);
            if (current == 1)
            {
                return true;
            }

            if (current == 2)
            {
                return false;
            }

            state[id] = 1;
            if (adjacency.TryGetValue(id, out var targets) && targets.Any(Visit))
            {
                return true;
            }

            state[id] = 2;
            return false;
        }

        return adjacency.Keys.Any(Visit);
    }

    static Dictionary<string, HashSet<string>> CollectRoots(
        Dictionary<string, NodeInfo> infos, List<EdgeInfo> edges, List<ScriptDiagnostic> diagnostics)
    {
        var roots = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var info in infos.Values.Where(info => info.IsEvent))
        {
            roots[info.Node.Id] = [info.Node.Id];
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var edge in edges.Where(edge => edge.FromPin.Type == ScriptValueType.Exec))
            {
                if (!roots.TryGetValue(edge.From.Node.Id, out var fromRoots))
                {
                    continue;
                }

                if (!roots.TryGetValue(edge.To.Node.Id, out var toRoots))
                {
                    toRoots = [];
                    roots[edge.To.Node.Id] = toRoots;
                }

                foreach (string root in fromRoots)
                {
                    changed |= toRoots.Add(root);
                }
            }
        }

        foreach (var (id, set) in roots.Where(pair => pair.Value.Count > 1))
        {
            diagnostics.Add(Error(ScriptDiagnosticCodes.CrossEntry,
                "Exec chains from different events are merged; each event chain must stay separate.", id));
        }

        return roots;
    }

    static void ValidateReturns(
        Dictionary<string, NodeInfo> infos,
        Dictionary<string, HashSet<string>> roots,
        IReadOnlyList<ScriptReturn>? returnContext,
        List<ScriptDiagnostic> diagnostics)
    {
        foreach (var info in infos.Values.Where(info => info.IsReturn))
        {
            var nodeRoots = roots.TryGetValue(info.Node.Id, out var set) ? set : [];
            if (returnContext is null)
            {
                if (nodeRoots.Count == 0)
                {
                    diagnostics.Add(Error(ScriptDiagnosticCodes.ReturnContext,
                        "A Return node is not reachable from an event.", info.Node.Id));
                    continue;
                }

                if (nodeRoots.Count > 1)
                {
                    continue;
                }

                var returns = infos[nodeRoots.First()].Event?.Returns ?? [];
                CompareReturn(info, returns.Select(slot => (slot.Name, slot.Type)), diagnostics);
            }
            else
            {
                CompareReturn(info, returnContext.Select(slot => (slot.Name, slot.Type)), diagnostics);
            }
        }
    }

    static void CompareReturn(
        NodeInfo info,
        IEnumerable<(string Name, ScriptValueType Type)> expected,
        List<ScriptDiagnostic> diagnostics)
    {
        var slots = expected.ToArray();
        var pins = info.Pins.Where(pin => pin.Direction == ScriptPinDirection.In && pin.Type != ScriptValueType.Exec).ToArray();
        bool matches = pins.Length == slots.Length
            && slots.All(slot => pins.Any(pin => pin.Id == slot.Name && pin.Type == slot.Type));
        if (!matches)
        {
            diagnostics.Add(Error(ScriptDiagnosticCodes.ReturnContext,
                "The Return node's slots do not match the event or function returns.", info.Node.Id));
        }
    }

    static void ValidateInputs(
        Dictionary<string, NodeInfo> infos,
        List<EdgeInfo> edges,
        bool functionContext,
        List<ScriptDiagnostic> diagnostics)
    {
        var incomingData = edges.Where(edge => edge.FromPin.Type != ScriptValueType.Exec)
            .GroupBy(edge => edge.To.Node.Id)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.ToPin.Id).ToHashSet(StringComparer.Ordinal));
        var incomingExec = edges.Where(edge => edge.FromPin.Type == ScriptValueType.Exec)
            .GroupBy(edge => edge.To.Node.Id)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.ToPin.Id).ToHashSet(StringComparer.Ordinal));

        foreach (var info in infos.Values)
        {
            var data = incomingData.GetValueOrDefault(info.Node.Id) ?? [];
            var exec = incomingExec.GetValueOrDefault(info.Node.Id) ?? [];
            foreach (var pin in info.Pins.Where(pin => pin.Direction == ScriptPinDirection.In))
            {
                bool hasLiteral = info.Node.Literals.TryGetValue(pin.Id, out var literal);
                if (hasLiteral && !ScriptTypeChecker.LiteralMatches(literal, pin.Type, pin.Nullable))
                {
                    diagnostics.Add(Error(ScriptDiagnosticCodes.WrongType,
                        $"A {ScriptTypeChecker.KindName(literal)} literal cannot feed {ScriptValueTypeNames.ToJson(pin.Type)}.",
                        info.Node.Id, pin.Id));
                }

                if (pin.Type == ScriptValueType.Exec)
                {
                    // A function graph has no event edge to start it: an unconnected exec input is
                    // an entry point (the compiler lowers roots and the legacy expression Return).
                    bool functionRoot = functionContext;
                    if (!exec.Contains(pin.Id) && !functionRoot)
                    {
                        diagnostics.Add(Error(ScriptDiagnosticCodes.MissingInput, $"Exec input '{pin.Id}' is not connected.", info.Node.Id, pin.Id));
                    }

                    continue;
                }

                if (!data.Contains(pin.Id) && !hasLiteral && IsRequired(info, pin))
                {
                    diagnostics.Add(Error(ScriptDiagnosticCodes.MissingInput, $"Input '{pin.Id}' needs a connection or a literal.", info.Node.Id, pin.Id));
                }
            }

            foreach (string key in info.Node.Literals.Keys)
            {
                if (!info.PinById.TryGetValue(key, out var pin) || pin.Direction != ScriptPinDirection.In)
                {
                    diagnostics.Add(Error(ScriptDiagnosticCodes.WrongType,
                        $"Literal '{key}' does not target an input pin.", info.Node.Id, key));
                }
            }
        }
    }

    static bool IsRequired(NodeInfo info, ScriptPin pin)
    {
        if (pin.Id == "self")
        {
            return true;
        }

        if (info.Event is not null)
        {
            return false;
        }

        if (info.Capability is { } capability)
        {
            var parameter = capability.Params.FirstOrDefault(item => item.Name == pin.Id);
            return parameter is null || !parameter.Optional;
        }

        return info.Node.Kind switch
        {
            ScriptBuiltinCatalog.VariableSet => pin.Id == "value",
            ScriptBuiltinCatalog.CallFunction => true,
            ScriptBuiltinCatalog.Return => false,
            _ => true
        };
    }

    static void ValidateScope(Dictionary<string, NodeInfo> infos, List<ScriptDiagnostic> diagnostics)
    {
        var registrations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var info in infos.Values.Where(info => info.IsEvent))
        {
            var definition = info.Event!;
            foreach (var scope in definition.ScopeParams)
            {
                if (!info.Node.Properties.TryGetValue(scope.Name, out var value)
                    || !ScriptTypeChecker.LiteralMatches(value, scope.Type, scope.Nullable))
                {
                    diagnostics.Add(Error(ScriptDiagnosticCodes.MissingScope,
                        $"Event '{definition.Display}' needs a {ScriptValueTypeNames.ToJson(scope.Type)} '{scope.Name}'.", info.Node.Id));
                }
            }

            if (info.Node.Properties.TryGetValue("shots", out var shots)
                && (shots.ValueKind != JsonValueKind.Number || !shots.TryGetInt64(out long count) || count < 0))
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.MissingScope, "'shots' must be a non-negative integer.", info.Node.Id));
            }

            string key = info.Node.Kind + "|"
                + string.Join(";", definition.ScopeParams.Select(scope => $"{scope.Name}={Raw(info.Node, scope.Name)}"));
            if (!registrations.Add(key))
            {
                diagnostics.Add(Warning(ScriptDiagnosticCodes.DuplicateRegistration,
                    $"'{definition.Display}' is registered more than once for the same scope; ALE composes the handlers.", info.Node.Id));
            }
        }
    }

    static string Raw(ScriptNode node, string name) =>
        node.Properties.TryGetValue(name, out var value) ? value.GetRawText() : "";

    static void ValidateReferences(
        Dictionary<string, NodeInfo> infos,
        List<EdgeInfo> edges,
        IReadOnlyDictionary<string, ScriptVariable> variables,
        IReadOnlyDictionary<string, ScriptFunction> functions,
        List<ScriptDiagnostic> diagnostics)
    {
        var incomingData = edges.Where(edge => edge.FromPin.Type != ScriptValueType.Exec)
            .GroupBy(edge => edge.To.Node.Id)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.ToPin.Id).ToHashSet(StringComparer.Ordinal));

        foreach (var info in infos.Values)
        {
            if (info.Node.Kind is ScriptBuiltinCatalog.VariableGet or ScriptBuiltinCatalog.VariableSet)
            {
                string? id = ScriptPins.PropertyString(info.Node, "variable");
                if (id is null || !variables.ContainsKey(id))
                {
                    diagnostics.Add(Error(ScriptDiagnosticCodes.UnknownVariable,
                        $"Variable node '{info.Node.Id}' references an undeclared variable.", info.Node.Id));
                }
            }
            else if (info.Node.Kind == ScriptBuiltinCatalog.CallFunction)
            {
                string? id = ScriptPins.PropertyString(info.Node, "function");
                if (id is null || !functions.TryGetValue(id, out var function))
                {
                    diagnostics.Add(Error(ScriptDiagnosticCodes.UnknownFunction,
                        $"Function call '{info.Node.Id}' references an undeclared function.", info.Node.Id));
                    continue;
                }

                var data = incomingData.GetValueOrDefault(info.Node.Id) ?? [];
                foreach (var parameter in function.Parameters)
                {
                    if (!data.Contains(parameter.Name) && !info.Node.Literals.ContainsKey(parameter.Name))
                    {
                        diagnostics.Add(Error(ScriptDiagnosticCodes.CallArity,
                            $"Call to '{function.Name}' is missing argument '{parameter.Name}'.", info.Node.Id, parameter.Name));
                    }
                }
            }
        }
    }

    static void ValidateReachability(
        Dictionary<string, NodeInfo> infos,
        List<EdgeInfo> edges,
        IReadOnlyList<ScriptReturn>? returnContext,
        List<ScriptDiagnostic> diagnostics)
    {
        if (returnContext is not null)
        {
            return;
        }

        var reachable = new HashSet<string>(
            infos.Values.Where(info => info.IsEvent).Select(info => info.Node.Id), StringComparer.Ordinal);
        var execTargets = edges.Where(edge => edge.FromPin.Type == ScriptValueType.Exec)
            .GroupBy(edge => edge.From.Node.Id)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.To.Node.Id).ToArray());
        var dataSources = edges.Where(edge => edge.FromPin.Type != ScriptValueType.Exec)
            .GroupBy(edge => edge.To.Node.Id)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.From.Node.Id).ToArray());

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (string id in reachable.ToArray())
            {
                if (execTargets.TryGetValue(id, out var targets))
                {
                    foreach (string target in targets)
                    {
                        changed |= reachable.Add(target);
                    }
                }

                if (dataSources.TryGetValue(id, out var sources))
                {
                    foreach (string source in sources)
                    {
                        changed |= reachable.Add(source);
                    }
                }
            }
        }

        foreach (var info in infos.Values.Where(info => !reachable.Contains(info.Node.Id)))
        {
            diagnostics.Add(Warning(ScriptDiagnosticCodes.Unreachable,
                $"Node '{info.Node.Id}' is not reachable from an event.", info.Node.Id));
        }

        foreach (var info in infos.Values.Where(info => info.IsWrite && info.PinById.ContainsKey("exec_out")))
        {
            if (!edges.Any(edge => edge.From.Node.Id == info.Node.Id && edge.FromPin.Id == "exec_out"))
            {
                diagnostics.Add(Info(ScriptDiagnosticCodes.UnconnectedExec,
                    $"The exec output of '{info.Node.Id}' is not connected.", info.Node.Id, "exec_out"));
            }
        }
    }

    static void ValidateLargeIntegers(Dictionary<string, NodeInfo> infos, List<ScriptDiagnostic> diagnostics)
    {
        foreach (var info in infos.Values)
        {
            foreach (var literal in info.Node.Literals.Values)
            {
                if (ScriptTypeChecker.IsLargeInteger(literal))
                {
                    diagnostics.Add(Warning(ScriptDiagnosticCodes.LargeInteger,
                        "An integer literal above 2^53 loses precision in Lua.", info.Node.Id));
                }
            }
        }
    }
}
