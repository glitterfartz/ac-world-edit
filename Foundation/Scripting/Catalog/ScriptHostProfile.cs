namespace ACWorldEdit.Foundation.Scripting;

/// <summary>
/// The merged catalog for one host: semantic capabilities and events from the shared core, plus
/// that host's bindings, enums and conventions. The compiler and validator never look anywhere else.
/// </summary>
public sealed class ScriptHostProfile
{
    public ScriptHostProfile(
        string host,
        string label,
        IReadOnlyDictionary<string, ScriptCapability> capabilities,
        IReadOnlyDictionary<string, ScriptEventDefinition> events,
        IReadOnlyDictionary<string, ScriptCapabilityBinding> capabilityBindings,
        IReadOnlyDictionary<string, ScriptEventBinding> eventBindings,
        IReadOnlyDictionary<string, ScriptEnumDefinition> enums,
        ScriptConventions conventions)
    {
        Host = host;
        Label = label;
        Capabilities = capabilities;
        Events = events;
        CapabilityBindings = capabilityBindings;
        EventBindings = eventBindings;
        Enums = enums;
        Conventions = conventions;
    }

    public string Host { get; }
    public string Label { get; }
    public IReadOnlyDictionary<string, ScriptCapability> Capabilities { get; }
    public IReadOnlyDictionary<string, ScriptEventDefinition> Events { get; }
    public IReadOnlyDictionary<string, ScriptCapabilityBinding> CapabilityBindings { get; }
    public IReadOnlyDictionary<string, ScriptEventBinding> EventBindings { get; }
    public IReadOnlyDictionary<string, ScriptEnumDefinition> Enums { get; }
    public ScriptConventions Conventions { get; }

    public ScriptCapability? Capability(string id) => Capabilities.GetValueOrDefault(id);

    public ScriptEventDefinition? Event(string id) => Events.GetValueOrDefault(id);

    public ScriptCapabilityBinding? CapabilityBinding(string id) => CapabilityBindings.GetValueOrDefault(id);

    public ScriptEventBinding? EventBinding(string id) => EventBindings.GetValueOrDefault(id);

    public ScriptEnumDefinition? Enum(string name) => Enums.GetValueOrDefault(name);
}

/// <summary>A host the UI may list; <paramref name="Experimental"/> hosts are not yet complete.</summary>
public sealed record ScriptHostInfo(string Id, string Label, bool Experimental = false);

/// <summary>The one place that names every host. Dialect details live in their folders.</summary>
public static class ScriptHostRegistry
{
    public static IReadOnlyList<ScriptHostInfo> Hosts { get; } = [AcScriptHosts.Info, TcScriptHosts.Info];

    public static ScriptHostProfile? Profile(string host) => host switch
    {
        AcScriptHosts.Id => AcScriptHosts.Profile,
        TcScriptHosts.Id => TcScriptHosts.Profile,
        _ => null
    };
}
