using ACWorldEdit.Foundation.Scripting;
using Xunit;

/// <summary>
/// The embedded ALE profile and the loader's integrity rules: unique ids, bindings that reference
/// declared parameters, every implemented capability bound, every event registered, and host packs
/// that cannot smuggle in pin or type definitions.
/// </summary>
public class ScriptCatalogTests
{
    const string EmptyArray = "[]";
    const string EmptyObject = "{}";
    const string Host = """{ "host": "test-host", "label": "Test host", "enums": [] }""";
    const string Event = """
    [{ "id": "event.test", "display": "Test", "category": "Test", "scopeParams": [], "params": [],
       "returns": [], "availability": "implemented", "performance": "normal" }]
    """;
    const string EventBinding = """
    { "event.test": { "register": "RegisterTestEvent", "scopeArgs": [], "eventId": 1,
                      "eventName": "TEST_EVENT", "handlerEventParam": "event", "returnDefaults": [] } }
    """;
    const string Capability = """
    [{ "id": "unit.test", "display": "Test", "category": "Unit", "receivers": ["Unit"], "params": [],
       "returns": [], "effect": "write", "tier": "core", "availability": "implemented", "performance": "normal" }]
    """;
    const string CapabilityBinding = """
    { "unit.test": { "call": "Test", "receiver": "colon", "args": [], "returns": "none" } }
    """;

    static ScriptHostProfile Build(string capabilities = Capability, string capabilityBindings = CapabilityBinding) =>
        ScriptCatalogLoader.Build(Host, capabilities, Event, EmptyObject, capabilityBindings, EventBinding);

    [Fact]
    public void EmbeddedAleProfileLoads()
    {
        var profile = AcScriptHosts.Profile;

        Assert.Equal("ale-azerothcore", profile.Host);
        Assert.Equal("ALE (AzerothCore)", profile.Label);
        Assert.True(profile.Capabilities.Count >= 20);
        Assert.True(profile.Events.Count >= 200);
        Assert.Equal("5.1-compatible", profile.Conventions.LuaVersion);
        Assert.Equal("RegisterEvent", profile.Conventions.ObjectTimer.Register);
        Assert.NotNull(profile.Enum("PowerType"));
        Assert.NotNull(profile.Enum("QuestStatus"));
    }

    [Fact]
    public void CastSpellBindingUsesTheAleArgumentOrder()
    {
        var binding = AcScriptHosts.Profile.CapabilityBinding("unit.cast_spell")!;

        Assert.Equal("CastSpell", binding.Call);
        Assert.Equal(ScriptReceiverStyle.Colon, binding.Receiver);
        Assert.Equal(["target", "spell", "triggered"], binding.Args.Select(argument => argument.Param));
        Assert.Equal("false", binding.Args[2].DefaultLiteral);
    }

    [Fact]
    public void EveryImplementedCapabilityHasABinding()
    {
        var profile = AcScriptHosts.Profile;
        foreach (var capability in profile.Capabilities.Values.Where(c => c.Availability == ScriptAvailability.Implemented))
        {
            Assert.True(profile.CapabilityBindings.ContainsKey(capability.Id), capability.Id);
        }
    }

    [Fact]
    public void EveryEventHasANumericIdAndRegistration()
    {
        var profile = AcScriptHosts.Profile;
        foreach (var (id, binding) in profile.EventBindings)
        {
            Assert.True(binding.EventId > 0, id);
            Assert.False(string.IsNullOrWhiteSpace(binding.Register), id);
            Assert.False(string.IsNullOrWhiteSpace(binding.EventName), id);
        }
    }

    [Fact]
    public void BindingArgumentsReferenceDeclaredParameters()
    {
        var profile = AcScriptHosts.Profile;
        foreach (var (id, capability) in profile.Capabilities)
        {
            var declared = capability.Params.Select(parameter => parameter.Name).ToHashSet();
            var binding = profile.CapabilityBinding(id);
            if (binding is null)
            {
                continue;
            }

            foreach (var argument in binding.Args)
            {
                Assert.Contains(argument.Param, declared);
            }
        }
    }

