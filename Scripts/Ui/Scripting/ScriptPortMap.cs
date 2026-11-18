namespace ACWorldEdit;

/// <summary>
/// One node view's pin → GraphEdit mapping. GraphNode slots are configured by row, while GraphEdit
/// signals speak separate input and output port indices; this keeps both and the reverse lookups.
/// </summary>
public sealed class ScriptPortMap
{
    readonly Dictionary<string, int> slots = [];
    readonly Dictionary<string, int> inputs = [];
    readonly Dictionary<string, int> outputs = [];
    readonly Dictionary<int, string> inputPins = [];
    readonly Dictionary<int, string> outputPins = [];
    readonly Dictionary<int, string> slotPins = [];

    /// <summary>Register one pin's slot and the port index GraphNode assigned it.</summary>
    public void Add(string pinId, int slot, bool input, bool output)
    {
        slots[pinId] = slot;
        slotPins[slot] = pinId;
        if (input)
        {
            inputs[pinId] = inputPins.Count;
            inputPins[inputs[pinId]] = pinId;
        }

        if (output)
        {
            outputs[pinId] = outputPins.Count;
            outputPins[outputs[pinId]] = pinId;
        }
    }

    public int Slot(string pinId) => slots.TryGetValue(pinId, out int slot) ? slot : -1;

    public int? InputPort(string pinId) => inputs.TryGetValue(pinId, out int port) ? port : null;

    public int? OutputPort(string pinId) => outputs.TryGetValue(pinId, out int port) ? port : null;

    public string? PinForSlot(int slot) => slotPins.GetValueOrDefault(slot);

    public string? PinForInput(int port) => inputPins.GetValueOrDefault(port);

    public string? PinForOutput(int port) => outputPins.GetValueOrDefault(port);
}
