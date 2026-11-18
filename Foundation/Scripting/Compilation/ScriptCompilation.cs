namespace ACWorldEdit.Foundation.Scripting;

/// <summary>
/// The result of compiling one document. <see cref="LuaText"/> is null when an error blocks export;
/// the hashes let the exporter refuse a stale or foreign generated file.
/// </summary>
public sealed record ScriptCompilation(
    string? LuaText,
    IReadOnlyList<ScriptDiagnostic> Diagnostics,
    string SourceHash,
    string CatalogHash,
    string OutputName)
{
    public bool Success => LuaText is not null;

    public IReadOnlyList<ScriptDiagnostic> Errors =>
        Diagnostics.Where(diagnostic => diagnostic.IsError).ToList();
}
