using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ACWorldEdit.Foundation;
using ACWorldEdit.Tests.Support;
using Xunit;
using static Test;

/// <summary>
/// Graveyard draft persistence and server-rule validation using disposable artifact paths, plus a
/// check that creature drafts written before the DraftHistory extraction still load.
/// </summary>
public class GraveyardDraftTests
{
    [Fact]
    public void Passes() => Core();

    static void Core()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N")));
        string path = Path.Combine(root, "graveyards.json");
        // Unsorted links on purpose: the source row must compare equal after normalization.
        var source = new GraveyardRecord(4, 0, -10546.9, 1197.24, 31.7263, "Westfall, Sentinel Hill GY", [new(40, 0, "Westfall"), new(12, 469, "")]);
        static bool Known(uint zone) => zone is 12 or 40 or 44;

        var moved = source with { X = -10540, Z = 32 };
        var relinked = moved with { Links = [new(40, 0, "Westfall"), new(12, 469, ""), new(44, 67, "Redridge")] };
        using (var draft = new GraveyardDraft(path))
        {
            Check(draft.Effective("db", source) == source.Normalized(), "Unedited graveyard differs from source");
            draft.Edit("db", source, moved, Known);
            draft.Edit("db", source, relinked, Known);
            draft.Edit("db", source, relinked with { Comment = "Sentinel Hill" }, Known);
            Check(draft.Effective("db", source).Comment == "Sentinel Hill", "Comment edit lost");
            Check(draft.Effective("other", source) == source.Normalized(), "Different source shares draft");
            Check(draft.Conflict("db", source with { Z = 99 }), "Stale baseline not detected");
            Rejects(() => draft.Edit("db", source with { Z = 99 }, moved, Known), "Edit over stale baseline accepted");
            try
            {
                using var duplicate = new GraveyardDraft(path);
                throw new Exception("Second writer accepted");
            }
            catch (IOException)
            {
            }
        }

        using (var draft = new GraveyardDraft(path))
        {
            var effective = draft.Effective("db", source);
            Check(effective.Comment == "Sentinel Hill" && effective.Links.Length == 3 && effective.X == -10540, "Reopen lost edits");
            Check(effective.Links.Select(l => l.GhostZone).SequenceEqual(new uint[] { 12, 40, 44 }), "Links not normalized");
            draft.Undo();
            Check(draft.Effective("db", source).Comment == source.Comment, "Undo failed");
            draft.Undo();
            Check(draft.Effective("db", source).Links.Length == 2, "Undo link add failed");
            draft.Redo();
            Check(draft.Effective("db", source).Links.Length == 3, "Redo failed");
            draft.Edit("db", source, source with { Y = 1200 }, Known);
            draft.Redo();
            Check(draft.Effective("db", source).Y == 1200 && !draft.CanRedo, "Branch retained invalid redo");

            // Server loading rules.
            Rejects(() => draft.Edit("db", source, source with { Links = [new(40, 1, "")] }, Known), "Non-player faction accepted");
            Rejects(() => draft.Edit("db", source, source with { Links = [new(40, 0, ""), new(40, 469, "")] }, Known), "Duplicate zone link accepted");
            Rejects(() => draft.Edit("db", source, source with { Links = [new(999999, 0, "")] }, Known), "Unknown AreaTable zone accepted");
            Rejects(() => draft.Edit("db", source, source with { Comment = new string('x', 256) }, Known), "Over-long name accepted");
            Rejects(() => draft.Edit("db", source, source with { Map = 1 }, Known), "Cross-map edit accepted");
            Rejects(() => draft.Edit("db", source, source with { Z = double.NaN }, Known), "Non-finite coordinate accepted");
        }

        File.WriteAllText(path, File.ReadAllText(path).Replace("Sentinel Hill\\u0022", "Tampered\\u0022").Replace("Sentinel Hill", "Tampered"));
        Rejects(() => new GraveyardDraft(path), "Tampered graveyard draft accepted");

        Console.WriteLine("GRAVEYARD_DRAFT_CHECKS_PASSED edit/links/comment/reopen/undo/redo/branch/conflict/faction/duplicate/zone/length/map/checksum");
    }
}
