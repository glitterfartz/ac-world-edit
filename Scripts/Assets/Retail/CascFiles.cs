using System.Reflection;
using System.Runtime.InteropServices;

namespace ACWorldEdit.Assets;

/// <summary>
/// A CASC read failed. The code is CascLib's own (not a Win32 code); MissingKey is set when the
/// failure is an encrypted section whose key is not available.
/// </summary>
public sealed class CascReadException(string operation, uint code, ulong? missingKey)
    : IOException($"{operation}: CASC error {code}" + (missingKey is { } key ? $" (missing encryption key {key:X16})" : ""))
{
    public uint ErrorCode { get; } = code;
    public ulong? MissingKey { get; } = missingKey;
}

/// <summary>
/// The Battle.net install root and its active retail build, from the root's .build.info. Only the
/// build string and root are used here; the DB2 layouts decide whether a build is readable.
/// </summary>
public sealed record RetailInstall(string Root, string Build, string Locale)
{
    /// <summary>
    /// Accept the install root or its _retail_ folder. The active "wow" row of .build.info must
    /// exist; nothing is written to the install.
    /// </summary>
    public static RetailInstall Detect(string path, string locale = "enUS")
    {
        string root = path.TrimEnd('\\', '/');
        if (Path.GetFileName(root).StartsWith('_') && Path.GetFileName(root).EndsWith('_'))
        {
            root = Directory.GetParent(root)?.FullName ?? root;
        }

        string info = Path.Combine(root, ".build.info");
        if (!File.Exists(info))
        {
            throw new FileNotFoundException("No .build.info in the chosen folder; choose the World of Warcraft install root.", info);
        }

        string[] lines = File.ReadAllLines(info);
        if (lines.Length < 2)
        {
            throw new InvalidDataException(".build.info has no product rows.");
        }

        // Header cells look like "Active!DEC:1"; only the name before '!' matters.
        string[] headers = lines[0].Split('|').Select(h => h.Split('!')[0]).ToArray();
        int active = Array.IndexOf(headers, "Active");
        int product = Array.IndexOf(headers, "Product");
        int version = Array.IndexOf(headers, "Version");
        if (active < 0 || product < 0 || version < 0)
        {
            throw new InvalidDataException(".build.info is missing the Active, Product or Version column.");
        }

        var row = lines.Skip(1).Select(l => l.Split('|')).FirstOrDefault(r => r.Length == headers.Length && r[active] == "1" && r[product] == "wow")
            ?? throw new InvalidDataException("No active retail WoW product in .build.info.");
        return new RetailInstall(root, row[version], locale);
    }
}

/// <summary>
/// Read-only CASC storage for a retail install through the native CascLib. Retail files are
/// addressed by FileDataID (<see cref="AssetRef"/> strings); name lookup does not work on this
/// storage, so a path is only readable when <see cref="PathResolver"/> maps it to a FileDataID. All
/// reads share one lock because CascLib handles are not documented as thread-safe; results are
/// detached byte arrays.
/// </summary>
public sealed class CascFiles : IClientFiles
{
    const long MaximumBytes = 256L * 1024 * 1024;
    // CascLib error codes that mean "encrypted section, key unavailable".
    const uint KeyMissing = 1005;
    const uint KeyNotFound = 6002;
    const uint OpenByFileId = 3;
    const uint OpenByFileIdSkipEncrypted = 0x23;

