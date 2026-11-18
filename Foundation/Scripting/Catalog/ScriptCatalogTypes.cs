using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACWorldEdit.Foundation.Scripting;

public enum ScriptEffect
{
    Pure,
    Read,
    Write
}

public enum ScriptTier
{
    Core,
    Standard,
    Advanced,
    Hidden
}

public enum ScriptAvailability
{
    Implemented,
    Unimplemented
}

public enum ScriptPerformance
{
    Normal,
    HighFrequency,
    Blocking
}

public enum ScriptReceiverStyle
{
    Colon,
    Dot,
    None,
    Global
}

/// <summary>One parameter or return slot of a capability or event. Defaults are Lua literals.</summary>
public sealed class ScriptParamDef
{
    public string Name { get; init; } = "";
    public ScriptValueType Type { get; init; }
    public bool Optional { get; init; }
    public string? Default { get; init; }
    public bool Nullable { get; init; }
    public string? Units { get; init; }
}

/// <summary>A semantic operation. A host pack binds it to a call; the document only names its id.</summary>
public sealed class ScriptCapability
{
    public string Id { get; init; } = "";
    public string Display { get; init; } = "";
    public string Category { get; init; } = "";
    public ScriptValueType[] Receivers { get; init; } = [];
    public ScriptParamDef[] Params { get; init; } = [];
    public ScriptParamDef[] Returns { get; init; } = [];
    public ScriptEffect Effect { get; init; }
    public ScriptTier Tier { get; init; }
    public ScriptAvailability Availability { get; init; }
    public ScriptPerformance Performance { get; init; }
    public string? Doc { get; init; }
}

/// <summary>A scope value inserted into the registration call before the event id.</summary>
public sealed class ScriptScopeParam
{
    public string Name { get; init; } = "";
    public ScriptValueType Type { get; init; }
    public bool Nullable { get; init; }
}

/// <summary>A semantic event: scope, handler parameters and declared return slots.</summary>
public sealed class ScriptEventDefinition
{
    public string Id { get; init; } = "";
    public string Display { get; init; } = "";
    public string Category { get; init; } = "";
    public ScriptScopeParam[] ScopeParams { get; init; } = [];
    public ScriptParamDef[] Params { get; init; } = [];
    public ScriptParamDef[] Returns { get; init; } = [];
    public ScriptAvailability Availability { get; init; }
    public ScriptPerformance Performance { get; init; }
    public string? Doc { get; init; }
}

public sealed class ScriptArgBinding
{
    public string Param { get; init; } = "";
    public string? DefaultLiteral { get; init; }
}

public sealed class ScriptCapabilityBinding
{
    public string Call { get; init; } = "";
    public ScriptReceiverStyle Receiver { get; init; }
    public ScriptArgBinding[] Args { get; init; } = [];
    public string Returns { get; init; } = "none";
}

public sealed class ScriptEventBinding
{
    public string Register { get; init; } = "";
    public string[] ScopeArgs { get; init; } = [];
    public int EventId { get; init; }
    public string EventName { get; init; } = "";
    public string HandlerEventParam { get; init; } = "event";
    public string[] ReturnDefaults { get; init; } = [];
}

public sealed class ScriptEnumValue
{
    public string Name { get; init; } = "";
    public long Value { get; init; }
}

public sealed class ScriptEnumDefinition
{
    public string Name { get; init; } = "";
    public ScriptEnumValue[] Values { get; init; } = [];
}

public sealed class ScriptTimerConvention
{
    public string Register { get; init; } = "";
    public string[] CallbackArgs { get; init; } = [];
}

public sealed class ScriptAsyncQuery
{
    public string Call { get; init; } = "";
    public string CallbackArg { get; init; } = "";
}

public sealed class ScriptConventions
{
    public string LuaVersion { get; init; } = "5.1-compatible";
    public ScriptTimerConvention ObjectTimer { get; init; } = new();
    public ScriptTimerConvention GlobalTimer { get; init; } = new();
    public ScriptAsyncQuery? AsyncQuery { get; init; }
    public string[] Caveats { get; init; } = [];
}

/// <summary>The host pack's identity, conventions and any host-only enum values.</summary>
public sealed class ScriptHostManifest
{
    public string Host { get; init; } = "";
    public string Label { get; init; } = "";
    public ScriptConventions Conventions { get; init; } = new();
    public ScriptEnumDefinition[] Enums { get; init; } = [];
}

/// <summary>A catalog or host pack that does not satisfy the loader's integrity rules.</summary>
public sealed class ScriptCatalogException : Exception
{
    public ScriptCatalogException(string message) : base(message)
    {
    }
}

/// <summary>JSON options for catalog and host packs: strict fields, stable enum spellings.</summary>
public static class ScriptCatalogJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new ScriptValueTypeJsonConverter(),
            new LowerEnumJsonConverter<ScriptEffect>(),
            new LowerEnumJsonConverter<ScriptTier>(),
            new LowerEnumJsonConverter<ScriptAvailability>(),
            new LowerEnumJsonConverter<ScriptPerformance>(),
            new LowerEnumJsonConverter<ScriptReceiverStyle>()
        }
    };
}

/// <summary>Reads and writes the lowercase-with-hyphens enum spelling used by the packs.</summary>
public sealed class LowerEnumJsonConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string text = (reader.GetString() ?? "").Replace("-", "");
        return Enum.TryParse<T>(text, ignoreCase: true, out var value)
            ? value
            : throw new JsonException($"Unknown {typeof(T).Name} value '{reader.GetString()}'.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        string text = value.ToString();
        if (value is ScriptPerformance.HighFrequency)
        {
            text = "high-frequency";
        }

        writer.WriteStringValue(char.ToLowerInvariant(text[0]) + text[1..]);
    }
}

/// <summary>Maps <see cref="ScriptValueType"/> to and from its stable JSON spelling.</summary>
public sealed class ScriptValueTypeJsonConverter : JsonConverter<ScriptValueType>
{
    public override ScriptValueType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ScriptValueTypeNames.Parse(reader.GetString() ?? throw new JsonException("A script type must be a string."));

    public override void Write(Utf8JsonWriter writer, ScriptValueType value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ScriptValueTypeNames.ToJson(value));
}
