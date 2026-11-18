using System.Text.Json;

namespace ACWorldEdit.Foundation.Scripting;

/// <summary>
/// Loads the embedded semantic core and one host pack, then enforces the integrity rules: unique
/// ids, bindings that reference declared parameters, every implemented capability bound, every event
/// registered, and host packs that cannot redefine pins, parameter types or return slots.
/// </summary>
public static class ScriptCatalogLoader
{
    public static ScriptHostProfile LoadAleAzerothCore() => Build(
        ReadResource("Profiles/ale-azerothcore/host.json"),
        ReadResource("Profiles/core/capabilities.json"),
        ReadResource("Profiles/core/events.json"),
        ReadResource("Profiles/core/enums.json"),
        ReadResource("Profiles/ale-azerothcore/capability-bindings.json"),
        ReadResource("Profiles/ale-azerothcore/event-bindings.json"));

    /// <summary>
    /// The Eluna (TrinityCore) proof profile. Its bindings mirror Eluna's Lua API; the profile is
    /// marked experimental until a live TrinityCore server verifies the registration ids.
    /// </summary>
    public static ScriptHostProfile LoadElunaTrinityCore() => Build(
        ReadResource("Profiles/eluna-trinitycore/host.json"),
        ReadResource("Profiles/core/capabilities.json"),
        ReadResource("Profiles/core/events.json"),
        ReadResource("Profiles/core/enums.json"),
        ReadResource("Profiles/eluna-trinitycore/capability-bindings.json"),
        ReadResource("Profiles/eluna-trinitycore/event-bindings.json"));

    /// <summary>Build a profile from JSON strings; the embedded loader and the integrity tests share it.</summary>
    public static ScriptHostProfile Build(
        string hostJson,
        string capabilitiesJson,
        string eventsJson,
        string enumsJson,
        string capabilityBindingsJson,
        string eventBindingsJson)
    {
        var manifest = Parse<ScriptHostManifest>(hostJson, "host.json");
        var capabilities = Parse<ScriptCapability[]>(capabilitiesJson, "capabilities.json");
        var events = Parse<ScriptEventDefinition[]>(eventsJson, "events.json");
        var coreEnums = Parse<Dictionary<string, ScriptEnumDefinition>>(enumsJson, "enums.json");
        var capabilityBindings = Parse<Dictionary<string, ScriptCapabilityBinding>>(capabilityBindingsJson, "capability-bindings.json");
        var eventBindings = Parse<Dictionary<string, ScriptEventBinding>>(eventBindingsJson, "event-bindings.json");

        if (string.IsNullOrWhiteSpace(manifest.Host))
        {
            throw new ScriptCatalogException("The host manifest needs a host id.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Label))
        {
            throw new ScriptCatalogException("The host manifest needs a label.");
        }

        var byCapability = Unique(capabilities, capability => capability.Id, "capability");
        var byEvent = Unique(events, definition => definition.Id, "event");
        var enums = MergeEnums(coreEnums, manifest.Enums);

        foreach (var (id, binding) in capabilityBindings)
        {
            if (!byCapability.TryGetValue(id, out var capability))
            {
                throw new ScriptCatalogException($"The host binds unknown capability '{id}'.");
            }

            ValidateCapabilityBinding(capability, binding);
        }

        foreach (var capability in byCapability.Values)
        {
            if (capability.Availability == ScriptAvailability.Implemented && !capabilityBindings.ContainsKey(capability.Id))
            {
                throw new ScriptCatalogException($"Implemented capability '{capability.Id}' has no host binding.");
            }
        }

        foreach (var (id, binding) in eventBindings)
        {
            if (!byEvent.TryGetValue(id, out var definition))
            {
                throw new ScriptCatalogException($"The host binds unknown event '{id}'.");
            }

            ValidateEventBinding(definition, binding);
        }

        foreach (var definition in byEvent.Values)
        {
            if (!eventBindings.ContainsKey(definition.Id))
            {
                throw new ScriptCatalogException($"Event '{definition.Id}' has no host registration.");
            }
        }

        return new ScriptHostProfile(
            manifest.Host, manifest.Label, byCapability, byEvent,
            capabilityBindings, eventBindings, enums, manifest.Conventions);
    }

    static void ValidateCapabilityBinding(ScriptCapability capability, ScriptCapabilityBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.Call))
        {
            throw new ScriptCatalogException($"Capability '{capability.Id}' has an empty call name.");
        }

        bool hasReceiver = capability.Receivers.Length > 0;
        if (hasReceiver && binding.Receiver is not (ScriptReceiverStyle.Colon or ScriptReceiverStyle.Dot))
        {
            throw new ScriptCatalogException($"Capability '{capability.Id}' needs a colon or dot receiver.");
        }

        if (!hasReceiver && binding.Receiver is not (ScriptReceiverStyle.None or ScriptReceiverStyle.Global))
        {
            throw new ScriptCatalogException($"Capability '{capability.Id}' has no receiver but is bound with one.");
        }

        var declared = capability.Params.ToDictionary(param => param.Name, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var argument in binding.Args)
        {
            if (!declared.ContainsKey(argument.Param))
            {
                throw new ScriptCatalogException($"Capability '{capability.Id}' binds unknown parameter '{argument.Param}'.");
            }

            if (!seen.Add(argument.Param))
            {
                throw new ScriptCatalogException($"Capability '{capability.Id}' binds parameter '{argument.Param}' twice.");
            }
        }

        foreach (var required in capability.Params.Where(param => !param.Optional))
        {
            if (!seen.Contains(required.Name))
            {
                throw new ScriptCatalogException($"Capability '{capability.Id}' does not bind required parameter '{required.Name}'.");
            }
        }

        string expectedReturns = capability.Returns.Length switch
        {
            0 => "none",
            1 => "single",
            _ => "tuple"
        };
        if (binding.Returns != expectedReturns)
        {
            throw new ScriptCatalogException(
                $"Capability '{capability.Id}' declares {capability.Returns.Length} return(s) but the host says '{binding.Returns}'.");
        }
    }

