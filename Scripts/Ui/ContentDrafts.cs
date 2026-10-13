using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The one local content draft shared by the interaction, quest and behavior editors: a single
/// InteractionDraft file (single writer) with one undo history. Editors commit through Commit, which
/// rejects edits that introduce a new rule error, and listen to Changed to refresh after any
/// editor's change. Nothing here touches the database.
/// </summary>
public sealed class ContentDrafts : IDisposable
{
    string path = "";
    public InteractionDraft? Draft { get; private set; }
    public string? Error { get; private set; }
    /// <summary>
    /// Raised after every successful commit, undo or redo; the argument is the editor that caused it.
    /// </summary>
    public event Action<object?>? Changed;

    public static ContentDrafts Open()
    {
        var args = OS.GetCmdlineUserArgs();
        bool smoke = args.Any(a => a.EndsWith("-smoke", StringComparison.Ordinal));
        var drafts = new ContentDrafts
        {
            path = ProjectSettings.GlobalizePath(smoke ? $"res://artifacts/content-drafts/{Guid.NewGuid():N}/interactions.json" : "user://drafts/interactions.json")
        };
        drafts.Reopen();
        return drafts;
    }

    /// <summary>
    /// (Re)open the draft file; a corrupt or locked file keeps editing disabled and is preserved.
    /// </summary>
    public void Reopen()
    {
        Draft?.Dispose();
        Draft = null;
        try
        {
            Draft = new InteractionDraft(path);
            Error = null;
        }
        catch (Exception)
        {
            Error = "Cannot open the local content draft; editing disabled, existing file preserved.";
        }
    }

    /// <summary>
    /// Record one command when it introduces no new error from check. Returns a user-facing message.
    /// </summary>
    public (bool Ok, string Message) Commit(object? sender, string source, RowSet snapshot, RowSet effective, string label, IReadOnlyList<RowEdit> edits, Func<RowSet, IEnumerable<InteractionIssue>> check)
    {
        if (Draft == null)
        {
            return (false, Error ?? "Editing is disabled.");
        }

        var prospective = effective.Clone();
        foreach (var edit in edits)
        {
            var table = InteractionTables.Find(edit.Table);
            if (edit.After == null)
            {
                prospective.Remove(table, edit.Key);
            }
            else
            {
                prospective.Set(table, edit.After);
            }
        }

        try
        {
            var before = check(effective).Where(i => i.Error).Select(i => i.Message).ToHashSet();
            var introduced = check(prospective).FirstOrDefault(i => i.Error && !before.Contains(i.Message));
            if (introduced != null)
            {
                return (false, "Not saved: " + introduced.Message);
            }

            Draft.Execute(source, snapshot, label, edits);
        }
        catch (InvalidDataException error)
        {
            return (false, "Not saved: " + error.Message);
        }
        catch (Exception)
        {
            return (false, "Could not save the content draft; previous draft preserved.");
        }

        Changed?.Invoke(sender);
        return (true, $"Saved \"{label}\" to the local draft.");
    }

    public string History(object? sender, bool redo)
    {
        if (Draft == null)
        {
            return Error ?? "Editing is disabled.";
        }

        try
        {
            string? label = redo ? Draft.NextLabel : Draft.LastLabel;
            if (redo)
            {
                Draft.Redo();
            }
            else
            {
                Draft.Undo();
            }

            Changed?.Invoke(sender);
            return label == null ? "" : $"{(redo ? "Redid" : "Undid")} \"{label}\".";
        }
        catch (Exception)
        {
            return "Could not save the draft history.";
        }
    }

    public void Dispose() => Draft?.Dispose();
}
