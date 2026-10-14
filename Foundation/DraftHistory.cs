using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ACWorldEdit.Foundation;

/// <summary>
/// Versioned persisted history and undo cursor. Entries at or beyond Cursor are redo history until a
/// new entry replaces that branch. Immutable, so a worker can read a captured snapshot while the main
/// thread appends. Property names are the on-disk JSON shape; keep them stable.
/// </summary>
public sealed record DraftSnapshot<T>(int Version, T[] History, int Cursor)
{
    public IEnumerable<T> AppliedEntries() => History.Take(Cursor);
}

/// <summary>
/// Single-writer local draft file shared by creature, graveyard and interaction drafts. A sibling
/// .lock file is held exclusively for the draft's lifetime; the whole snapshot is stored as a JSON
/// envelope [payload, SHA-256(payload)] replaced atomically through a flushed temporary file. Corrupt
/// or unsupported files are rejected, never reset. Each draft type supplies its entry validation and
/// its own stale-baseline rules over <see cref="State"/>.
/// </summary>
public sealed class DraftHistory<T> : IDisposable
{
    readonly string path;
    readonly FileStream writerLock;
    public DraftSnapshot<T> State { get; private set; } = new(1, [], 0);
    public bool CanUndo => State.Cursor > 0;
    public bool CanRedo => State.Cursor < State.History.Length;

    /// <summary>
    /// Acquire the exclusive lock (a second writer for the same path fails with IOException), then
    /// recover saved history. check validates each entry and returns it, possibly normalized; a failed
    /// recovery releases the lock and preserves the existing file. kind names the draft in errors.
    /// </summary>
    public DraftHistory(string path, string kind, Func<T, T> check)
    {
        this.path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        writerLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            Recover(kind, check);
        }
        catch
        {
            writerLock.Dispose();
            throw;
        }
    }

    void Recover(string kind, Func<T, T> check)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var envelope = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid draft envelope.");
        if (envelope.Length != 2 || Hash(envelope[0]) != envelope[1])
        {
            throw new InvalidDataException("Draft checksum mismatch.");
        }

        var restored = JsonSerializer.Deserialize<DraftSnapshot<T>>(envelope[0]) ?? throw new InvalidDataException($"Invalid {kind} draft.");
        if (restored.Version != 1 || restored.Cursor < 0 || restored.Cursor > restored.History.Length)
        {
            throw new InvalidDataException($"Unsupported {kind} draft.");
        }

        State = restored with { History = restored.History.Select(check).ToArray() };
    }

    /// <summary>
    /// Persist one new entry after discarding any redo branch.
    /// </summary>
    public void Append(T entry) => Save(new(1, State.AppliedEntries().Append(entry).ToArray(), State.Cursor + 1));

    public void Undo()
    {
        if (CanUndo)
        {
            Save(State with { Cursor = State.Cursor - 1 });
        }
    }

    public void Redo()
    {
        if (CanRedo)
        {
            Save(State with { Cursor = State.Cursor + 1 });
        }
    }

    /// <summary>
    /// Flush a checksummed envelope to a sibling temporary file, replace the draft, then publish the
    /// in-memory state. A failed write leaves both the previous file and history intact.
    /// </summary>
    void Save(DraftSnapshot<T> next)
    {
        string payload = JsonSerializer.Serialize(next);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { payload, Hash(payload) })));
            stream.Flush(true);
        }

        File.Move(temp, path, true);
        State = next;
    }

    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public void Dispose() => writerLock.Dispose();
}
