using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ACWorldEdit.Foundation;

public sealed record Placement(Guid StableId, uint TemplateId, WorldPose Pose);
public sealed record PlacementChange(Placement? Before, Placement? After);
public sealed record WorkspaceSnapshot(int Version, long Sequence, PlacementChange[] History, int Cursor);
sealed record DurableEnvelope(string Payload, string Sha256);

/// <summary>Single-writer, small-corpus experiment. No database or scene-node ownership.</summary>
public sealed class DraftWorkspace : IDisposable
{
    readonly string directory;
    readonly FileStream writerLock;
    List<PlacementChange> history = [];
    int cursor;
    long sequence;
    public string? RecoveryDiagnostic { get; private set; }
    public IReadOnlyDictionary<Guid, Placement> Placements
    {
        get
        {
            var state = new Dictionary<Guid, Placement>();
            foreach (var change in history.Take(cursor))
            {
                if (change.Before != null) state.Remove(change.Before.StableId);
                if (change.After != null) state.Add(change.After.StableId, change.After);
            }
            return state;
        }
    }
    public DraftWorkspace(string directory)
    {
        this.directory = Path.GetFullPath(directory); Directory.CreateDirectory(this.directory);
        writerLock = new FileStream(Path.Combine(this.directory, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { Recover(); } catch { writerLock.Dispose(); throw; }
    }
    static string Encode(WorkspaceSnapshot snapshot)
    {
        string payload = JsonSerializer.Serialize(snapshot);
        return JsonSerializer.Serialize(new DurableEnvelope(payload, Hash(payload)));
    }
    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    static WorkspaceSnapshot Decode(string line)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<DurableEnvelope>(line) ?? throw new InvalidDataException("Missing envelope.");
            if (Hash(envelope.Payload) != envelope.Sha256) throw new InvalidDataException("Workspace checksum mismatch.");
            var snapshot = JsonSerializer.Deserialize<WorkspaceSnapshot>(envelope.Payload) ?? throw new InvalidDataException("Missing snapshot.");
            if (snapshot.Version != 1 || snapshot.Sequence < 1 || snapshot.Cursor < 0 || snapshot.Cursor > snapshot.History.Length)
                throw new InvalidDataException("Unsupported or invalid workspace snapshot.");
            var state = new Dictionary<Guid, Placement>();
            foreach (var change in snapshot.History)
            {
                if (change.Before == null && change.After == null) throw new InvalidDataException("Empty command.");
                if (change.Before != null && (!state.TryGetValue(change.Before.StableId, out var previous) || previous != change.Before))
                    throw new InvalidDataException("Command baseline mismatch.");
                if (change.Before != null) state.Remove(change.Before.StableId);
                if (change.After != null)
                {
                    change.After.Pose.Validate();
                    if (!state.TryAdd(change.After.StableId, change.After)) throw new InvalidDataException("Duplicate stable ID.");
                }
            }
            return snapshot;
        }
        catch (JsonException e) { throw new InvalidDataException("Malformed workspace record.", e); }
    }
    void Restore(WorkspaceSnapshot snapshot) { history = snapshot.History.ToList(); cursor = snapshot.Cursor; sequence = snapshot.Sequence; }
    void Recover()
    {
        string checkpoint = Path.Combine(directory, "checkpoint.json");
        if (File.Exists(checkpoint)) Restore(Decode(File.ReadAllText(checkpoint)));
        string journal = Path.Combine(directory, "journal.jsonl");
        if (!File.Exists(journal)) return;
        byte[] bytes = File.ReadAllBytes(journal); int start = 0; long previous = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 10) continue;
            var snapshot = Decode(Encoding.UTF8.GetString(bytes, start, i - start));
            if (snapshot.Sequence != previous + 1) throw new InvalidDataException("Journal sequence gap.");
            previous = snapshot.Sequence;
            if (snapshot.Sequence > sequence) Restore(snapshot);
            start = i + 1;
        }
        if (previous < sequence) throw new InvalidDataException("Checkpoint is ahead of committed journal.");
        if (start != bytes.Length)
        {
            RecoveryDiagnostic = "Ignored torn final journal record; retained journal.torn for inspection.";
            File.WriteAllBytes(Path.Combine(directory, "journal.torn"), bytes[start..]);
            using var stream = new FileStream(journal, FileMode.Open, FileAccess.Write, FileShare.None);
            stream.SetLength(start); stream.Flush(true);
        }
    }
    void Persist(List<PlacementChange> nextHistory, int nextCursor)
    {
        var snapshot = new WorkspaceSnapshot(1, sequence + 1, nextHistory.ToArray(), nextCursor);
        string encoded = Encode(snapshot); _ = Decode(encoded);
        using (var stream = new FileStream(Path.Combine(directory, "journal.jsonl"), FileMode.Append, FileAccess.Write, FileShare.None))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(encoded + "\n"); stream.Write(bytes); stream.Flush(true);
        }
        Restore(snapshot);
    }
    void Apply(PlacementChange change)
    {
        var next = history.Take(cursor).ToList(); next.Add(change); Persist(next, next.Count);
    }
    public Guid Place(uint template, WorldPose pose)
    {
        if (template == 0) throw new ArgumentException("Template ID must be positive.");
        pose.Validate(); var id = Guid.NewGuid(); Apply(new(null, new(id, template, pose))); return id;
    }
    public void Move(Guid id, WorldPose pose) { pose.Validate(); var before = Placements[id]; Apply(new(before, before with { Pose = pose })); }
    public Guid Duplicate(Guid id) { var old = Placements[id]; return Place(old.TemplateId, old.Pose); }
    public void Delete(Guid id) => Apply(new(Placements[id], null));
    public void Undo() { if (cursor > 0) Persist(history, cursor - 1); }
    public void Redo() { if (cursor < history.Count) Persist(history, cursor + 1); }
    public void Checkpoint()
    {
        if (sequence == 0) return;
        string temporary = Path.Combine(directory, "checkpoint.tmp");
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { stream.Write(Encoding.UTF8.GetBytes(Encode(new(1, sequence, history.ToArray(), cursor)))); stream.Flush(true); }
        File.Move(temporary, Path.Combine(directory, "checkpoint.json"), true);
    }
    public void Dispose() => writerLock.Dispose();
}
