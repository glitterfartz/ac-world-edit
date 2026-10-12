using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ACWorldEdit.Foundation;

/// <summary>
/// One local history command isolated by database identity and GUID. Before/After are server
/// poses; commands never own or write database rows.
/// </summary>
public sealed record CreatureMove(string Source, uint Guid, WorldPose Before, WorldPose After);
/// <summary>
/// Versioned persisted history and undo cursor. Entries beyond Cursor are redo history until a
/// new movement replaces that branch.
/// </summary>
public sealed record CreatureDraftSnapshot(int Version, CreatureMove[] History, int Cursor);
/// <summary>
/// Single-writer position-only draft independent of scene nodes and the database. Checksummed
/// whole-history snapshots are replaced atomically; source identity and the first baseline
/// prevent replaying stale edits.
/// </summary>
public sealed class CreatureDraft : IDisposable
{
    readonly string path;
    readonly FileStream writerLock;
    CreatureDraftSnapshot state = new(1, [], 0);
    /// <summary>
    /// Acquire an exclusive sibling lock before recovering local history. Failed recovery releases
    /// the lock and preserves the existing draft.
    /// </summary>
    public CreatureDraft(string path)
    {
        this.path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        writerLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            Recover();
        }
        catch
        {
            writerLock.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Verify checksum, version, cursor and pose constraints before publishing recovered history.
    /// Invalid files are rejected rather than silently reset.
    /// </summary>
    void Recover()
    {
        if (!File.Exists(path))
        {
            return;
        }

        var envelope = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid creature draft.");
        if (envelope.Length != 2 || Hash(envelope[0]) != envelope[1])
        {
            throw new InvalidDataException("Creature draft checksum mismatch.");
        }

        var restored = JsonSerializer.Deserialize<CreatureDraftSnapshot>(envelope[0]) ?? throw new InvalidDataException("Invalid creature draft.");
        if (restored.Version != 1 || restored.Cursor < 0 || restored.Cursor > restored.History.Length)
        {
            throw new InvalidDataException("Unsupported creature draft.");
        }

        foreach (var move in restored.History)
        {
            move.Before.Validate();
            move.After.Validate();
            if (move.Before.Map != move.After.Map)
            {
                throw new InvalidDataException("Cross-map creature draft.");
            }
        }

        state = restored;
    }

    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    /// <summary>
    /// Resolve the last applied move only when its first baseline still equals the source pose.
    /// Conflicts return the source position so outdated drafts cannot silently win.
    /// </summary>
    public WorldPose Effective(string source, uint guid, WorldPose original)
    {
        var moves = state.History.Take(state.Cursor).Where(m => m.Source == source && m.Guid == guid && m.Before.Map == original.Map).ToArray();
        // Never silently apply an old draft over a changed server baseline.
        return moves.Length > 0 && moves[0].Before == original ? moves[^1].After : original;
    }

    /// <summary>
    /// Compare the earliest applied command for this source/GUID/map with the current database
    /// baseline. Undone commands do not create an active conflict.
    /// </summary>
    public bool Conflict(string source, uint guid, WorldPose original)
    {
        var first = state.History.Take(state.Cursor).FirstOrDefault(m => m.Source == source && m.Guid == guid && m.Before.Map == original.Map);
        return first != null && first.Before != original;
    }

    /// <summary>
    /// Validate a same-map movement, reject a stale baseline and save a command after discarding any
    /// redo branch. No-op moves do not add history.
    /// </summary>
    public void Move(string source, uint guid, WorldPose original, WorldPose after)
    {
        after.Validate();
        if (after.Map != original.Map)
        {
            throw new InvalidDataException("Cross-map move.");
        }

        if (Conflict(source, guid, original))
        {
            throw new InvalidDataException("Source position changed; draft requires review.");
        }

        var before = Effective(source, guid, original);
        if (after == before)
        {
            return;
        }

        Save(new(1, state.History.Take(state.Cursor).Append(new CreatureMove(source, guid, before, after)).ToArray(), state.Cursor + 1));
    }

    /// <summary>
    /// Persist one step backward in history before changing the in-memory cursor.
    /// </summary>
    public void Undo()
    {
        if (state.Cursor > 0)
        {
            Save(state with { Cursor = state.Cursor - 1 });
        }
    }

    /// <summary>
    /// Persist one step forward in retained history, if any.
    /// </summary>
    public void Redo()
    {
        if (state.Cursor < state.History.Length)
        {
            Save(state with { Cursor = state.Cursor + 1 });
        }
    }

    /// <summary>
    /// Flush a checksummed snapshot into a sibling temporary file, then replace the draft and
    /// publish memory state. A failed write cannot advance history.
    /// </summary>
    void Save(CreatureDraftSnapshot next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string payload = JsonSerializer.Serialize(next), temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { payload, Hash(payload) }));
            stream.Write(bytes);
            stream.Flush(true);
        }

        File.Move(temp, path, true);
        state = next;
    }

    /// <summary>
    /// Release this draft writer lock. Persistence happens at each completed command, not on
    /// disposal.
    /// </summary>
    public void Dispose() => writerLock.Dispose();
}
