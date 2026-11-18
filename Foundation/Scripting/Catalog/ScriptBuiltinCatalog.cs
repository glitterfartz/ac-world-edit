namespace ACWorldEdit.Foundation.Scripting;

/// <summary>One pin of a built-in node.</summary>
public sealed record ScriptBuiltinPin(string Name, ScriptPinDirection Direction, ScriptValueType Type, bool Nullable = false);

/// <summary>
/// A language-level node the compiler lowers itself (control flow, operators, variables and user
/// function calls). These are not host operations, so they never appear in a host pack.
/// </summary>
public sealed record ScriptBuiltinNode(string Id, string Display, string Category, ScriptBuiltinPin[] Pins);

/// <summary>The compiler's built-in node set, shared by the validator, palette and IR builder.</summary>
public static class ScriptBuiltinCatalog
{
    public const string Branch = "flow.branch";
    public const string Sequence = "flow.sequence";
    public const string VariableGet = "flow.variable_get";
    public const string VariableSet = "flow.variable_set";
    public const string CallFunction = "flow.call_function";
    public const string Return = "flow.return";
    public const string ForRange = "flow.for_range";
    public const string ParameterGet = "flow.parameter_get";
    public const string TimerOnce = "timer.once";
    public const string TimerRepeat = "timer.repeat";
    public const string TimerEventId = "timer.event_id";
    public const string TimerObject = "timer.object";
    public const string TimerRepeats = "timer.repeats";
    public const string ForEach = "flow.foreach";
    public const string ListCreate = "list.create";
    public const string Equal = "flow.equal";
    public const string NotEqual = "flow.not_equal";
    public const string Less = "flow.less";
    public const string LessEqual = "flow.less_equal";
    public const string Greater = "flow.greater";
    public const string GreaterEqual = "flow.greater_equal";
    public const string And = "flow.and";
    public const string Or = "flow.or";
    public const string Not = "flow.not";

