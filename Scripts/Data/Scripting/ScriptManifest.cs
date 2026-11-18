using System.Text.Json;
using System.Text.Json.Serialization;
using ACWorldEdit.Foundation.Scripting;

namespace ACWorldEdit.Data.Scripting;

/// <summary>One generated Lua file the manifest owns: document id → output, hash and provenance.</summary>
public sealed record ScriptManifestEntry(
    string DocumentId,
    string OutputName,
    string Hash,
    string HostId,
    string SourceHash,
    string CatalogHash);

public sealed class ScriptManifestException : Exception
{
    public ScriptManifestException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>A reason the manifest will not let an export overwrite a file.</summary>
public sealed record ScriptManifestRefusal(string Code, string Message);

/// <summary>
/// <c>.acworld-scripts.json</c> in the export directory. It records which generated files this
/// editor wrote and their hashes, so export never overwrites a hand-written or edited script.
/// The manifest is advisory bookkeeping: unmanaged files are never deleted and orphaned entries
/// are reported, not removed.
/// </summary>
public sealed class ScriptManifest
{
    public const string FileName = ".acworld-scripts.json";
    public const int CurrentFormat = 1;

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    readonly SortedDictionary<string, ScriptManifestEntry> entries = new(StringComparer.Ordinal);

    public IReadOnlyCollection<ScriptManifestEntry> Entries => entries.Values.ToList();

    public ScriptManifestEntry? Find(string documentId) => entries.GetValueOrDefault(documentId);

    public void Record(ScriptManifestEntry entry) => entries[entry.DocumentId] = entry;

    public bool Remove(string documentId) => entries.Remove(documentId);

    public static ScriptManifest Load(string directory)
    {
        string path = Path.Combine(directory, FileName);
        var manifest = new ScriptManifest();
        if (!File.Exists(path))
        {
            return manifest;
        }

        try
        {
            var file = JsonSerializer.Deserialize<ManifestFile>(File.ReadAllText(path), Options)
                ?? throw new ScriptManifestException("The export manifest is empty.");
            if (file.Format > CurrentFormat)
            {
                throw new ScriptManifestException(
                    $"The export manifest has format {file.Format}; this build understands {CurrentFormat}.");
            }

            foreach (var (key, entry) in file.Entries ?? [])
            {
                if (string.IsNullOrWhiteSpace(entry.DocumentId) || entry.DocumentId != key
                    || string.IsNullOrWhiteSpace(entry.OutputName) || string.IsNullOrWhiteSpace(entry.Hash))
                {
                    throw new ScriptManifestException($"The export manifest entry '{key}' is incomplete.");
                }

                manifest.entries[key] = entry;
            }

            return manifest;
        }
        catch (ScriptManifestException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            throw new ScriptManifestException($"The export manifest could not be read: {error.Message}", error);
        }
    }

    /// <summary>Replace the manifest atomically; the export directory is created when missing.</summary>
    public void Save(string directory)
    {
        var file = new ManifestFile
        {
            Format = CurrentFormat,
            Entries = new SortedDictionary<string, ScriptManifestEntry>(entries, StringComparer.Ordinal)
        };
        ScriptFileIo.WriteAtomic(Path.Combine(directory, FileName), JsonSerializer.Serialize(file, Options));
    }

    /// <summary>
    /// Null when <paramref name="candidate"/> may be written, otherwise why it may not: the target
    /// exists and is untracked, belongs to another output, or changed since it was exported.
    /// </summary>
    public ScriptManifestRefusal? Refusal(string directory, ScriptManifestEntry candidate)
    {
        string name = candidate.OutputName;
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name)
        {
            return new ScriptManifestRefusal(ScriptDiagnosticCodes.ExportForeignFile,
                $"'{name}' is not a plain file name inside the export directory.");
        }

        string path = Path.Combine(directory, name);
        if (!File.Exists(path))
        {
            return null;
        }

        if (!entries.TryGetValue(candidate.DocumentId, out var owned))
        {
            return new ScriptManifestRefusal(ScriptDiagnosticCodes.ExportForeignFile,
                $"Refusing to overwrite '{name}': the export manifest does not own this file.");
        }

        if (!string.Equals(owned.OutputName, name, StringComparison.Ordinal))
        {
            return new ScriptManifestRefusal(ScriptDiagnosticCodes.ExportForeignFile,
                $"Refusing to overwrite '{name}': it belongs to another export of this document.");
        }

        if (!string.Equals(owned.Hash, ScriptFileIo.HashFile(path), StringComparison.Ordinal))
        {
            return new ScriptManifestRefusal(ScriptDiagnosticCodes.ExportForeignFile,
                $"Refusing to overwrite '{name}': the file changed after it was exported.");
        }

        return null;
    }

    /// <summary>Entries whose generated file is gone; they are reported and left in the manifest.</summary>
    public IReadOnlyList<ScriptManifestEntry> Orphans(string directory) =>
        entries.Values
            .Where(entry => !File.Exists(Path.Combine(directory, entry.OutputName)))
            .ToList();

    sealed class ManifestFile
    {
        public int Format { get; set; } = CurrentFormat;
        public SortedDictionary<string, ScriptManifestEntry>? Entries { get; set; }
    }
}
