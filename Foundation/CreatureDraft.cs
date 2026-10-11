using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace ACWorldEdit.Foundation;

public sealed record CreatureMove(string Source, uint Guid, WorldPose Before, WorldPose After);
public sealed record CreatureDraftSnapshot(int Version, CreatureMove[] History, int Cursor);
/// <summary>Local position-only draft. Never owns or mutates source rows.</summary>
public sealed class CreatureDraft : IDisposable
{
    readonly string path;
    readonly FileStream writerLock;
    CreatureDraftSnapshot state = new(1,[],0);
    public int Count => state.Cursor;
    public CreatureDraft(string path)
    {
        this.path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        writerLock = new FileStream(path + ".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try { Recover(); } catch { writerLock.Dispose(); throw; }
    }
    void Recover()
    {
        if (!File.Exists(path)) return;
        var envelope = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid creature draft.");
        if (envelope.Length != 2 || Hash(envelope[0]) != envelope[1]) throw new InvalidDataException("Creature draft checksum mismatch.");
        var restored = JsonSerializer.Deserialize<CreatureDraftSnapshot>(envelope[0]) ?? throw new InvalidDataException("Invalid creature draft.");
        if (restored.Version != 1 || restored.Cursor < 0 || restored.Cursor > restored.History.Length) throw new InvalidDataException("Unsupported creature draft.");
        foreach (var move in restored.History) { move.Before.Validate(); move.After.Validate(); if (move.Before.Map != move.After.Map) throw new InvalidDataException("Cross-map creature draft."); }
        state = restored;
    }
    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public WorldPose Effective(string source,uint guid,WorldPose original)
    {
        var moves = state.History.Take(state.Cursor).Where(m => m.Source == source && m.Guid == guid && m.Before.Map == original.Map).ToArray();
        // Never silently apply an old draft over a changed server baseline.
        return moves.Length > 0 && moves[0].Before == original ? moves[^1].After : original;
    }
    public bool Conflict(string source,uint guid,WorldPose original)
    { var first = state.History.Take(state.Cursor).FirstOrDefault(m => m.Source == source && m.Guid == guid && m.Before.Map == original.Map); return first != null && first.Before != original; }
    public void Move(string source,uint guid,WorldPose original,WorldPose after)
    {
        after.Validate(); if (after.Map != original.Map) throw new InvalidDataException("Cross-map move.");
        if (Conflict(source,guid,original)) throw new InvalidDataException("Source position changed; draft requires review.");
        var before = Effective(source,guid,original); if (after == before) return;
        Save(new(1,state.History.Take(state.Cursor).Append(new CreatureMove(source,guid,before,after)).ToArray(),state.Cursor+1));
    }
    public void Undo() { if (state.Cursor > 0) Save(state with { Cursor = state.Cursor-1 }); }
    public void Redo() { if (state.Cursor < state.History.Length) Save(state with { Cursor = state.Cursor+1 }); }
    void Save(CreatureDraftSnapshot next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); string payload = JsonSerializer.Serialize(next), temp = path + ".tmp";
        using (var stream = new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None))
        { byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] {payload,Hash(payload)})); stream.Write(bytes); stream.Flush(true); }
        File.Move(temp,path,true); state = next;
    }
    public void Dispose() => writerLock.Dispose();
}
