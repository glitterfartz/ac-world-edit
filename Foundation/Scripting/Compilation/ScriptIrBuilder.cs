using System.Text.Json;

namespace ACWorldEdit.Foundation.Scripting;

/// <summary>
/// Lowers a validated document into the host-agnostic IR: event functions plus registrations,
/// exec chains as statements, pure/read nodes as expressions, and write outputs materialized into
/// locals when a data pin is consumed more than once.
/// </summary>
public sealed class ScriptIrBuilder
{
    readonly ScriptHostProfile host;

    public ScriptIrBuilder(ScriptHostProfile host)
    {
        this.host = host;
    }

    public IrModule Build(ScriptDocument document, string sourceName) => new BuildState(document, host).Build(sourceName);

    sealed class BuildState
    {
        readonly ScriptDocument document;
        readonly ScriptHostProfile host;
        readonly Dictionary<string, string> variableNames = new(StringComparer.Ordinal);
        readonly Dictionary<string, string> functionNames = new(StringComparer.Ordinal);
        readonly Dictionary<string, ScriptVariable> variables;
        readonly Dictionary<string, ScriptFunction> functions;
        readonly Dictionary<string, EventContext> events = new(StringComparer.Ordinal);
        readonly HashSet<string> usedNames = new(StringComparer.Ordinal);
        readonly List<IrVariable> irVariables = [];
        readonly List<IrFunction> irFunctions = [];
        readonly List<IrFunction> irEvents = [];
        readonly List<IrRegistration> registrations = [];

