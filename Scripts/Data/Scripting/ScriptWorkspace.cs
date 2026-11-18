using System.Text.Json;
using ACWorldEdit.Foundation.Scripting;

namespace ACWorldEdit.Data.Scripting;

/// <summary>The file state observed when a document was opened, used to detect outside edits.</summary>
public sealed record ScriptFileStamp(string Hash, long Length, DateTime LastWriteUtc);

/// <summary>One `.acwscript` file found by a workspace scan. The document id is authoritative.</summary>
public sealed record ScriptWorkspaceEntry(
    string Path, string FileName, string DocumentId, string Name, string Hash);

/// <summary>A file the scan could not use (malformed document or a duplicated id).</summary>
public sealed record ScriptWorkspaceIssue(string Code, string Message, string Path);

public sealed record ScriptWorkspaceScan(
    IReadOnlyList<ScriptWorkspaceEntry> Files, IReadOnlyList<ScriptWorkspaceIssue> Issues);

public sealed class ScriptWorkspaceException : Exception
{
    public ScriptWorkspaceException(string code, string message, Exception? inner = null)
        : base(message, inner) => Code = code;

    public string Code { get; }
}

/// <summary>
/// Owns the `.acwscript` files in one directory: scan, load, atomic save and delete. The directory
/// is an absolute path supplied by the UI; Foundation never resolves user paths.
/// </summary>
public sealed class ScriptWorkspace
{
    public const string Extension = ".acwscript";
    public const string DuplicateIdCode = "duplicate-id";
    public const string UnreadableCode = "unreadable";
    public const string StaleStampCode = "stale-stamp";
    public const string InvalidDocumentCode = "invalid-document";
    public const string MissingFileCode = "missing-file";
    public const string OutsideWorkspaceCode = "outside-workspace";

    public ScriptWorkspace(string directory) => Directory = Path.GetFullPath(directory);

    public string Directory { get; }

    public ScriptWorkspaceScan Scan()
    {
        var files = new List<ScriptWorkspaceEntry>();
        var issues = new List<ScriptWorkspaceIssue>();
        if (!System.IO.Directory.Exists(Directory))
        {
            return new ScriptWorkspaceScan(files, issues);
        }

        foreach (string path in System.IO.Directory
                     .EnumerateFiles(Directory, "*" + Extension)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
        {
            try
            {
                var document = Load(path);
                if (string.IsNullOrWhiteSpace(document.Id))
                {
                    issues.Add(new ScriptWorkspaceIssue(UnreadableCode,
                        $"'{Path.GetFileName(path)}' has no document id.", path));
                    continue;
                }

                files.Add(new ScriptWorkspaceEntry(
                    path, Path.GetFileName(path), document.Id, document.Name,
                    ScriptDocumentJson.CanonicalHash(document)));
            }
            catch (ScriptWorkspaceException error)
            {
                issues.Add(new ScriptWorkspaceIssue(UnreadableCode, error.Message, path));
            }
        }

        foreach (var duplicate in files
                     .GroupBy(file => file.DocumentId, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            foreach (var file in duplicate)
            {
                issues.Add(new ScriptWorkspaceIssue(DuplicateIdCode,
                    $"Document id '{duplicate.Key}' appears in {duplicate.Count()} files.", file.Path));
            }
        }

        return new ScriptWorkspaceScan(files, issues);
    }

    public ScriptDocument Load(string path)
    {
        string full = Resolve(path);
        string json;
        try
        {
            json = File.ReadAllText(full);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            string code = error is FileNotFoundException or System.IO.DirectoryNotFoundException
                ? MissingFileCode
                : InvalidDocumentCode;
            throw new ScriptWorkspaceException(code, $"Could not read '{Path.GetFileName(full)}': {error.Message}", error);
        }

        try
        {
            return ScriptDocumentJson.Load(json);
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or ScriptFormatException)
        {
            throw new ScriptWorkspaceException(InvalidDocumentCode,
                $"'{Path.GetFileName(full)}' is not a valid script document: {error.Message}", error);
        }
    }

    public ScriptFileStamp Stamp(string path)
    {
        string full = Resolve(path);
        FileInfo info = new(full);
        if (!info.Exists)
        {
            throw new ScriptWorkspaceException(MissingFileCode, $"'{Path.GetFileName(full)}' no longer exists.");
        }

        byte[] bytes = File.ReadAllBytes(full);
        return new ScriptFileStamp(ScriptFileIo.HashBytes(bytes), bytes.LongLength, info.LastWriteTimeUtc);
    }

    /// <summary>Save under the document's existing file, or a fresh slug; atomic and stamp-checked.</summary>
    public string Save(ScriptDocument document, ScriptFileStamp? expected = null)
    {
        string path = FindPath(document.Id) ?? AllocatePath(document);
        if (expected is not null)
        {
            if (!File.Exists(path))
            {
                throw new ScriptWorkspaceException(StaleStampCode,
                    $"'{Path.GetFileName(path)}' was deleted after it was opened.");
            }

            if (Stamp(path) != expected)
            {
                throw new ScriptWorkspaceException(StaleStampCode,
                    $"'{Path.GetFileName(path)}' changed on disk after it was opened.");
            }
        }

        ScriptFileIo.WriteAtomic(path, ScriptDocumentJson.Save(document));
        return path;
    }

    public void Delete(string path, ScriptFileStamp? expected = null)
    {
        string full = Resolve(path);
        if (!File.Exists(full))
        {
            throw new ScriptWorkspaceException(MissingFileCode, $"'{Path.GetFileName(full)}' no longer exists.");
        }

        if (expected is not null && Stamp(full) != expected)
        {
            throw new ScriptWorkspaceException(StaleStampCode,
                $"'{Path.GetFileName(full)}' changed on disk after it was opened.");
        }

        File.Delete(full);
    }

    string? FindPath(string documentId) =>
        Scan().Files.FirstOrDefault(file => string.Equals(file.DocumentId, documentId, StringComparison.Ordinal))?.Path;

    string AllocatePath(ScriptDocument document)
    {
        string slug = ScriptSlug.FromName(document.Name);
        string candidate = Path.Combine(Directory, slug + Extension);
        if (!File.Exists(candidate) || OwnedBy(candidate, document.Id))
        {
            return candidate;
        }

        string shortId = ScriptSlug.ShortId(document.Id);
        candidate = Path.Combine(Directory, $"{slug}-{shortId}{Extension}");
        for (int index = 2; File.Exists(candidate) && !OwnedBy(candidate, document.Id); index++)
        {
            candidate = Path.Combine(Directory, $"{slug}-{shortId}-{index}{Extension}");
        }

        return candidate;
    }

    static bool OwnedBy(string path, string documentId)
    {
        try
        {
            return string.Equals(ScriptDocumentJson.Load(File.ReadAllText(path)).Id, documentId,
                StringComparison.Ordinal);
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException or ScriptFormatException)
        {
            return false;
        }
    }

    string Resolve(string path)
    {
        string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Directory, path));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!full.StartsWith(Directory + Path.DirectorySeparatorChar, comparison))
        {
            throw new ScriptWorkspaceException(OutsideWorkspaceCode,
                $"'{path}' is outside the script workspace.");
        }

        return full;
    }
}