    static readonly ScriptBuiltinNode[] Builtins =
    [
        new(Branch, "Branch", "Flow",
        [
            new("exec", ScriptPinDirection.In, ScriptValueType.Exec),
            new("condition", ScriptPinDirection.In, ScriptValueType.Bool),
            new("true", ScriptPinDirection.Out, ScriptValueType.Exec),
            new("false", ScriptPinDirection.Out, ScriptValueType.Exec)
        ]),
        new(Sequence, "Sequence", "Flow",
        [
            new("exec", ScriptPinDirection.In, ScriptValueType.Exec),
            new("first", ScriptPinDirection.Out, ScriptValueType.Exec),
            new("second", ScriptPinDirection.Out, ScriptValueType.Exec)
        ]),
        new(ForEach, "For Each", "Flow",
        [
            new("exec", ScriptPinDirection.In, ScriptValueType.Exec),
            new("list", ScriptPinDirection.In, ScriptValueType.List),
            new("exec_out", ScriptPinDirection.Out, ScriptValueType.Exec),
            new("item", ScriptPinDirection.Out, ScriptValueType.Any)
        ]),
        new(ListCreate, "Make List", "Lists",
        [
            new("a", ScriptPinDirection.In, ScriptValueType.Any),
            new("b", ScriptPinDirection.In, ScriptValueType.Any, Nullable: true),
            new("c", ScriptPinDirection.In, ScriptValueType.Any, Nullable: true),
            new("result", ScriptPinDirection.Out, ScriptValueType.List)
        ]),
        new(ForRange, "For Range", "Flow",
        [
            new("exec", ScriptPinDirection.In, ScriptValueType.Exec),
            new("from", ScriptPinDirection.In, ScriptValueType.Int),
            new("to", ScriptPinDirection.In, ScriptValueType.Int),
            new("exec_out", ScriptPinDirection.Out, ScriptValueType.Exec),
            new("index", ScriptPinDirection.Out, ScriptValueType.Int)
        ]),
        new(ParameterGet, "Get Parameter", "Functions",
        [
            new("value", ScriptPinDirection.Out, ScriptValueType.Any)
        ]),
        new(TimerOnce, "Once (timer)", "Timers",
        [
            new("exec", ScriptPinDirection.In, ScriptValueType.Exec),
            new("self", ScriptPinDirection.In, ScriptValueType.Object),
            new("delay_ms", ScriptPinDirection.In, ScriptValueType.Int),
            new("exec_out", ScriptPinDirection.Out, ScriptValueType.Exec)
        ]),
        new(TimerRepeat, "Repeat (timer)", "Timers",
        [
            new("exec", ScriptPinDirection.In, ScriptValueType.Exec),
            new("self", ScriptPinDirection.In, ScriptValueType.Object),
            new("delay_ms", ScriptPinDirection.In, ScriptValueType.Int),
            new("repeats", ScriptPinDirection.In, ScriptValueType.Int),
            new("exec_out", ScriptPinDirection.Out, ScriptValueType.Exec)
        ]),
        new(TimerEventId, "Timer Event Id", "Timers",
        [
            new("event_id", ScriptPinDirection.Out, ScriptValueType.Int)
        ]),
        new(TimerObject, "Timer Object", "Timers",
        [
            new("object", ScriptPinDirection.Out, ScriptValueType.Any)
        ]),
        new(TimerRepeats, "Timer Repeats", "Timers",
        [
            new("repeats", ScriptPinDirection.Out, ScriptValueType.Int)
        ]),
        new(VariableGet, "Get Variable", "Variables",
        [
            new("value", ScriptPinDirection.Out, ScriptValueType.Any)
        ]),
        new(VariableSet, "Set Variable", "Variables",
        [
            new("exec", ScriptPinDirection.In, ScriptValueType.Exec),
            new("value", ScriptPinDirection.In, ScriptValueType.Any),
            new("exec_out", ScriptPinDirection.Out, ScriptValueType.Exec)
        ]),
        new(CallFunction, "Call Function", "Functions",
        [
            new("exec", ScriptPinDirection.In, ScriptValueType.Exec),
            new("exec_out", ScriptPinDirection.Out, ScriptValueType.Exec)
        ]),
        new(Return, "Return", "Flow",
        [
            new("exec", ScriptPinDirection.In, ScriptValueType.Exec)
        ]),
        Binary(Equal, "Equal", "Flow", ScriptValueType.Any, ScriptValueType.Bool),
        Binary(NotEqual, "Not Equal", "Flow", ScriptValueType.Any, ScriptValueType.Bool),
        Binary(Less, "Less", "Flow", ScriptValueType.Any, ScriptValueType.Bool),
        Binary(LessEqual, "Less or Equal", "Flow", ScriptValueType.Any, ScriptValueType.Bool),
        Binary(Greater, "Greater", "Flow", ScriptValueType.Any, ScriptValueType.Bool),
        Binary(GreaterEqual, "Greater or Equal", "Flow", ScriptValueType.Any, ScriptValueType.Bool),
        Binary(And, "And", "Flow", ScriptValueType.Bool, ScriptValueType.Bool),
        Binary(Or, "Or", "Flow", ScriptValueType.Bool, ScriptValueType.Bool),
        new(Not, "Not", "Flow",
        [
            new("value", ScriptPinDirection.In, ScriptValueType.Bool),
            new("result", ScriptPinDirection.Out, ScriptValueType.Bool)
        ]),
        Number("math.add", "Add"),
        Number("math.subtract", "Subtract"),
        Number("math.multiply", "Multiply"),
        Number("math.divide", "Divide"),
        Number("math.modulo", "Modulo"),
        Number("math.min", "Min"),
        Number("math.max", "Max"),
        new("string.concat", "Concat", "String",
        [
            new("a", ScriptPinDirection.In, ScriptValueType.String),
            new("b", ScriptPinDirection.In, ScriptValueType.String),
            new("result", ScriptPinDirection.Out, ScriptValueType.String)
        ]),
        new("string.length", "Length", "String",
        [
            new("text", ScriptPinDirection.In, ScriptValueType.String),
            new("result", ScriptPinDirection.Out, ScriptValueType.Int)
        ])
    ];

    public static IReadOnlyList<ScriptBuiltinNode> All => Builtins;

    public static ScriptBuiltinNode? Find(string id) => Builtins.FirstOrDefault(node => node.Id == id);

    static ScriptBuiltinNode Binary(string id, string display, string category, ScriptValueType input, ScriptValueType result) =>
        new(id, display, category,
        [
            new("a", ScriptPinDirection.In, input),
            new("b", ScriptPinDirection.In, input),
            new("result", ScriptPinDirection.Out, result)
        ]);

    static ScriptBuiltinNode Number(string id, string display) =>
        new(id, display, "Math",
        [
            new("a", ScriptPinDirection.In, ScriptValueType.Float),
            new("b", ScriptPinDirection.In, ScriptValueType.Float),
            new("result", ScriptPinDirection.Out, ScriptValueType.Float)
        ]);
}
