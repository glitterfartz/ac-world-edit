namespace ACWorldEdit.Foundation.Scripting;

/// <summary>
/// The embedded graph templates. <see cref="Load"/> returns the checked-in document unchanged (used
/// by golden tests); <see cref="Create"/> clones it with fresh ids for a new document.
/// </summary>
public static class ScriptTemplateLibrary
{
    public static IReadOnlyList<string> Names { get; } = ["blank", "command", "creature-death", "dungeon-encounter", "quest-giver", "spell-cast", "world-event"];

    public static ScriptDocument Load(string name)
    {
        string suffix = $"Scripting.Templates.{name}.acwscript.json";
        var assembly = typeof(ScriptTemplateLibrary).Assembly;
        var matches = assembly.GetManifestResourceNames()
            .Where(resource => resource.EndsWith(suffix, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new ScriptCatalogException($"Expected one template resource ending with '{suffix}', found {matches.Length}.");
        }

        using var stream = assembly.GetManifestResourceStream(matches[0])
            ?? throw new ScriptCatalogException($"Template '{name}' is missing.");
        using var reader = new StreamReader(stream);
        return ScriptDocumentJson.Load(reader.ReadToEnd());
    }

    public static ScriptDocument Create(string name)
    {
        var document = Load(name);
        document.Id = Guid.NewGuid().ToString("N");
        Remap(document);
        return document;
    }

    static void Remap(ScriptDocument document)
    {
        var nodes = new Dictionary<string, string>(StringComparer.Ordinal);
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        var functions = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var variable in document.Variables)
        {
            string id = FreshId();
            variables[variable.Id] = id;
            variable.Id = id;
        }

        foreach (var function in document.Functions)
        {
            string id = FreshId();
            functions[function.Id] = id;
            function.Id = id;
            foreach (var parameter in function.Parameters)
            {
                parameter.Id = FreshId();
            }

            RemapGraph(function.Graph, nodes, variables, functions);
        }

        RemapGraph(document.Graph, nodes, variables, functions);
    }

    static void RemapGraph(
        ScriptGraph graph,
        Dictionary<string, string> nodes,
        Dictionary<string, string> variables,
        Dictionary<string, string> functions)
    {
        foreach (var node in graph.Nodes)
        {
            string id = FreshId();
            nodes[node.Id] = id;
            node.Id = id;
        }

        foreach (var node in graph.Nodes)
        {
            if (node.Properties.TryGetValue("variable", out var variable)
                && variable.ValueKind == System.Text.Json.JsonValueKind.String
                && variables.TryGetValue(variable.GetString() ?? "", out var mappedVariable))
            {
                node.Properties["variable"] = System.Text.Json.JsonSerializer.SerializeToElement(mappedVariable);
            }

            if (node.Properties.TryGetValue("function", out var function)
                && function.ValueKind == System.Text.Json.JsonValueKind.String
                && functions.TryGetValue(function.GetString() ?? "", out var mappedFunction))
            {
                node.Properties["function"] = System.Text.Json.JsonSerializer.SerializeToElement(mappedFunction);
            }
        }

        foreach (var edge in graph.Edges)
        {
            edge.Id = FreshId();
            edge.From.Node = nodes.GetValueOrDefault(edge.From.Node, edge.From.Node);
            edge.To.Node = nodes.GetValueOrDefault(edge.To.Node, edge.To.Node);
        }

        foreach (var comment in graph.Comments)
        {
            comment.Id = FreshId();
        }
    }

    static string FreshId() => "n" + Guid.NewGuid().ToString("N")[..10];
}
