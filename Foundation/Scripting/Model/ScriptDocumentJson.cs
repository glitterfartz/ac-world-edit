using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACWorldEdit.Foundation.Scripting;

/// <summary>
/// Reads and writes the `.acwscript` JSON shape. The canonical form sorts object keys and removes
/// whitespace, which gives the dirty-state hash and the deterministic compile input.
/// </summary>
public static class ScriptDocumentJson
{
    public const int CurrentFormat = 1;

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new ScriptValueTypeJsonConverter(),
            new ScriptPinDirectionJsonConverter()
        }
    };

    public static ScriptDocument Load(string json)
    {
        using var probe = JsonDocument.Parse(json);
        if (probe.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Invalid script document: the root must be an object.");
        }

        int format = probe.RootElement.TryGetProperty("format", out var formatElement)
            ? formatElement.GetInt32()
            : CurrentFormat;
        ScriptDocumentMigrations.EnsureSupported(format);

        var document = JsonSerializer.Deserialize<ScriptDocument>(json, Options)
            ?? throw new InvalidDataException("Invalid script document.");
        return ScriptDocumentMigrations.Migrate(document);
    }

    public static string Save(ScriptDocument document)
    {
        document.Format = CurrentFormat;
        return JsonSerializer.Serialize(document, Options);
    }

    /// <summary>Canonical JSON: sorted object keys, no insignificant whitespace, stable literals.</summary>
    public static string Canonical(ScriptDocument document)
    {
        var element = JsonSerializer.SerializeToElement(document, Options);
        var builder = new StringBuilder();
        WriteCanonical(element, builder);
        return builder.ToString();
    }

    public static string CanonicalHash(ScriptDocument document) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(document)))).ToLowerInvariant();

    static void WriteCanonical(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                bool first = true;
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    builder.Append(JsonSerializer.Serialize(property.Name));
                    builder.Append(':');
                    WriteCanonical(property.Value, builder);
                }

                builder.Append('}');
                break;

            case JsonValueKind.Array:
                builder.Append('[');
                bool firstItem = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (!firstItem)
                    {
                        builder.Append(',');
                    }

                    firstItem = false;
                    WriteCanonical(item, builder);
                }

                builder.Append(']');
                break;

            case JsonValueKind.String:
                builder.Append(JsonSerializer.Serialize(element.GetString()));
                break;

            case JsonValueKind.Number:
                builder.Append(element.GetRawText());
                break;

            case JsonValueKind.True:
                builder.Append("true");
                break;

            case JsonValueKind.False:
                builder.Append("false");
                break;

            default:
                builder.Append("null");
                break;
        }
    }

    sealed class ScriptPinDirectionJsonConverter : JsonConverter<ScriptPinDirection>
    {
        public override ScriptPinDirection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetString() switch
            {
                "in" => ScriptPinDirection.In,
                "out" => ScriptPinDirection.Out,
                _ => throw new JsonException("A pin direction must be 'in' or 'out'.")
            };

        public override void Write(Utf8JsonWriter writer, ScriptPinDirection value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value == ScriptPinDirection.In ? "in" : "out");
    }
}
