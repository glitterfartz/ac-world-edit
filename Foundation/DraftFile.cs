using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ACWorldEdit.Foundation;

/// <summary>
/// Single-writer local draft file shared by creature and graveyard drafts. A sibling .lock file is
/// held exclusively for the draft's lifetime; content is a JSON envelope [payload, SHA-256(payload)]
/// replaced atomically through a flushed temporary file. Corrupt files are rejected, never reset.
/// </summary>
public sealed class DraftFile : IDisposable
{
    readonly string path;
    readonly FileStream writerLock;

    /// <summary>
    /// Acquire the exclusive lock; a second writer for the same path fails with IOException.
    /// </summary>
    public DraftFile(string path)
    {
        this.path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        writerLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>
    /// Return the verified payload, or null when no draft exists yet. A malformed envelope or a
    /// checksum mismatch throws so callers keep the file for review.
    /// </summary>
    public string? Read()
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var envelope = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid draft envelope.");
        if (envelope.Length != 2 || Hash(envelope[0]) != envelope[1])
        {
            throw new InvalidDataException("Draft checksum mismatch.");
        }

        return envelope[0];
    }

    /// <summary>
    /// Flush a checksummed envelope to a sibling temporary file, then replace the draft. A failed
    /// write leaves the previous draft intact.
    /// </summary>
    public void Write(string payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { payload, Hash(payload) }));
            stream.Write(bytes);
            stream.Flush(true);
        }

        File.Move(temp, path, true);
    }

    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public void Dispose() => writerLock.Dispose();
}