        public BuildState(ScriptDocument document, ScriptHostProfile host)
        {
            this.document = document;
            this.host = host;
            variables = document.Variables
                .GroupBy(variable => variable.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            functions = document.Functions
                .GroupBy(function => function.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }

        public IrModule Build(string sourceName)
        {
            BuildVariables();
            BuildFunctions();
            BuildEvents(sourceName);
            return new IrModule(document.Name, sourceName, irVariables, irFunctions, irEvents, registrations);
        }

        void BuildVariables()
        {
            foreach (var variable in document.Variables)
            {
                string name = UniqueName(LuaLiteral.Identifier(variable.Name));
                variableNames[variable.Id] = name;
                irVariables.Add(new IrVariable(name, VariableDefault(variable)));
            }
        }

        IrLiteral? VariableDefault(ScriptVariable variable) =>
            variable.Default is { } value ? Literal(value, variable.Type) : null;

        void BuildFunctions()
        {
            foreach (var function in document.Functions)
            {
                string name = UniqueName(LuaLiteral.Identifier(function.Name));
                functionNames[function.Id] = name;
                var index = new GraphIndex(document, host, function.Graph);
                var usedParamNames = new HashSet<string>(StringComparer.Ordinal);
                var parameters = function.Parameters
                    .Select(parameter => new IrParameter(UniqueFrom(parameter.Name, usedParamNames), parameter.Type))
                    .ToList();
                var returns = function.Returns.Select(slot => new IrReturnSlot(slot.Name, slot.Type)).ToList();
                var body = BuildFunctionBody(function, index);
                irFunctions.Add(new IrFunction(name, parameters, returns, body));
            }
        }

        IReadOnlyList<IrStmt> BuildFunctionBody(ScriptFunction function, GraphIndex index)
        {
            var block = new BlockContext(index);
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in function.Parameters)
            {
                string name = UniqueFrom(parameter.Name, used);
                usedNames.Add(name);
                foreach (var node in function.Graph.Nodes.Where(node => node.Kind == ScriptBuiltinCatalog.ParameterGet
                             && ScriptPins.PropertyString(node, "parameter") == parameter.Id))
                {
                    block.Bindings[(node.Id, "value")] = name;
                }
            }

            var returns = function.Returns.Select(slot => new IrReturnSlot(slot.Name, slot.Type)).ToList();
            var roots = function.Graph.Nodes
                .Where(node => !index.HasExecInput(node.Id)
                    && host.Event(node.Kind) is null
                    && node.Kind is not ScriptBuiltinCatalog.Return
                    && index.Pins[node.Id].ContainsKey("exec"))
                .ToList();
            foreach (var root in roots)
            {
                BuildFromNode(index, root.Id, block, returns);
            }

            // Legacy shape: a Return node alone, with its slots fed by data expressions.
            if (roots.Count == 0)
            {
                var entry = function.Graph.Nodes.FirstOrDefault(node =>
                    node.Kind == ScriptBuiltinCatalog.Return && !index.HasExecInput(node.Id));
                if (entry is not null)
                {
                    var values = function.Returns.Select(slot => ResolveInput(index, entry.Id, slot.Name, block)).ToList();
                    block.Statements.Add(new IrReturn(values));
                }
            }

            return block.Statements;
        }

        void BuildEvents(string sourceName)
        {
            int index = 0;
            foreach (var node in document.Graph.Nodes)
            {
                if (host.Event(node.Kind) is not { } definition)
                {
                    continue;
                }

                index++;
                var graph = new GraphIndex(document, host, document.Graph);
                var parameters = new List<IrParameter> { new("event", ScriptValueType.Any) };
                var paramNames = new Dictionary<string, string>(StringComparer.Ordinal);
                var used = new HashSet<string> { "event" };
                usedNames.Add("event");
                foreach (var parameter in definition.Params)
                {
                    string name = UniqueFrom(parameter.Name, used);
                    usedNames.Add(name);
                    paramNames[parameter.Name] = name;
                    parameters.Add(new IrParameter(name, parameter.Type, parameter.Nullable));
                }

                var context = new EventContext(node, $"__acw_n{index}", paramNames, definition);
                events[node.Id] = context;

                var block = new BlockContext(graph);
                BuildExecBlock(graph, node.Id, "exec", block, definition.Returns.Select(slot =>
                    new IrReturnSlot(slot.Name, slot.Type)).ToList());
                if (definition.Returns.Length > 0 && block.Statements.LastOrDefault() is not IrReturn)
                {
                    block.Statements.Add(new IrReturn(definition.Returns.Select(_ => (IrExpr?)null).ToList()));
                }

                irEvents.Add(new IrFunction(context.Name, parameters,
                    definition.Returns.Select(slot => new IrReturnSlot(slot.Name, slot.Type)).ToList(),
                    block.Statements, IsEvent: true));
                registrations.Add(Registration(node, definition, context.Name));
            }
        }

        IrRegistration Registration(ScriptNode node, ScriptEventDefinition definition, string functionName)
        {
            var binding = host.EventBinding(definition.Id)
                ?? throw new InvalidOperationException($"Event '{definition.Id}' has no host binding.");
            var scopeValues = binding.ScopeArgs.Select(name =>
            {
                var scope = definition.ScopeParams.First(parameter => parameter.Name == name);
                return node.Properties.TryGetValue(name, out var value)
                    ? Literal(value, scope.Type)
                    : new IrLiteral(scope.Type, null);
            }).ToList();
            int shots = node.Properties.TryGetValue("shots", out var shotsValue) && shotsValue.TryGetInt32(out int count)
                ? Math.Max(0, count)
                : 0;
            return new IrRegistration(functionName, binding.Register, scopeValues, binding.EventId, binding.EventName, shots);
        }

        void BuildExecBlock(
            GraphIndex index, string nodeId, string pinId, BlockContext block, IReadOnlyList<IrReturnSlot> returns)
        {
            string? current = index.ExecTarget.GetValueOrDefault((nodeId, pinId));
            if (current is not null)
            {
                BuildFromNode(index, current, block, returns);
            }
        }

        /// <summary>Lower one exec node and everything its exec outputs lead to.</summary>
        void BuildFromNode(
            GraphIndex index, string start, BlockContext block, IReadOnlyList<IrReturnSlot> returns)
        {
            string? current = start;
            while (current is not null)
            {
                var node = index.Nodes[current];
                if (host.Event(node.Kind) is not null)
                {
                    return;
                }

                switch (node.Kind)
                {
                    case ScriptBuiltinCatalog.Branch:
                    {
                        var condition = ResolveInput(index, node.Id, "condition", block);
                        var thenBlock = block.Fork();
                        BuildExecBlock(index, node.Id, "true", thenBlock, returns);
                        var elseBlock = block.Fork();
                        BuildExecBlock(index, node.Id, "false", elseBlock, returns);
                        block.Statements.Add(new IrIf(condition, thenBlock.Statements, elseBlock.Statements));
                        return;
                    }
                    case ScriptBuiltinCatalog.Sequence:
                    {
                        BuildExecBlock(index, node.Id, "first", block, returns);
                        BuildExecBlock(index, node.Id, "second", block, returns);
                        return;
                    }
                    case ScriptBuiltinCatalog.ForRange:
                    {
                        var from = ResolveInput(index, node.Id, "from", block);
                        var to = ResolveInput(index, node.Id, "to", block);
                        string name = UniqueName("i");
                        var body = block.Fork();
                        body.Bindings[(node.Id, "index")] = name;
                        BuildExecBlock(index, node.Id, "exec_out", body, returns);
                        block.Statements.Add(new IrForRange(name, from, to, body.Statements));
                        return;
                    }
                    case ScriptBuiltinCatalog.VariableSet:
                    {
                        string? variableId = ScriptPins.PropertyString(node, "variable");
                        string name = variableNames.GetValueOrDefault(variableId ?? "", "");
                        var value = ResolveInput(index, node.Id, "value", block);
                        block.Statements.Add(new IrVarSet($"__vars.{name}", value));
                        current = index.ExecTarget.GetValueOrDefault((node.Id, "exec_out"));
                        continue;
                    }
                    case ScriptBuiltinCatalog.CallFunction:
                    {
                        EmitFunctionCall(index, node, block);
                        current = index.ExecTarget.GetValueOrDefault((node.Id, "exec_out"));
                        continue;
                    }
                    case ScriptBuiltinCatalog.Return:
                    {
                        var values = returns.Select(slot => ResolveInput(index, node.Id, slot.Name, block)).ToList();
                        block.Statements.Add(new IrReturn(values));
                        return;
                    }
                }

                if (host.Capability(node.Kind) is { } capability)
                {
                    if (capability.Effect != ScriptEffect.Write)
                    {
                        current = index.ExecTarget.GetValueOrDefault((node.Id, "exec_out"));
                        continue;
                    }

                    EmitWrite(index, node, capability, block);
                    current = index.ExecTarget.GetValueOrDefault((node.Id, "exec_out"));
                    continue;
                }

                return;
            }
        }

        void EmitFunctionCall(GraphIndex index, ScriptNode node, BlockContext block)
        {
            string? functionId = ScriptPins.PropertyString(node, "function");
            if (functionId is null || !functions.TryGetValue(functionId, out var function))
            {
                return;
            }

            var args = function.Parameters.Select(parameter =>
                ResolveInput(index, node.Id, parameter.Name, block)).ToList();
            block.Statements.Add(new IrCallStmt(null, $"__fns.{functionNames[functionId]}", args, ScriptReceiverStyle.None));
        }

        void EmitWrite(GraphIndex index, ScriptNode node, ScriptCapability capability, BlockContext block)
        {
            var binding = host.CapabilityBinding(capability.Id)
                ?? throw new InvalidOperationException($"Capability '{capability.Id}' has no host binding.");
            IrExpr? receiver = capability.Receivers.Length > 0 ? ResolveInput(index, node.Id, "self", block) : null;
            var args = ResolveArgs(index, node, capability, binding, block);

            var returnUses = capability.Returns
                .Select(slot => index.UseCounts.GetValueOrDefault((node.Id, slot.Name)))
                .ToArray();
            if (returnUses.Any(count => count > 0))
            {
                var names = capability.Returns
                    .Select(slot => UniqueName(LuaLiteral.Identifier(slot.Name)))
                    .ToList();
                var call = new IrCall(receiver, binding.Call, args, binding.Receiver);
                block.Statements.Add(new IrMultiLocal(names, call));
                for (int i = 0; i < capability.Returns.Length; i++)
                {
                    block.Locals[(node.Id, capability.Returns[i].Name)] = names[i];
                }

                return;
            }

            block.Statements.Add(new IrCallStmt(receiver, binding.Call, args, binding.Receiver));
        }

        IReadOnlyList<IrExpr> ResolveArgs(
            GraphIndex index, ScriptNode node, ScriptCapability capability, ScriptCapabilityBinding binding, BlockContext block)
        {
            var args = new List<IrExpr>();
            for (int i = 0; i < binding.Args.Length; i++)
            {
                var argument = binding.Args[i];
                var parameter = capability.Params.FirstOrDefault(item => item.Name == argument.Param);
                if (index.DataTo.ContainsKey((node.Id, argument.Param)))
                {
                    args.Add(ResolveInput(index, node.Id, argument.Param, block));
                }
                else if (node.Literals.TryGetValue(argument.Param, out var literal))
                {
                    args.Add(Literal(literal, parameter?.Type ?? ScriptValueType.Any));
                }
                else if (argument.DefaultLiteral is { } hostDefault)
                {
                    args.Add(DefaultLiteral(hostDefault, parameter?.Type ?? ScriptValueType.Any));
                }
                else if (parameter?.Default is { } semanticDefault)
                {
                    args.Add(DefaultLiteral(semanticDefault, parameter.Type));
                }
                else
                {
                    args.Add(new IrLiteral(parameter?.Type ?? ScriptValueType.Any, null));
                }
            }

            int end = args.Count;
            while (end > 0 && args[end - 1] is IrLiteral { Value: null })
            {
                end--;
            }

            return args.Take(end).ToList();
        }

        IrExpr ResolveInput(GraphIndex index, string nodeId, string pinId, BlockContext block)
        {
            if (index.DataTo.TryGetValue((nodeId, pinId), out var source))
            {
                return ResolveOutput(index, source.Node, source.Pin, block);
            }

            if (index.Nodes[nodeId].Literals.TryGetValue(pinId, out var literal)
                && index.Pins[nodeId].TryGetValue(pinId, out var pin))
            {
                return Literal(literal, pin.Type);
            }

            return new IrLiteral(ScriptValueType.Any, null);
        }

        IrExpr ResolveOutput(GraphIndex index, string nodeId, string pinId, BlockContext block)
        {
            if (block.Bindings.TryGetValue((nodeId, pinId), out var bound))
            {
                return new IrVariableGet(bound);
            }

            // Event parameters are already function parameters; never hoist them into a redundant local.
            if (events.ContainsKey(nodeId))
            {
                return BuildOutput(index, nodeId, pinId, block);
            }

            int uses = index.UseCounts.GetValueOrDefault((nodeId, pinId));
            bool write = IsWrite(index.Nodes[nodeId]);
            if (uses <= 1 && !write)
            {
                return BuildOutput(index, nodeId, pinId, block);
            }

            if (block.Locals.TryGetValue((nodeId, pinId), out var existing))
            {
                return new IrVariableGet(existing);
            }

            string local = UniqueName(LuaLiteral.Identifier(pinId));
            var expression = BuildOutput(index, nodeId, pinId, block);
            block.Statements.Add(new IrLocal(local, expression));
            block.Locals[(nodeId, pinId)] = local;
            return new IrVariableGet(local);
        }

        IrExpr BuildOutput(GraphIndex index, string nodeId, string pinId, BlockContext block)
        {
            var node = index.Nodes[nodeId];
            if (events.TryGetValue(nodeId, out var eventContext)
                && eventContext.ParamNames.TryGetValue(pinId, out var parameterName))
            {
                return new IrVariableGet(parameterName);
            }

            if (host.Capability(node.Kind) is { } capability)
            {
                var binding = host.CapabilityBinding(capability.Id)
                    ?? throw new InvalidOperationException($"Capability '{capability.Id}' has no host binding.");
                IrExpr? receiver = capability.Receivers.Length > 0 ? ResolveInput(index, nodeId, "self", block) : null;
                bool nullable = capability.Receivers.Length > 0 && IsNullableSource(index, nodeId, "self");
                return new IrCall(receiver, binding.Call, ResolveArgs(index, node, capability, binding, block),
                    binding.Receiver, nullable);
            }

            return node.Kind switch
            {
                ScriptBuiltinCatalog.VariableGet => new IrVariableGet(
                    $"__vars.{variableNames.GetValueOrDefault(ScriptPins.PropertyString(node, "variable") ?? "", "")}"),
                ScriptBuiltinCatalog.Equal => Binary("==", index, nodeId, block),
                ScriptBuiltinCatalog.NotEqual => Binary("~=", index, nodeId, block),
                ScriptBuiltinCatalog.Less => Binary("<", index, nodeId, block),
                ScriptBuiltinCatalog.LessEqual => Binary("<=", index, nodeId, block),
                ScriptBuiltinCatalog.Greater => Binary(">", index, nodeId, block),
                ScriptBuiltinCatalog.GreaterEqual => Binary(">=", index, nodeId, block),
                ScriptBuiltinCatalog.And => Binary("and", index, nodeId, block),
                ScriptBuiltinCatalog.Or => Binary("or", index, nodeId, block),
                ScriptBuiltinCatalog.Not => new IrUnary("not", ResolveInput(index, nodeId, "value", block)),
                "math.add" => Binary("+", index, nodeId, block),
                "math.subtract" => Binary("-", index, nodeId, block),
                "math.multiply" => Binary("*", index, nodeId, block),
                "math.divide" => Binary("/", index, nodeId, block),
                "math.modulo" => Binary("%", index, nodeId, block),
                "math.min" => MathCall("math.min", index, nodeId, block),
                "math.max" => MathCall("math.max", index, nodeId, block),
                "string.concat" => Binary("..", index, nodeId, block),
                "string.length" => new IrUnary("#", ResolveInput(index, nodeId, "text", block)),
                _ => new IrLiteral(ScriptValueType.Any, null)
            };
        }

        IrExpr Binary(string op, GraphIndex index, string nodeId, BlockContext block) =>
            new IrBinary(op, ResolveInput(index, nodeId, "a", block), ResolveInput(index, nodeId, "b", block));

        IrExpr MathCall(string call, GraphIndex index, string nodeId, BlockContext block) =>
            new IrCall(null, call,
                [ResolveInput(index, nodeId, "a", block), ResolveInput(index, nodeId, "b", block)],
                ScriptReceiverStyle.Global);

        bool IsNullableSource(GraphIndex index, string nodeId, string pinId) =>
            index.DataTo.TryGetValue((nodeId, pinId), out var source)
            && index.Pins[source.Node].TryGetValue(source.Pin, out var pin)
            && pin.Nullable;

        bool IsWrite(ScriptNode node) =>
            host.Capability(node.Kind)?.Effect == ScriptEffect.Write
            || node.Kind == ScriptBuiltinCatalog.VariableSet;

        IrLiteral Literal(JsonElement value, ScriptValueType type)
        {
            object? parsed = value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number when type == ScriptValueType.Float => value.GetDouble(),
                JsonValueKind.Number when value.TryGetInt64(out long integer) => integer,
                JsonValueKind.Number => value.GetDouble(),
                _ => null
            };
            return new IrLiteral(type, parsed);
        }

