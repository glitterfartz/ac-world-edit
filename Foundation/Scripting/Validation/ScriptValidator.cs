namespace ACWorldEdit.Foundation.Scripting;

/// <summary>
/// Validates a document against its host profile before compilation. Errors block export; warnings
/// and info are reported but do not. Codes are stable and listed in <see cref="ScriptDiagnosticCodes"/>.
/// </summary>
public sealed partial class ScriptValidator
{
    readonly ScriptHostProfile host;

    public ScriptValidator(ScriptHostProfile host)
    {
        this.host = host;
    }

    public IReadOnlyList<ScriptDiagnostic> Validate(ScriptDocument document)
    {
        var diagnostics = new List<ScriptDiagnostic>();
        var variables = ValidateVariables(document, diagnostics);
        var functions = ValidateFunctions(document, diagnostics);

        ValidateGraph(document, document.Graph, null, variables, functions, diagnostics);
        foreach (var function in document.Functions)
        {
            ValidateGraph(document, function.Graph, function.Returns, variables, functions, diagnostics);
        }

        ValidateFunctionCycles(document, diagnostics);
        ValidateLocals(document, diagnostics);

        return diagnostics
            .OrderBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.NodeId, StringComparer.Ordinal)
            .ToList();
    }

    Dictionary<string, ScriptVariable> ValidateVariables(ScriptDocument document, List<ScriptDiagnostic> diagnostics)
    {
        var byId = new Dictionary<string, ScriptVariable>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variable in document.Variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Id) || !byId.TryAdd(variable.Id, variable))
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.DuplicateName, $"Variable id '{variable.Id}' is blank or duplicated."));
            }

            if (string.IsNullOrWhiteSpace(variable.Name) || !names.Add(variable.Name))
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.DuplicateName, $"Variable name '{variable.Name}' is blank or duplicated."));
            }

            if (!ScriptTypeRegistry.Default.CanStoreInVariable(variable.Type))
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.ObjectVariable,
                    $"Variable '{variable.Name}' uses {ScriptValueTypeNames.ToJson(variable.Type)}; module variables hold only values. Store a GUID or id instead."));
            }

            if (variable.Default is { } value)
            {
                if (!ScriptTypeChecker.LiteralMatches(value, variable.Type, nullable: false))
                {
                    diagnostics.Add(Error(ScriptDiagnosticCodes.VariableDefault,
                        $"Variable '{variable.Name}' has a {ScriptTypeChecker.KindName(value)} default; expected {ScriptValueTypeNames.ToJson(variable.Type)}."));
                }

                if (ScriptTypeChecker.IsLargeInteger(value))
                {
                    diagnostics.Add(Warning(ScriptDiagnosticCodes.LargeInteger,
                        $"Variable '{variable.Name}' has an integer above 2^53, which loses precision in Lua."));
                }
            }
        }

        return byId;
    }

    static Dictionary<string, ScriptFunction> ValidateFunctions(ScriptDocument document, List<ScriptDiagnostic> diagnostics)
    {
        var byId = new Dictionary<string, ScriptFunction>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in document.Functions)
        {
            if (string.IsNullOrWhiteSpace(function.Id) || !byId.TryAdd(function.Id, function))
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.DuplicateName, $"Function id '{function.Id}' is blank or duplicated."));
            }

            if (string.IsNullOrWhiteSpace(function.Name) || !names.Add(function.Name))
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.DuplicateName, $"Function name '{function.Name}' is blank or duplicated."));
            }

            if (function.Parameters.Select(parameter => parameter.Name).Distinct(StringComparer.Ordinal).Count() != function.Parameters.Count)
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.DuplicateName, $"Function '{function.Name}' has duplicate parameter names."));
            }

            if (function.Returns.Select(slot => slot.Name).Distinct(StringComparer.Ordinal).Count() != function.Returns.Count)
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.DuplicateName, $"Function '{function.Name}' has duplicate return names."));
            }
        }

        return byId;
    }

    static void ValidateFunctionCycles(ScriptDocument document, List<ScriptDiagnostic> diagnostics)
    {
        var calls = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var function in document.Functions)
        {
            var targets = calls.TryGetValue(function.Id, out var existing) ? existing : calls[function.Id] = [];
            AddCalls(function.Graph, targets);
        }

        AddCalls(document.Graph, null);

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in document.Functions)
        {
            if (VisitFunction(function.Id, calls, visiting, visited))
            {
                diagnostics.Add(Error(ScriptDiagnosticCodes.Recursion,
                    $"Function '{function.Name}' calls itself directly or through another function; recursion is not supported yet."));
            }
        }
    }

    static void AddCalls(ScriptGraph graph, HashSet<string>? targets)
    {
        foreach (var node in graph.Nodes.Where(node => node.Kind == ScriptBuiltinCatalog.CallFunction))
        {
            if (ScriptPins.PropertyString(node, "function") is { } id)
            {
                targets?.Add(id);
            }
        }
    }

    static bool VisitFunction(string id, Dictionary<string, HashSet<string>> calls, HashSet<string> visiting, HashSet<string> visited)
    {
        if (visited.Contains(id))
        {
            return false;
        }

        if (!visiting.Add(id))
        {
            return true;
        }

        bool cycle = calls.TryGetValue(id, out var targets)
            && targets.Any(target => VisitFunction(target, calls, visiting, visited));
        visiting.Remove(id);
        visited.Add(id);
        return cycle;
    }

    void ValidateLocals(ScriptDocument document, List<ScriptDiagnostic> diagnostics)
    {
        int locals = document.Variables.Count + document.Functions.Count;
        var graphs = document.Functions.Select(function => function.Graph).Append(document.Graph);
        foreach (var graph in graphs)
        {
            var uses = graph.Edges
                .GroupBy(edge => (edge.From.Node, edge.From.Pin))
                .ToDictionary(group => group.Key, group => group.Count());
            foreach (var node in graph.Nodes)
            {
                bool write = host.Capability(node.Kind)?.Effect == ScriptEffect.Write
                    || node.Kind is ScriptBuiltinCatalog.VariableSet or ScriptBuiltinCatalog.CallFunction;
                if (write)
                {
                    locals++;
                }

                if (uses.Any(use => use.Key.Node == node.Id && use.Value > 1))
                {
                    locals++;
                }
            }
        }

        if (locals > 180)
        {
            diagnostics.Add(Error(ScriptDiagnosticCodes.TooManyLocals,
                $"The document would need about {locals} Lua locals; the limit is 180. Split it into user functions."));
        }
    }

    static ScriptDiagnostic Error(string code, string message, string? nodeId = null, string? pinId = null, string? edgeId = null) =>
        new(code, ScriptSeverity.Error, message, nodeId, pinId, edgeId);

    static ScriptDiagnostic Warning(string code, string message, string? nodeId = null, string? pinId = null, string? edgeId = null) =>
        new(code, ScriptSeverity.Warning, message, nodeId, pinId, edgeId);

    static ScriptDiagnostic Info(string code, string message, string? nodeId = null, string? pinId = null, string? edgeId = null) =>
        new(code, ScriptSeverity.Info, message, nodeId, pinId, edgeId);
}
