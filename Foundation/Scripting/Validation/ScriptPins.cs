using System.Text.Json;

namespace ACWorldEdit.Foundation.Scripting;

/// <summary>
/// Builds the pins of one node from the host profile, the built-in catalog and the document's
/// variables/functions. The validator and the compiler both call this, so a known node's pins can
/// never mean different things in the two passes. An unknown kind keeps its stored snapshot.
/// </summary>
public static class ScriptPins
{
    public static IReadOnlyList<ScriptPin> Build(ScriptDocument document, ScriptHostProfile host, ScriptNode node)
    {
        if (host.Event(node.Kind) is { } definition)
        {
            return EventPins(definition);
        }

        if (host.Capability(node.Kind) is { } capability)
        {
            return CapabilityPins(capability);
        }

        return node.Kind switch
        {
            ScriptBuiltinCatalog.VariableGet => VariablePins(document, node, only: true),
            ScriptBuiltinCatalog.VariableSet => VariablePins(document, node, only: false),
            ScriptBuiltinCatalog.CallFunction => CallPins(document, node),
            ScriptBuiltinCatalog.Return => node.Pins,
            _ when ScriptBuiltinCatalog.Find(node.Kind) is { } builtin => BuiltinPins(builtin),
            _ => node.Pins
        };
    }

    public static ScriptPin? Find(IReadOnlyList<ScriptPin> pins, string id) =>
        pins.FirstOrDefault(pin => pin.Id == id);

    public static string? PropertyString(ScriptNode node, string name) =>
        node.Properties.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static IReadOnlyList<ScriptPin> EventPins(ScriptEventDefinition definition)
    {
        var pins = new List<ScriptPin> { Pin("exec", ScriptPinDirection.Out, ScriptValueType.Exec) };
        pins.AddRange(definition.Params.Select(parameter =>
            Pin(parameter.Name, ScriptPinDirection.Out, parameter.Type, parameter.Nullable)));
        pins.AddRange(definition.Returns.Select(slot =>
            Pin(slot.Name, ScriptPinDirection.In, slot.Type, slot.Nullable)));
        return pins;
    }

    static IReadOnlyList<ScriptPin> CapabilityPins(ScriptCapability capability)
    {
        var pins = new List<ScriptPin>();
        if (capability.Effect == ScriptEffect.Write)
        {
            pins.Add(Pin("exec", ScriptPinDirection.In, ScriptValueType.Exec));
            pins.Add(Pin("exec_out", ScriptPinDirection.Out, ScriptValueType.Exec));
        }

        if (capability.Receivers.Length > 0)
        {
            pins.Add(Pin("self", ScriptPinDirection.In, capability.Receivers[0]));
        }

        pins.AddRange(capability.Params.Select(parameter =>
            Pin(parameter.Name, ScriptPinDirection.In, parameter.Type, parameter.Nullable)));
        pins.AddRange(capability.Returns.Select(slot =>
            Pin(slot.Name, ScriptPinDirection.Out, slot.Type, slot.Nullable)));
        return pins;
    }

    static IReadOnlyList<ScriptPin> BuiltinPins(ScriptBuiltinNode builtin) =>
        builtin.Pins.Select(pin => Pin(pin.Name, pin.Direction, pin.Type, pin.Nullable)).ToArray();

    static IReadOnlyList<ScriptPin> VariablePins(ScriptDocument document, ScriptNode node, bool only)
    {
        ScriptValueType type = ScriptValueType.Any;
        string? variableId = PropertyString(node, "variable");
        if (variableId is not null && document.Variables.FirstOrDefault(v => v.Id == variableId) is { } variable)
        {
            type = variable.Type;
        }
        else if (Find(node.Pins, "value") is { } snapshot)
        {
            type = snapshot.Type;
        }

        if (only)
        {
            return [Pin("value", ScriptPinDirection.Out, type)];
        }

        return
        [
            Pin("exec", ScriptPinDirection.In, ScriptValueType.Exec),
            Pin("value", ScriptPinDirection.In, type),
            Pin("exec_out", ScriptPinDirection.Out, ScriptValueType.Exec)
        ];
    }

    static IReadOnlyList<ScriptPin> CallPins(ScriptDocument document, ScriptNode node)
    {
        string? functionId = PropertyString(node, "function");
        var function = functionId is null ? null : document.Functions.FirstOrDefault(f => f.Id == functionId);
        var pins = new List<ScriptPin>
        {
            Pin("exec", ScriptPinDirection.In, ScriptValueType.Exec)
        };
        if (function is not null)
        {
            pins.AddRange(function.Parameters.Select(parameter =>
                Pin(parameter.Name, ScriptPinDirection.In, parameter.Type)));
        }

        pins.Add(Pin("exec_out", ScriptPinDirection.Out, ScriptValueType.Exec));
        if (function is not null)
        {
            pins.AddRange(function.Returns.Select(slot =>
                Pin(slot.Name, ScriptPinDirection.Out, slot.Type)));
        }

        return pins;
    }

    static ScriptPin Pin(string id, ScriptPinDirection direction, ScriptValueType type, bool nullable = false) =>
        new() { Id = id, Direction = direction, Type = type, Nullable = nullable };
}
