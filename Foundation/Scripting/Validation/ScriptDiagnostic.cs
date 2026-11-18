namespace ACWorldEdit.Foundation.Scripting;

public enum ScriptSeverity
{
    Error,
    Warning,
    Info
}

/// <summary>One validation result with a stable code and an optional graph location.</summary>
public sealed record ScriptDiagnostic(
    string Code,
    ScriptSeverity Severity,
    string Message,
    string? NodeId = null,
    string? PinId = null,
    string? EdgeId = null)
{
    public bool IsError => Severity == ScriptSeverity.Error;
}

/// <summary>Stable diagnostic codes. A code never changes meaning once shipped.</summary>
public static class ScriptDiagnosticCodes
{
    public const string UnknownNode = "SC0101";
    public const string DuplicatePin = "SC0102";
    public const string UnknownEvent = "SC0103";
    public const string NotAvailable = "SC0104";
    public const string DuplicateNode = "SC0105";

    public const string MissingInput = "SC0201";
    public const string WrongType = "SC0202";
    public const string MultipleDataInputs = "SC0203";
    public const string ExecFanOut = "SC0204";
    public const string ExecDataMismatch = "SC0205";
    public const string ExecCycle = "SC0206";
    public const string DataCycle = "SC0207";
    public const string BadEdge = "SC0209";

    public const string MissingScope = "SC0301";
    public const string DuplicateRegistration = "SC0302";
    public const string CrossEntry = "SC0303";
    public const string ReturnContext = "SC0304";
    public const string UnknownVariable = "SC0305";
    public const string VariableDefault = "SC0306";
    public const string ObjectVariable = "SC0307";
    public const string WriteDominance = "SC0308";
    public const string Unreachable = "SC0309";
    public const string UnconnectedExec = "SC0310";
    public const string HighFrequency = "SC0311";
    public const string Recursion = "SC0312";
    public const string CallArity = "SC0313";
    public const string UnknownFunction = "SC0314";
    public const string DuplicateName = "SC0315";

    public const string TooManyLocals = "SC0401";
    public const string LargeInteger = "SC0402";

    public const string ExportCollision = "SC0501";
    public const string ExportForeignFile = "SC0502";
    public const string ExportCorruptManifest = "SC0503";
    public const string ExportAllBlocked = "SC0504";

    public const string MissingEntry = "SC0601";
    public const string CatalogProfile = "SC0602";
}