    static readonly Dictionary<string, uint> Locales = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enUS"] = 2,
        ["enGB"] = 2,
        ["koKR"] = 4,
        ["frFR"] = 16,
        ["deDE"] = 32,
        ["zhCN"] = 64,
        ["esES"] = 128,
        ["zhTW"] = 256,
        ["ruRU"] = 8192,
        ["esMX"] = 4096,
        ["ptBR"] = 16384,
        ["itIT"] = 32768,
    };

    readonly object gate = new();
    readonly uint locale;
    IntPtr storage;

    public ClientKind Kind => ClientKind.Retail;
    public string Build { get; }
    public string Root { get; }

    /// <summary>
    /// Maps a client path to a FileDataID, normally through the community listfile. Null means paths
    /// are unreadable (they fail closed).
    /// </summary>
    public Func<string, uint?>? PathResolver { get; set; }

    /// <summary>
    /// DB2 FileDataIDs that were read with encrypted sections zero-filled.
    /// </summary>
    public HashSet<uint> PartialDb2Files { get; } = [];

    public CascFiles(string path, string localeName = "enUS")
    {
        if (!Locales.TryGetValue(localeName, out locale))
        {
            throw new ArgumentException("Unsupported locale: " + localeName);
        }

        var install = RetailInstall.Detect(path, localeName);
        Build = install.Build;
        Root = install.Root;
        CascNative.EnsureResolver();
        if (!CascNative.CascOpenStorage(Root, locale, out storage))
        {
            throw Error("Open the CASC storage");
        }
    }

    public byte[] Read(string reference)
    {
        if (AssetRef.TryParse(reference, out uint fileId))
        {
            return ReadId(fileId, false);
        }

        if (AssetRef.IsFileReference(reference))
        {
            throw new InvalidDataException("Malformed FileDataID reference: " + reference);
        }

        if (PathResolver?.Invoke(reference) is { } resolved && resolved != 0)
        {
            return ReadId(resolved, false);
        }

        throw new FileNotFoundException("Path is not in the listfile; retail files are addressed by FileDataID: " + reference);
    }

    public bool Exists(string reference)
    {
        uint fileId;
        if (!AssetRef.TryParse(reference, out fileId))
        {
            if (AssetRef.IsFileReference(reference) || PathResolver?.Invoke(reference) is not { } resolved || resolved == 0)
            {
                return false;
            }

            fileId = resolved;
        }

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(storage == IntPtr.Zero, this);
            if (!CascNative.CascOpenFile(storage, new IntPtr(fileId), locale, OpenByFileId, out var file))
            {
                return false;
            }

            CascNative.CascCloseFile(file);
            return true;
        }
    }

    public string Source(string reference) => Exists(reference) ? "casc:" + Build : "missing";

    /// <summary>
    /// Read a DB2 even when some sections are encrypted with an unavailable key. Only a confirmed
    /// missing-key failure permits the zero-filled retry; the table is then recorded in
    /// <see cref="PartialDb2Files"/> so callers can report the gap.
    /// </summary>
    public byte[] ReadAvailableDb2(uint fileId)
    {
        try
        {
            byte[] bytes = ReadId(fileId, false);
            lock (gate)
            {
                PartialDb2Files.Remove(fileId);
            }

            return bytes;
        }
        catch (CascReadException e) when (e.ErrorCode is KeyMissing or KeyNotFound)
        {
            byte[] bytes = ReadId(fileId, true);
            lock (gate)
            {
                PartialDb2Files.Add(fileId);
            }

            return bytes;
        }
    }

    byte[] ReadId(uint fileId, bool skipEncryptedSections)
    {
        if (fileId == 0)
        {
            throw new FileNotFoundException("A zero FileDataID is not a file.");
        }

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(storage == IntPtr.Zero, this);
            long held = System.Diagnostics.Stopwatch.GetTimestamp();
            uint flags = skipEncryptedSections ? OpenByFileIdSkipEncrypted : OpenByFileId;
            if (!CascNative.CascOpenFile(storage, new IntPtr(fileId), locale, flags, out var file))
            {
                throw Error($"Open FileDataID {fileId}");
            }

            try
            {
                if (!CascNative.CascGetFileSize64(file, out ulong length))
                {
                    throw Error($"Read the size of FileDataID {fileId}");
                }

                if (length > (ulong)MaximumBytes)
                {
                    throw new InvalidDataException($"FileDataID {fileId} exceeds the {MaximumBytes >> 20} MiB read limit.");
                }

                var bytes = new byte[(int)length];
                if (length > 0 && (!CascNative.CascReadFile(file, bytes, (uint)length, out uint read) || read != length))
                {
                    throw Error($"Read FileDataID {fileId} (missing local data or encryption key)");
                }

                return bytes;
            }
            finally
            {
                CascNative.CascCloseFile(file);
                Interlocked.Add(ref heldTicks, System.Diagnostics.Stopwatch.GetTimestamp() - held);
            }
        }
    }

    static long heldTicks;

    /// <summary>
    /// Milliseconds all threads together have spent inside the read lock opening and reading files
    /// (the serialized share of decode work). A diagnostic counter for --perf-smoke.
    /// </summary>
    public static double ReadLockMilliseconds => Interlocked.Read(ref heldTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    CascReadException Error(string operation)
    {
        uint code = CascNative.GetCascError();
        ulong? key = code is KeyMissing or KeyNotFound && storage != IntPtr.Zero && CascNative.CascGetNotFoundEncryptionKey(storage, out ulong name) ? name : null;
        return new CascReadException(operation, code, key);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (storage != IntPtr.Zero)
            {
                CascNative.CascCloseStorage(storage);
                storage = IntPtr.Zero;
            }
        }
    }
}

/// <summary>
/// P/Invoke surface of CascLib.dll (MIT, built from github.com/ladislav-zezula/CascLib). The DLL is
/// copied next to the game assembly or kept in native/win-x64; a missing DLL fails only the Retail
/// profile.
/// </summary>
static class CascNative
{
    const string Library = "CascLib.dll";
    static int resolverInstalled;

    /// <summary>
    /// Find CascLib.dll beside the assembly, in the repo's native/win-x64, or at ACWORLD_CASCLIB.
    /// </summary>
    public static void EnsureResolver()
    {
        if (Interlocked.Exchange(ref resolverInstalled, 1) == 1)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(CascNative).Assembly, Resolve);
    }

    static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library)
        {
            return IntPtr.Zero;
        }

        foreach (string candidate in Candidates(Environment.GetEnvironmentVariable("ACWORLD_CASCLIB"), AppContext.BaseDirectory, Path.GetDirectoryName(assembly.Location)))
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Candidate DLL paths in priority order: explicit override, then each base directory and the
    /// nearest ancestor holding native/win-x64.
    /// </summary>
    public static IEnumerable<string> Candidates(string? overridePath, params string?[] directories)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            yield return overridePath;
        }

        foreach (string? directory in directories)
        {
            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            yield return Path.Combine(directory, Library);
            for (var parent = new DirectoryInfo(directory); parent != null; parent = parent.Parent)
            {
                yield return Path.Combine(parent.FullName, "native", "win-x64", Library);
            }
        }
    }

    [DllImport(Library)]
    public static extern uint GetCascError();

    [DllImport(Library, CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CascOpenStorage(string path, uint locale, out IntPtr handle);

    [DllImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CascCloseStorage(IntPtr storage);

    [DllImport(Library, EntryPoint = "CascOpenFile", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CascOpenFile(IntPtr storage, IntPtr fileDataId, uint locale, uint flags, out IntPtr file);

    [DllImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CascGetFileSize64(IntPtr file, out ulong size);

    [DllImport(Library, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CascReadFile(IntPtr file, byte[] buffer, uint length, out uint read);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CascCloseFile(IntPtr file);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CascGetNotFoundEncryptionKey(IntPtr storage, out ulong keyName);
}