        static IrLiteral DefaultLiteral(string text, ScriptValueType type)
        {
            object? value = text switch
            {
                "nil" => null,
                "true" => true,
                "false" => false,
                _ when type == ScriptValueType.String => text,
                _ when long.TryParse(text, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out long integer) => integer,
                _ when type == ScriptValueType.Float && double.TryParse(text,
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number) => number,
                _ => null
            };
            return new IrLiteral(type, value);
        }

        string UniqueFrom(string name, HashSet<string> used)
        {
            string candidate = LuaLiteral.Identifier(name);
            int suffix = 2;
            while (!used.Add(candidate))
            {
                candidate = LuaLiteral.Identifier(name) + "_" + suffix;
                suffix++;
            }

            return candidate;
        }

        string UniqueName(string name)
        {
            string candidate = name;
            int suffix = 2;
            while (!usedNames.Add(candidate))
            {
                candidate = name + "_" + suffix;
                suffix++;
            }

            return candidate;
        }

        sealed record EventContext(
            ScriptNode Node,
            string Name,
            IReadOnlyDictionary<string, string> ParamNames,
            ScriptEventDefinition Definition);

        sealed class BlockContext
        {
            public BlockContext(GraphIndex index)
            {
                Index = index;
            }

            public GraphIndex Index { get; }
            public List<IrStmt> Statements { get; } = [];
            public Dictionary<(string Node, string Pin), string> Locals { get; } = new();

