using System.Text;
using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Change history: staged draft changes sync into the acworld database, applying runs their SQL
/// against the world, reverting runs the stored revert SQL, and any checked rows export into one
/// file for sharing. Stale rows abort before anything is written.
/// </summary>
public partial class ViewerShell
{
    PendingChanges pendingStore = null!;
    static SourceDialects SourceDialects => ContentDraftFiles.Sources;
    PendingChangesWindow pendingWindow = null!;
    SqlPreviewWindow sqlPreview = null!;
    double pendingPoll;
    double historyPoll;
    bool historyLoading;
    bool historyShowAll;
    List<ChangeRecord> historyCache = [];
    // Godot nodes may only be touched on the main thread, but `await`ed database calls resume on
    // worker threads. Async history work therefore posts its UI and draft updates here, and the
    // per-frame poll below runs them on the main thread.
    readonly System.Collections.Concurrent.ConcurrentQueue<Action> mainThreadActions = new();

    void PostToMainThread(Action action) => mainThreadActions.Enqueue(action);

    void DrainMainThreadActions()
    {
        while (mainThreadActions.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                statusLog.Post("Change history: " + error.Message);
            }
        }
    }

    /// <summary>
    /// Every net draft change that has not been applied, one entry per discardable unit.
    /// </summary>
    List<PendingChange> CollectPending()
    {
        pendingStore ??= new PendingChanges(ContentDraftFiles.LocalPath("exported.json"));
        RememberSourceDialect();
        var all = pendingStore.Collect(graveyards.NetEdits(), drafts.Draft?.NetEdits() ?? [], SourceDialects.SchemaFor);
        return all;
    }

    /// <summary>
    /// Every net draft change regardless of the legacy exported file: the history stages these in
    /// acworld instead of hiding them locally. Same stable fingerprints as <see cref="CollectPending"/>.
    /// </summary>
    List<PendingChange> CollectStaged()
    {
        pendingStore ??= new PendingChanges(ContentDraftFiles.LocalPath("exported.json"));
        RememberSourceDialect();
        return pendingStore.CollectAll(graveyards.NetEdits(), drafts.Draft?.NetEdits() ?? [], SourceDialects.SchemaFor);
    }

    /// <summary>
    /// Note the open database's dialect under its source key, so its drafts keep their statements
    /// in that dialect after the connection or the client changes.
    /// </summary>
    void RememberSourceDialect()
    {
        var connection = DatabaseConnection();
        if (connection.World.IsComplete)
        {
            SourceDialects.Remember(new Data.WorldDatabase(connection).SourceKey, connection.Dialect);
        }
    }

    string? CurrentSourceKey()
    {
        var connection = DatabaseConnection();
        if (!connection.World.IsComplete)
        {
            return null;
        }

        return new Data.WorldDatabase(connection).SourceKey;
    }

    /// <summary>
    /// Re-read the drafts, update the sidebar indicator and, when the history window is open,
    /// refresh it from acworld at most every few seconds (edits arrive from several editors, but
    /// the database round-trip must not run at poll rate).
    /// </summary>
    void RefreshPending()
    {
        var changes = CollectStaged();
        sidebar.SetUnsaved(changes.Count);
        if (!pendingWindow.Visible)
        {
            return;
        }

        historyPoll += 0.4;
        if (!historyLoading && historyPoll >= 5.0)
        {
            historyPoll = 0;
            LoadHistoryAsync();
        }
    }

    void PollPending(double delta)
    {
        DrainMainThreadActions();
        pendingPoll += delta;
        if (pendingPoll >= 0.4)
        {
            pendingPoll = 0;
            RefreshPending();
        }
    }

    void OpenPendingChanges()
    {
        pendingWindow.SetShowAll(historyShowAll);
        pendingWindow.Open();
        historyPoll = 0;
        LoadHistoryAsync();
    }

    /// <summary>
    /// Stage current drafts in acworld (preserving notes and applied/reverted status), then show the
    /// filtered history. Database work runs on worker threads; every node touch is posted back to
    /// the main thread. Failures land in the window summary, never in the poll loop.
    /// </summary>
    async void LoadHistoryAsync()
    {
        if (historyLoading)
        {
            return;
        }

        historyLoading = true;
        // Captured on the main thread before any await: everything below may run on workers.
        var connection = DatabaseConnection();
        if (!connection.World.IsComplete)
        {
            PostToMainThread(() =>
            {
                historyCache = [];
                pendingWindow.RefreshHistory(historyCache, "Connect a world database to track changes.");
            });
            historyLoading = false;
            return;
        }

        RememberSourceDialect();
        var staged = CollectStaged();
        DateTime now = DateTime.UtcNow;
        var acworld = connection.Acworld;
        string? sourceKey = CurrentSourceKey();
        bool showAll = historyShowAll;
        int stagedCount = staged.Count;
        // Resolve every staged source's schema and dialect here on the main thread: the merge below
        // runs on workers, and SourceDialects itself is not thread-safe.
        var stagedSchemas = staged.Select(c => c.Source).Distinct(StringComparer.Ordinal)
            .ToDictionary(s => s, s => (Schema: SourceDialects.SchemaFor(s), Dialect: SourceDialects.DialectOf(s)), StringComparer.Ordinal);
        IWorldSchema SchemaFor(string source) => stagedSchemas.TryGetValue(source, out var found) ? found.Schema : WorldSchemas.For(WorldDialect.AzerothCore);
        WorldDialect DialectFor(string source) => stagedSchemas.TryGetValue(source, out var found) ? found.Dialect : WorldDialect.AzerothCore;

        string? error = null;
        var records = new List<ChangeRecord>();
        try
        {
            try
            {
                await AcworldStore.EnsureSchemaAsync(acworld, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception schemaError)
            {
                error = "Change history is unreachable: " + CleanError(schemaError, acworld) + " Edits still stage locally.";
                PostToMainThread(() =>
                {
                    historyCache = [];
                    pendingWindow.RefreshHistory(historyCache, error);
                });
                return;
            }

            List<ChangeRecord> existing;
            try
            {
                existing = await AcworldStore.ListAsync(acworld, sourceKey, showAll, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception listError)
            {
                error = "Change history is unreachable: " + CleanError(listError, acworld);
                var cached = historyCache;
                PostToMainThread(() => pendingWindow.RefreshHistory(cached, error));
                return;
            }

            var byFingerprint = existing.ToDictionary(r => r.Fingerprint, StringComparer.Ordinal);
            var toStage = ChangeLogMerge.MergeStaged(staged, byFingerprint, SchemaFor, DialectFor, now);
            foreach (var record in toStage)
            {
                try
                {
                    await AcworldStore.UpsertStagedAsync(acworld, record, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // One bad row must not hide the rest; the summary below says the history is partial.
                }
            }

            try
            {
                records = await AcworldStore.ListAsync(acworld, sourceKey, showAll, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception listError)
            {
                error = "Change history is unreachable: " + CleanError(listError, acworld);
                var cached = historyCache;
                PostToMainThread(() => pendingWindow.RefreshHistory(cached, error));
                return;
            }

            var snapshot = records;
            PostToMainThread(() =>
            {
                historyCache = snapshot;
                pendingWindow.RefreshHistory(historyCache);
                sidebar.SetUnsaved(stagedCount);
            });
        }
        finally
        {
            historyLoading = false;
        }
    }

    static string CleanError(Exception error, DatabaseSettings acworld) => AcworldDiagnostics.Describe(error, acworld);

    List<ChangeRecord> SelectedRecords(IReadOnlyList<string> fingerprints, bool revert)
    {
        var set = new HashSet<string>(fingerprints);
        var selected = historyCache.Where(r => set.Contains(r.Fingerprint)).ToList();
        selected.Sort((a, b) => revert
            ? b.CreatedAt.CompareTo(a.CreatedAt)
            : a.CreatedAt.CompareTo(b.CreatedAt));
        return selected;
    }

    /// <summary>
    /// Run the checked staged changes against the world in chronological order. Each success marks
    /// the row applied; the matching drafts are discarded on the main thread (the database now
    /// holds the values). Stale rows fail without writing anything.
    /// </summary>
    async void ApplyHistoryAsync(IReadOnlyList<string> fingerprints)
    {
        // Main thread: snapshot everything the worker loop needs.
        var connection = DatabaseConnection();
        if (!connection.World.IsComplete)
        {
            pendingWindow.SetStatus("Connect a world database first.");
            return;
        }

        var ordered = SelectedRecords(fingerprints, revert: false).Where(r => r.Status == ChangeStatus.Staged).ToList();
        if (ordered.Count == 0)
        {
            pendingWindow.SetStatus("Nothing staged is checked.");
            return;
        }

        var staged = CollectStaged().ToDictionary(c => c.Id, StringComparer.Ordinal);
        var world = connection.World;
        var acworld = connection.Acworld;
        int total = ordered.Count;
        PostToMainThread(() => pendingWindow.SetStatus($"Applying {total} change(s)…"));

        var applied = new List<string>();
        string? firstError = null;
        foreach (var record in ordered)
        {
            if (!staged.ContainsKey(record.Fingerprint))
            {
                continue;
            }

            string? error = await WorldApplier.ExecuteAsync(world, record.ApplySql, CancellationToken.None).ConfigureAwait(false);
            if (error != null)
            {
                firstError ??= error;
                try
                {
                    await AcworldStore.MarkFailedAsync(acworld, record.Fingerprint, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }

                continue;
            }

            try
            {
                await AcworldStore.MarkAppliedAsync(acworld, record.Fingerprint, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            applied.Add(record.Fingerprint);
        }

        var done = applied;
        var failed = firstError;
        PostToMainThread(() =>
        {
            var map = CollectStaged().ToDictionary(c => c.Id, StringComparer.Ordinal);
            int ok = 0;
            foreach (string fingerprint in done)
            {
                if (map.TryGetValue(fingerprint, out var change))
                {
                    DiscardPending(change);
                    ok++;
                }
            }

            RefreshPending();
            LoadHistoryAsync();
            string result = $"Applied {ok} of {total} change(s).";
            if (failed != null)
            {
                result += " " + failed;
            }

            statusLog.Post(result);
            pendingWindow.SetStatus(result);
        });
    }

    /// <summary>
    /// Undo the checked applied changes by running their stored revert scripts, newest first.
    /// </summary>
    async void RevertHistoryAsync(IReadOnlyList<string> fingerprints)
    {
        // Main thread: snapshot everything the worker loop needs.
        var connection = DatabaseConnection();
        if (!connection.World.IsComplete)
        {
            pendingWindow.SetStatus("Connect a world database first.");
            return;
        }

        var ordered = SelectedRecords(fingerprints, revert: true).Where(r => r.Status == ChangeStatus.Applied).ToList();
        if (ordered.Count == 0)
        {
            pendingWindow.SetStatus("Nothing applied is checked.");
            return;
        }

        var world = connection.World;
        var acworld = connection.Acworld;
        int total = ordered.Count;
        PostToMainThread(() => pendingWindow.SetStatus($"Reverting {total} change(s)…"));

        int ok = 0;
        string? firstError = null;
        foreach (var record in ordered)
        {
            string? error = await WorldApplier.ExecuteAsync(world, record.RevertSql, CancellationToken.None).ConfigureAwait(false);
            if (error != null)
            {
                firstError ??= error;
                continue;
            }

            try
            {
                await AcworldStore.MarkRevertedAsync(acworld, record.Fingerprint, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }

            ok++;
        }

        int done = ok;
        string? failed = firstError;
        PostToMainThread(() =>
        {
            LoadHistoryAsync();
            string result = $"Reverted {done} of {total} change(s).";
            if (failed != null)
            {
                result += " " + failed;
            }

            statusLog.Post(result);
            pendingWindow.SetStatus(result);
        });
    }

    /// <summary>
    /// Revert one staged change through the owning editor so markers and inspectors refresh. Only
    /// that creature, graveyard or row is touched.
    /// </summary>
    void DiscardPending(PendingChange change)
    {
        switch (change.Kind)
        {
            case PendingKind.Graveyard:
                graveyards.Revert(change.Graveyard!);
                break;
            default:
                drafts.Revert(change.Source, change.Row!);
                break;
        }
    }

    async void DiscardStagedAsync(IReadOnlyList<string> fingerprints)
    {
        // Main thread: snapshot the staged changes; drafts are discarded below on the main thread.
        var set = new HashSet<string>(fingerprints);
        var changes = CollectStaged().Where(c => set.Contains(c.Id)).ToList();
        if (changes.Count == 0)
        {
            return;
        }

        var acworld = DatabaseConnection().Acworld;
        foreach (var change in changes)
        {
            try
            {
                await AcworldStore.DeleteAsync(acworld, change.Id, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        int count = changes.Count;
        PostToMainThread(() =>
        {
            var map = CollectStaged().ToDictionary(c => c.Id, StringComparer.Ordinal);
            foreach (var change in changes)
            {
                if (map.TryGetValue(change.Id, out var current))
                {
                    DiscardPending(current);
                }
            }

            RefreshPending();
            LoadHistoryAsync();
            pendingWindow.SetStatus("Discarded.");
            statusLog.Post($"Discarded {count} staged change(s).");
        });
    }

    void DiscardAllPending()
    {
        var changes = CollectStaged();
        if (changes.Count == 0)
        {
            return;
        }

        Dialogs.Confirm(this, "Discard staged", $"Discard all {changes.Count} staged change(s) and return them to the database values? Applied history is kept.", () =>
        {
            DiscardStagedAsync(changes.Select(c => c.Id).ToList());
        }, ok: "Discard all");
    }

    /// <summary>
    /// Wipe the saved editing state (every draft change with its undo history) and the model
    /// thumbnails of both client profiles. Applied change history stays in acworld; staged rows for
    /// the current drafts are removed. The listfile and the engine's shader caches are left alone.
    /// </summary>
    void ClearCache()
    {
        Dialogs.Confirm(this, "Clear cache", "Clear all saved edits, undo history and model thumbnails? Applied change history stays in the database.", () =>
        {
            ClearCacheAsync();
        }, ok: "Clear cache");
    }

    async void ClearCacheAsync()
    {
        // Main thread: snapshot the staged IDs, then clear drafts up front so no new edit can slip
        // between the snapshot and the database deletes below.
        var stagedIds = CollectStaged().Select(c => c.Id).ToList();
        var acworld = DatabaseConnection().Acworld;
        graveyards.ClearDraft();
        drafts.Clear();
        pendingStore ??= new PendingChanges(ContentDraftFiles.LocalPath("exported.json"));
        pendingStore.ClearExported();
        foreach (string id in stagedIds)
        {
            try
            {
                await AcworldStore.DeleteAsync(acworld, id, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        PostToMainThread(() =>
        {
            // Reset first so no capture is still writing into the folder, then drop the files of both profiles.
            thumbnails?.Reset(ThumbnailDirectory(profile.Kind));
            bool thumbsGone = UserDataReset.TryDeleteDirectory(ProjectSettings.GlobalizePath("user://thumbs"));
            RefreshPending();
            LoadHistoryAsync();
            string result = thumbsGone ? "Cache cleared." : "Cache cleared, but some thumbnails are still in use.";
            pendingWindow.SetStatus(result);
            statusLog.Post(result);
        });
    }

    /// <summary>
    /// Delete the whole user data folder after the viewer exits: settings and saved logins, drafts,
    /// thumbnails, the downloaded listfile, logs and the engine's shader caches, so the next launch
    /// starts as a first run. Change history lives in the database, not in this folder. The delete
    /// is done by a detached helper because the viewer itself holds some of those files open.
    /// Nothing is deleted unless the helper started.
    /// </summary>
    void ResetEverything()
    {
        string folder = OS.GetUserDataDir();
        if (!CanReset(folder))
        {
            statusLog.Post("Reset is not available here.");
            return;
        }

        int unsaved = CollectStaged().Count;
        int scripts = UserDataReset.CountExportedScripts(folder);
        var confirm = new ConfirmationDialog
        {
            Title = "Reset configs and delete cache",
            DialogText = $"Delete everything the viewer saved, then close it?\n\n{folder}\n\n" +
                "This removes your client folders and database logins, all saved edits and undo history " +
                $"({unsaved} staged change(s) in this profile), {scripts} legacy exported SQL script(s), thumbnails, the downloaded listfile, " +
                "logs and shader caches. Applied change history stays in the database. The next launch starts as a first run.\n\nBack up the folder first if you still need the legacy scripts.",
            OkButtonText = "Delete everything and close"
        };
        confirm.AddButton("Open folder", false, "open");
        confirm.CustomAction += action =>
        {
            if (action == "open")
            {
                OS.ShellOpen(folder);
            }
        };
        confirm.Confirmed += async () =>
        {
            confirm.QueueFree();
            await BeginReset(folder);
        };
        confirm.Canceled += confirm.QueueFree;
        AddChild(confirm);
        confirm.PopupCentered();
    }

    /// <summary>Whether the folder is this app's own user folder and this platform can run the cleanup.</summary>
    static bool CanReset(string folder) =>
        OperatingSystem.IsWindows() && UserDataReset.IsSafeTarget(folder, (string)ProjectSettings.GetSetting("application/config/name"));

    /// <summary>
    /// Start the detached cleaner for this process, then quit so it can delete the folder. When the
    /// cleaner cannot start the viewer stays open and nothing is deleted.
    /// </summary>
    async Task<bool> BeginReset(string folder)
    {
        if (!CanReset(folder))
        {
            statusLog.Post("Reset is not available here.");
            return false;
        }

        if (!await UserDataReset.StartCleanerAsync(folder, System.Environment.ProcessId))
        {
            statusLog.Post("Could not start the cleanup. Nothing was deleted.");
            return false;
        }

        statusLog.Post("Closing to delete the saved data.");
        await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        GetTree().Quit();
        return true;
    }

    /// <summary>
    /// Show the combined script the checked changes would export, without saving anything.
    /// </summary>
    void PreviewHistory(IReadOnlyList<string> fingerprints, bool revert)
    {
        var selected = SelectedRecords(fingerprints, revert);
        if (selected.Count == 0)
        {
            pendingWindow.SetStatus("Check one or more changes first.");
            return;
        }

        sqlPreview.OpenText(ChangeLogExport.BuildCombined(selected, revert));
    }

    /// <summary>
    /// Save the checked changes into one SQL file for sharing (apply or revert scripts).
    /// </summary>
    void ExportHistory(IReadOnlyList<string> fingerprints, bool revert)
    {
        var selected = SelectedRecords(fingerprints, revert);
        if (selected.Count == 0)
        {
            pendingWindow.SetStatus("Check one or more changes first.");
            return;
        }

        string combined = ChangeLogExport.BuildCombined(selected, revert);
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.SaveFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Export changes as SQL",
            CurrentFile = $"acworld-export-{(revert ? "revert" : "apply")}.sql"
        };
        dialog.AddFilter("*.sql", "SQL scripts");
        dialog.FileSelected += path =>
        {
            try
            {
                File.WriteAllText(path, combined, new UTF8Encoding(false));
                pendingWindow.SetStatus($"Exported {selected.Count} change(s).");
                statusLog.Post($"Exported {selected.Count} change(s) to {path}.");
            }
            catch (Exception)
            {
                pendingWindow.SetStatus("Could not write the file. Nothing was changed.");
            }

            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(760, 520));
    }

    void SaveNotesAsync(string fingerprint, string notes)
    {
        // Called on the main thread from the notes field; the database write runs on a worker and
        // the cache update is posted back so the worker never touches viewer state.
        var cached = historyCache.FirstOrDefault(r => r.Fingerprint == fingerprint);
        if (cached != null && cached.Notes == notes)
        {
            return;
        }

        var acworld = DatabaseConnection().Acworld;
        _ = SaveNoteAsync(acworld, fingerprint, notes);
    }

    async Task SaveNoteAsync(DatabaseSettings acworld, string fingerprint, string notes)
    {
        try
        {
            await AcworldStore.UpdateNotesAsync(acworld, fingerprint, notes, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            PostToMainThread(() => pendingWindow.SetStatus("Could not save the note."));
            return;
        }

        PostToMainThread(() =>
        {
            historyCache = historyCache.Select(r => r.Fingerprint == fingerprint ? r with { Notes = notes } : r).ToList();
        });
    }
}
