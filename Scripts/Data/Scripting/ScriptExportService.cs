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

    /// <summary>
    /// Compile and write a set of documents. A resolver may name a profile per document so a
    /// workspace mixing hosts exports each script with the bindings its document records.
    /// </summary>
    public ScriptExportResult ExportAll(
        IEnumerable<ScriptDocument> documents,
        string exportDirectory,
        Func<ScriptDocument, ScriptHostProfile>? profileFor = null)
    {
        var compiled = documents
            .Select(document =>
            {
                var active = profileFor is null ? compiler : new ScriptCompiler(profileFor(document));
                return (Document: document, Compilation: active.Compile(document));
            })
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

        var diagnostics = new List<ScriptDiagnostic>();
        var files = new List<ScriptExportFile>();
        foreach (var (pair, candidate) in compiled.Zip(candidates))
        {
            string path = Path.Combine(exportDirectory, candidate.OutputName);
            ScriptFileIo.WriteAtomic(path, pair.Compilation.LuaText!);
            if (manifest.Find(candidate.DocumentId) is { } previous
                && previous.OutputName != candidate.OutputName)
            {
                // The rename leaves the old generated file behind; it is reported, never deleted.
                diagnostics.Add(new ScriptDiagnostic(ScriptDiagnosticCodes.ExportOrphaned,
                    ScriptSeverity.Info,
                    $"'{previous.OutputName}' was generated before the rename and is still in the export folder; delete it or ALE will load it too."));
            }

            manifest.Record(candidate);
            files.Add(new ScriptExportFile(pair.Document.Id, path, candidate.Hash));
        }

        manifest.Save(exportDirectory);
        return new ScriptExportResult(true, files, diagnostics, null);
    }
}