            /// <summary>Loop indices and function parameters available as bare Lua names.</summary>
            public Dictionary<(string Node, string Pin), string> Bindings { get; } = new();

            public BlockContext Fork()
            {
                var fork = new BlockContext(Index);
                foreach (var (key, value) in Locals)
                {
                    fork.Locals[key] = value;
                }

                foreach (var (key, value) in Bindings)
                {
                    fork.Bindings[key] = value;
                }

                return fork;
            }
        }

        sealed class GraphIndex
        {
            public GraphIndex(ScriptDocument document, ScriptHostProfile host, ScriptGraph graph)
            {
                Graph = graph;
                Nodes = graph.Nodes
                    .GroupBy(node => node.Id, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                Pins = graph.Nodes.ToDictionary(
                    node => node.Id,
                    node => ScriptPins.Build(document, host, node)
                        .GroupBy(pin => pin.Id, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal),
                    StringComparer.Ordinal);

                foreach (var edge in graph.Edges)
                {
                    if (!Pins.TryGetValue(edge.From.Node, out var fromPins)
                        || !Pins.TryGetValue(edge.To.Node, out var toPins)
                        || !fromPins.TryGetValue(edge.From.Pin, out var fromPin)
                        || !toPins.TryGetValue(edge.To.Pin, out var toPin)
                        || fromPin.Direction != ScriptPinDirection.Out)
                    {
                        continue;
                    }

                    if (fromPin.Type == ScriptValueType.Exec)
                    {
                        ExecTarget.TryAdd((edge.From.Node, edge.From.Pin), edge.To.Node);
                        ExecInputs.Add(edge.To.Node);
                    }
                    else
                    {
                        DataTo.TryAdd((edge.To.Node, edge.To.Pin), (edge.From.Node, edge.From.Pin));
                        UseCounts[(edge.From.Node, edge.From.Pin)] =
                            UseCounts.GetValueOrDefault((edge.From.Node, edge.From.Pin)) + 1;
                    }
                }
            }

            public ScriptGraph Graph { get; }
            public Dictionary<string, ScriptNode> Nodes { get; }
            public Dictionary<string, Dictionary<string, ScriptPin>> Pins { get; }
            public Dictionary<(string Node, string Pin), (string Node, string Pin)> DataTo { get; } = new();
            public Dictionary<(string Node, string Pin), string> ExecTarget { get; } = new();
            public Dictionary<(string Node, string Pin), int> UseCounts { get; } = new();

            public bool HasExecInput(string nodeId) => ExecInputs.Contains(nodeId);

            public HashSet<string> ExecInputs { get; } = new(StringComparer.Ordinal);
        }
    }
}
