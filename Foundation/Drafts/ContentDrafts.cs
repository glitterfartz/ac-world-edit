namespace ACWorldEdit.Foundation;

/// <summary>
/// The one local content draft shared by the interaction, quest and behavior editors: a single
/// InteractionDraft file (single writer) with one undo history. Editors commit through Commit, which
/// rejects edits that introduce a new rule error, and listen to Changed to refresh after any
/// editor's change. Nothing here touches the database.
/// </summary>
public sealed class ContentDrafts : IDisposable
{
    string path;
    readonly Func<string, ITableCatalog> catalogFor;
    public InteractionDraft? Draft { get; private set; }
    public string? Error { get; private set; }
    /// <summary>True when another process holds the draft's lock file, so editing is disabled.</summary>
    public bool Locked { get; private set; }
    /// <summary>The lock file another process holds (or that a failed open named), for the prompt.</summary>
    public string? LockPath { get; private set; }
    /// <summary>
    /// Raised after every successful commit, undo or redo; the argument is the editor that caused it.
    /// </summary>
    public event Action<object?>? Changed;

    /// <summary>
    /// Open the draft file at an absolute path; a corrupt or locked file leaves editing disabled.
    /// catalogFor names each source's table catalog (AzerothCore for every source when omitted).
    /// </summary>
    public ContentDrafts(string path, Func<string, ITableCatalog>? catalogFor = null)
    {
        this.path = path;
        this.catalogFor = catalogFor ?? (_ => AcTables.Instance);
        Reopen();
    }

    /// <summary>Switch to another draft file (another client profile) and open it.</summary>
    public void Rebind(string newPath)
    {
        path = newPath;
        Reopen();
        Changed?.Invoke(null);
    }

    /// <summary>
    /// (Re)open the draft file; a corrupt or locked file keeps editing disabled and is preserved.
    /// </summary>
    public void Reopen()
    {
        Draft?.Dispose();
        Draft = null;
        Locked = false;
        LockPath = null;
        try
        {
            Draft = new InteractionDraft(path, catalogFor);
            Error = null;
        }
        catch (IOException)
        {
            Locked = true;
            LockPath = path + ".lock";
            Error = $"Another editor is using the local content draft; its lock file is {LockPath}.";
        }
        catch (UnauthorizedAccessException)
        {
            Locked = true;
            LockPath = path + ".lock";
            Error = $"The local content draft's lock file cannot be opened: {LockPath}.";
        }
        catch (Exception)
        {
            Error = "Cannot open the local content draft; editing disabled, existing file preserved.";
        }
    }

    /// <summary>
    /// Remove the detected lock file and try again. If another process still holds it the delete or
    /// the reopen fails; the draft file itself is never deleted.
    /// </summary>
    public (bool Ok, string Message) DeleteLockAndReopen()
    {
        string lockPath = LockPath ?? path + ".lock";
        try
        {
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return (false, $"Could not delete {lockPath}. Close the other editor and try again.");
        }

        Reopen();
        if (Draft != null)
        {
            Changed?.Invoke(null);
            return (true, "Editing enabled.");
        }

        return (false, Error ?? "Editing is still disabled.");
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

        try
        {
            // Inside the try: a table this source cannot edit is reported like any other refusal.
            var prospective = effective.Clone();
            foreach (var edit in edits)
            {
                prospective.Apply(edit, catalogFor(source));
            }

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
        return (true, $"Saved \"{label}\".");
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

    /// <summary>
    /// Discard one row's net change (Pending Changes window); editors refresh through Changed.
    /// </summary>
    public bool Revert(string source, RowEdit net)
    {
        if (Draft == null)
        {
            return false;
        }

        try
        {
            Draft.Revert(source, net);
        }
        catch (Exception)
        {
            return false;
        }

        Changed?.Invoke(null);
        return true;
    }

    /// <summary>
    /// Drop the whole draft history; editors refresh through Changed.
    /// </summary>
    public bool Clear()
    {
        if (Draft == null)
        {
            return false;
        }

        try
        {
            Draft.Clear();
        }
        catch (Exception)
        {
            return false;
        }

        Changed?.Invoke(null);
        return true;
    }

    public void Dispose() => Draft?.Dispose();
}