    static void ValidateEventBinding(ScriptEventDefinition definition, ScriptEventBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.Register) || binding.EventId <= 0 || string.IsNullOrWhiteSpace(binding.EventName))
        {
            throw new ScriptCatalogException($"Event '{definition.Id}' needs a register, a positive event id and an event name.");
        }

        var scopes = definition.ScopeParams.Select(scope => scope.Name).ToHashSet(StringComparer.Ordinal);
        var bound = binding.ScopeArgs.ToHashSet(StringComparer.Ordinal);
        if (bound.Count != binding.ScopeArgs.Length || !bound.SetEquals(scopes))
        {
            throw new ScriptCatalogException(
                $"Event '{definition.Id}' scope arguments [{string.Join(", ", binding.ScopeArgs)}] do not match its scope parameters.");
        }

        if (binding.ReturnDefaults.Length != 0 && binding.ReturnDefaults.Length != definition.Returns.Length)
        {
            throw new ScriptCatalogException(
                $"Event '{definition.Id}' has {definition.Returns.Length} return slot(s) but {binding.ReturnDefaults.Length} defaults.");
        }
    }

    static IReadOnlyDictionary<string, ScriptEnumDefinition> MergeEnums(
        IReadOnlyDictionary<string, ScriptEnumDefinition> core,
        IReadOnlyList<ScriptEnumDefinition> host)
    {
        var merged = new Dictionary<string, ScriptEnumDefinition>(StringComparer.Ordinal);
        foreach (var definition in core.Values.Concat(host))
        {
            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                throw new ScriptCatalogException("An enum needs a name.");
            }

            if (!merged.TryAdd(definition.Name, definition))
            {
                throw new ScriptCatalogException($"Enum '{definition.Name}' is defined twice.");
            }

            ValidateEnum(definition);
        }

        return merged;
    }

    static void ValidateEnum(ScriptEnumDefinition definition)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var values = new HashSet<long>();
        foreach (var value in definition.Values)
        {
            if (string.IsNullOrWhiteSpace(value.Name) || !names.Add(value.Name) || !values.Add(value.Value))
            {
                throw new ScriptCatalogException($"Enum '{definition.Name}' has a blank or duplicate value '{value.Name}'.");
            }
        }
    }

    static Dictionary<string, T> Unique<T>(IEnumerable<T> items, Func<T, string> key, string kind)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            string id = key(item);
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ScriptCatalogException($"A {kind} has no id.");
            }

            if (!result.TryAdd(id, item))
            {
                throw new ScriptCatalogException($"Duplicate {kind} id '{id}'.");
            }
        }

        return result;
    }

    static T Parse<T>(string json, string source)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, ScriptCatalogJson.Options)
                ?? throw new ScriptCatalogException($"{source} is empty.");
        }
        catch (JsonException error)
        {
            string location = string.IsNullOrEmpty(error.Path) ? "" : $" (at {error.Path})";
            throw new ScriptCatalogException($"{source}: {error.Message}{location}");
        }
    }

    static string ReadResource(string relativePath)
    {
        // MSBuild turns path separators into dots and sanitizes directory hyphens to underscores
        // (`ale-azerothcore` becomes `ale_azerothcore`); a file name keeps its hyphens.
        string[] parts = relativePath.Split('/');
        string normalized = string.Join('.', parts.Select((part, index) =>
            index == parts.Length - 1 ? part : part.Replace('-', '_')));
        string suffix = "Scripting.Catalog." + normalized;
        var assembly = typeof(ScriptCatalogLoader).Assembly;
        var matches = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(suffix, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new ScriptCatalogException($"Expected one embedded resource ending with '{suffix}', found {matches.Length}.");
        }

        using var stream = assembly.GetManifestResourceStream(matches[0])
            ?? throw new ScriptCatalogException($"Embedded resource '{matches[0]}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
