using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>
/// Persistence checks using unique disposable artifact paths without any source database.
/// </summary>
public class CreatureDraftTests
{
    [Fact]
    public void Passes() => Core();

    /// <summary>
    /// Check reopen, undo/redo branching, source conflicts/isolation, writer locks and checksum
    /// rejection without overwriting corrupt files.
    /// </summary>
    static void Core()
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "positions.json"));
        var baseline = new WorldPose(0, 100, 200, 30, 1);
        var moved = baseline with
        {
            X = 110,
            Z = 35
        };
        using (var draft = new CreatureDraft(path))
        {
            draft.Move("source-a", 7, baseline, moved);
            Check(draft.Effective("source-a", 7, baseline) == moved, "Move lost");
            Check(draft.Effective("source-b", 7, baseline) == baseline, "Different source shares draft");
            Check(draft.Conflict("source-a", 7, baseline with { X = 101 }), "Stale baseline not detected");
            Check(draft.Effective("source-a", 7, baseline with { X = 101 }).X == 101, "Stale draft overwrites source");
            try
            {
                using var duplicate = new CreatureDraft(path);
                throw new Exception("Second writer accepted");
            }
            catch (IOException)
            {
            }
        }

        using (var draft = new CreatureDraft(path))
        {
            Check(draft.Effective("source-a", 7, baseline) == moved, "Reopen lost move");
            draft.Undo();
            Check(draft.Effective("source-a", 7, baseline) == baseline, "Undo failed");
            draft.Redo();
            Check(draft.Effective("source-a", 7, baseline) == moved, "Redo failed");
            draft.Undo();
            draft.Move("source-a", 7, baseline, baseline with { Y = 220 });
            draft.Redo();
            Check(draft.Effective("source-a", 7, baseline).Y == 220, "Branch retained invalid redo");
        }

        // Heading-only edits use the same command type; reopen must keep the exact double heading.
        var turned = baseline with { Heading = WorldCoordinates.NormalizeHeading(baseline.Heading + Math.PI / 2) };
        using (var draft = new CreatureDraft(path))
        {
            draft.Move("source-a", 8, baseline, turned);
        }

        using (var draft = new CreatureDraft(path))
        {
            Check(draft.Effective("source-a", 8, baseline) == turned, "Reopen lost heading-only move");
            draft.Undo();
            Check(draft.Effective("source-a", 8, baseline) == baseline, "Heading undo failed");
            draft.Redo();
            Check(draft.Effective("source-a", 8, baseline).Heading == turned.Heading, "Heading redo changed value");
        }

        string saved = File.ReadAllText(path);
        File.WriteAllText(path, saved.Replace("source-a", "source-z"));
        try
        {
            using var corrupt = new CreatureDraft(path);
            throw new Exception("Corrupt draft accepted");
        }
        catch (System.IO.InvalidDataException)
        {
        }

        Check(File.ReadAllText(path) != saved, "Corrupt draft was silently replaced");
        Console.WriteLine("CREATURE_DRAFT_CHECKS_PASSED move/heading/reopen/undo/redo/branch/source-conflict/single-writer/checksum");
    }
}
