namespace ACWorldEdit.Foundation.Scripting;

/// <summary>
/// The host- and language-agnostic module. It knows only semantic ids, literals, variables,
/// control flow and returns; function names, event numbers and call spellings live in the emitter.
/// </summary>
public sealed record IrModule(
    string Name,
    string SourceName,
    IReadOnlyList<IrVariable> Variables,
    IReadOnlyList<IrFunction> Functions,
    IReadOnlyList<IrFunction> Events,
    IReadOnlyList<IrRegistration> Registrations);

public sealed record IrVariable(string Name, IrLiteral? Default);

public sealed record IrParameter(string Name, ScriptValueType Type, bool Nullable = false);

public sealed record IrReturnSlot(string Name, ScriptValueType Type);

public sealed record IrFunction(
    string Name,
    IReadOnlyList<IrParameter> Parameters,
    IReadOnlyList<IrReturnSlot> Returns,
    IReadOnlyList<IrStmt> Body,
    bool IsEvent = false);

public sealed record IrRegistration(
    string FunctionName,
    string Register,
    IReadOnlyList<IrExpr> ScopeValues,
    int EventId,
    string EventName,
    int Shots);

public abstract record IrStmt;

/// <summary>A write call in a statement position.</summary>
public sealed record IrCallStmt(
    IrExpr? Receiver,
    string Call,
    IReadOnlyList<IrExpr> Args,
    ScriptReceiverStyle ReceiverStyle = ScriptReceiverStyle.Colon) : IrStmt;

public sealed record IrVarSet(string Name, IrExpr Value) : IrStmt;

public sealed record IrLocal(string Name, IrExpr Value) : IrStmt;

public sealed record IrMultiLocal(IReadOnlyList<string> Names, IrExpr Value) : IrStmt;

public sealed record IrIf(IrExpr Condition, IReadOnlyList<IrStmt> Then, IReadOnlyList<IrStmt> Else) : IrStmt;

public sealed record IrReturn(IReadOnlyList<IrExpr?> Values) : IrStmt;

/// <summary>Fixed shape for timer/async continuations; the UI and lowering arrive in a later milestone.</summary>
public sealed record IrLatent(
    IrExpr Call,
    IReadOnlyList<IrParameter> CallbackParameters,
    string? TimerIdLocal,
    IReadOnlyList<IrStmt> Continuation) : IrStmt;

public sealed record IrComment(string Text) : IrStmt;

public sealed record IrWhile(IrExpr Condition, IReadOnlyList<IrStmt> Body) : IrStmt;

public sealed record IrForRange(string Name, IrExpr From, IrExpr To, IReadOnlyList<IrStmt> Body) : IrStmt;

public sealed record IrForEach(string Name, IrExpr List, IReadOnlyList<IrStmt> Body) : IrStmt;

public abstract record IrExpr;

public sealed record IrLiteral(ScriptValueType Type, object? Value) : IrExpr;

public sealed record IrVariableGet(string Name) : IrExpr;

public sealed record IrCall(
    IrExpr? Receiver,
    string Call,
    IReadOnlyList<IrExpr> Args,
    ScriptReceiverStyle ReceiverStyle = ScriptReceiverStyle.Colon,
    bool NullableReceiver = false) : IrExpr;

public sealed record IrBinary(string Op, IrExpr Left, IrExpr Right) : IrExpr;

public sealed record IrUnary(string Op, IrExpr Operand) : IrExpr;

public sealed record IrIndex(IrExpr Target, IrExpr Key) : IrExpr;
