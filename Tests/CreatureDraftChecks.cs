using ACWorldEdit.Foundation;
public static class CreatureDraftChecks
{
    public static void Run()
    {
        string path = Path.GetFullPath(Path.Combine("artifacts","draft-checks",Guid.NewGuid().ToString("N"),"positions.json"));
        var baseline = new WorldPose(0,100,200,30,1); var moved = baseline with { X=110,Z=35 };
        void Check(bool condition,string message) { if (!condition) throw new Exception(message); }
        using (var draft = new CreatureDraft(path))
        {
            draft.Move("source-a",7,baseline,moved); Check(draft.Effective("source-a",7,baseline)==moved,"Move lost");
            Check(draft.Effective("source-b",7,baseline)==baseline,"Different source shares draft");
            Check(draft.Conflict("source-a",7,baseline with { X=101 }),"Stale baseline not detected");
            Check(draft.Effective("source-a",7,baseline with { X=101 }).X==101,"Stale draft overwrites source");
            try { using var duplicate = new CreatureDraft(path); throw new Exception("Second writer accepted"); } catch (IOException) { }
        }
        using (var draft = new CreatureDraft(path))
        {
            Check(draft.Effective("source-a",7,baseline)==moved,"Reopen lost move"); draft.Undo(); Check(draft.Effective("source-a",7,baseline)==baseline,"Undo failed");
            draft.Redo(); Check(draft.Effective("source-a",7,baseline)==moved,"Redo failed"); draft.Undo(); draft.Move("source-a",7,baseline,baseline with { Y=220 }); draft.Redo();
            Check(draft.Effective("source-a",7,baseline).Y==220,"Branch retained invalid redo");
        }
        string saved = File.ReadAllText(path); File.WriteAllText(path,saved.Replace("source-a","source-z"));
        try { using var corrupt = new CreatureDraft(path); throw new Exception("Corrupt draft accepted"); } catch (System.IO.InvalidDataException) { }
        Check(File.ReadAllText(path)!=saved,"Corrupt draft was silently replaced");
        Console.WriteLine("CREATURE_DRAFT_CHECKS_PASSED move/reopen/undo/redo/branch/source-conflict/single-writer/checksum");
    }
}
