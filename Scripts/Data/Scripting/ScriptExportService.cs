using ACWorldEdit.Foundation.Scripting;

namespace ACWorldEdit.Data.Scripting;

/// <summary>One generated Lua file written by an export.</summary>
public sealed record ScriptExportFile(string DocumentId, string Path, string Hash);

/// <summary>The outcome of one export run; <see cref="Files"/> is empty when anything was refused.</summary>
public sealed record ScriptExportResult(
    bool Success,
    IReadOnlyList<ScriptExportFile> Files,
    IReadOnlyList<ScriptDiagnostic> Diagnostics,
    string? Error,
    string? ErrorCode = null)
{
    public static ScriptExportResult Failed(
        string code, string error, IReadOnlyList<ScriptDiagnostic>? diagnostics = null) =>
        new(false, [], diagnostics ?? [], error, code);
}

/// <summary>
/// Compiles documents and writes their Lua into the ALE export directory. Every check (compile
/// errors, output collisions, manifest ownership) runs before the first write, so a failed run
/// leaves the directory untouched; a successful one replaces its generated files and the manifest.
/// </summary>
public sealed class ScriptExportService
{
    readonly ScriptCompiler compiler;

    public ScriptExportService(ScriptCompiler compiler) => this.compiler = compiler;

    public ScriptExportService(ScriptHostProfile host) : this(new ScriptCompiler(host)) { }

    public ScriptExportResult Export(ScriptDocument document, string exportDirectory) =>
        ExportAll([document], exportDirectory);

    public ScriptExportResult ExportAll(IEnumerable<ScriptDocument> documents, string exportDirectory)
    {
        var compiled = documents
            .Select(document => (Document: document, Compilation: compiler.Compile(document)))
            .ToList();

        var errors = compiled.SelectMany(pair => pair.Compilation.Errors).ToList();
        if (errors.Count > 0)
        {
            return ScriptExportResult.Failed(ScriptDiagnosticCodes.ExportAllBlocked, "Fix the errors before exporting.", errors);
        }

        var duplicateId = compiled
            .GroupBy(pair => pair.Document.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            return ScriptExportResult.Failed(ScriptDiagnosticCodes.ExportCollision,
                $"Document id '{duplicateId.Key}' is exported more than once.");
        }

        var collision = compiled
            .GroupBy(pair => pair.Compilation.OutputName, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (collision is not null)
        {
            return ScriptExportResult.Failed(ScriptDiagnosticCodes.ExportCollision,
                $"Two documents would collide on '{collision.Key}'; give one a different name.");
        }

        ScriptManifest manifest;
        try
        {
            manifest = ScriptManifest.Load(exportDirectory);
        }
        catch (ScriptManifestException error)
        {
            return ScriptExportResult.Failed(ScriptDiagnosticCodes.ExportCorruptManifest, error.Message);
        }

        var candidates = compiled.Select(pair => new ScriptManifestEntry(
            pair.Document.Id,
            pair.Compilation.OutputName,
            ScriptFileIo.HashText(pair.Compilation.LuaText!),
            pair.Document.Host,
            pair.Compilation.SourceHash,
            pair.Compilation.CatalogHash)).ToList();

        foreach (var candidate in candidates)
        {
            var refusal = manifest.Refusal(exportDirectory, candidate);
            if (refusal is not null)
            {
                return ScriptExportResult.Failed(refusal.Code, refusal.Message);
            }
        }

        var files = new List<ScriptExportFile>();
        foreach (var (pair, candidate) in compiled.Zip(candidates))
        {
            string path = Path.Combine(exportDirectory, candidate.OutputName);
            ScriptFileIo.WriteAtomic(path, pair.Compilation.LuaText!);
            manifest.Record(candidate);
            files.Add(new ScriptExportFile(pair.Document.Id, path, candidate.Hash));
        }

        manifest.Save(exportDirectory);
        return new ScriptExportResult(true, files, [], null);
    }
}