    [Fact]
    public void DuplicateCapabilityIdsAreRejected()
    {
        const string duplicated = """
        [
          { "id": "unit.test", "display": "Test", "category": "Unit", "receivers": ["Unit"], "params": [],
            "returns": [], "effect": "write", "tier": "core", "availability": "implemented", "performance": "normal" },
          { "id": "unit.test", "display": "Again", "category": "Unit", "receivers": ["Unit"], "params": [],
            "returns": [], "effect": "write", "tier": "core", "availability": "implemented", "performance": "normal" }
        ]
        """;
        var error = Assert.Throws<ScriptCatalogException>(() => Build(duplicated));
        Assert.Contains("Duplicate capability id", error.Message);
    }

    [Fact]
    public void ImplementedCapabilityWithoutABindingIsRejected()
    {
        var error = Assert.Throws<ScriptCatalogException>(() => Build(capabilityBindings: "{}"));
        Assert.Contains("has no host binding", error.Message);
    }

    [Fact]
    public void HostPackCannotRedefinePinsOrTypes()
    {
        const string withTypes = """
        { "unit.test": { "call": "Test", "receiver": "colon", "args": [], "returns": "none",
                         "params": [{ "name": "spell", "type": "int" }] } }
        """;
        var error = Assert.Throws<ScriptCatalogException>(() => Build(capabilityBindings: withTypes));
        Assert.Contains("unit.test", error.Message);
    }

    [Fact]
    public void EventScopeArgumentsMustMatchTheDeclaredScopes()
    {
        const string scopedEvent = """
        [{ "id": "event.test", "display": "Test", "category": "Test",
           "scopeParams": [{ "name": "entry", "type": "CreatureEntry" }], "params": [],
           "returns": [], "availability": "implemented", "performance": "normal" }]
        """;
        var error = Assert.Throws<ScriptCatalogException>(() =>
            ScriptCatalogLoader.Build(Host, Capability, scopedEvent, EmptyObject, CapabilityBinding, EventBinding));
        Assert.Contains("scope arguments", error.Message);
    }

    [Fact]
    public void UnknownHostFieldsAreRejected()
    {
        const string badHost = """{ "host": "test-host", "label": "Test host", "enums": [], "surprise": 1 }""";
        Assert.Throws<ScriptCatalogException>(() =>
            ScriptCatalogLoader.Build(badHost, Capability, Event, EmptyObject, CapabilityBinding, EventBinding));
    }

    [Fact]
    public void EmbeddedElunaProfileLoads()
    {
        var profile = TcScriptHosts.Profile;

        Assert.Equal("eluna-trinitycore", profile.Host);
        Assert.Equal("Eluna (TrinityCore)", profile.Label);
        Assert.Equal(230, profile.Events.Count);
        Assert.Equal(25, profile.Capabilities.Count);
        Assert.True(TcScriptHosts.Info.Experimental);
        Assert.Same(profile, TcScriptHosts.Profile);
    }

    [Fact]
    public void TheSameGraphCompilesUnderBothHosts()
    {
        var document = ScriptTemplateLibrary.Load("creature-death");

        var ale = new ScriptCompiler(AcScriptHosts.Profile).Compile(document, "creature-death.acwscript");
        var eluna = new ScriptCompiler(TcScriptHosts.Profile).Compile(document, "creature-death.acwscript");

        Assert.True(ale.Success, string.Join("; ", ale.Errors.Select(error => error.Message)));
        Assert.True(eluna.Success, string.Join("; ", eluna.Errors.Select(error => error.Message)));
        Assert.Contains("RegisterCreatureEvent(1234, 4", ale.LuaText);
        Assert.Contains("RegisterCreatureEvent(1234, 4", eluna.LuaText);
        Assert.NotEqual(ale.CatalogHash, eluna.CatalogHash);
    }

    [Fact]
    public void BothHostsAreNamedByTheRegistry()
    {
        Assert.Same(AcScriptHosts.Profile, ScriptHostRegistry.Profile(AcScriptHosts.Id));
        Assert.Same(TcScriptHosts.Profile, ScriptHostRegistry.Profile(TcScriptHosts.Id));
        Assert.Null(ScriptHostRegistry.Profile("missing-host"));
    }

    [Fact]
    public void ProfileLoadIsDeterministic()
    {
        var first = AcScriptHosts.Profile;
        var second = ScriptCatalogLoader.LoadAleAzerothCore();

        Assert.Equal(first.Capabilities.Keys.OrderBy(k => k), second.Capabilities.Keys.OrderBy(k => k));
        Assert.Equal(first.Events.Keys.OrderBy(k => k), second.Events.Keys.OrderBy(k => k));
    }
}
