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
    string? historyError;
    string? historySourceKey;
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
    /// A newly applied connection invalidates the history cache and the schema check: records from
    /// the previous world must never stay actionable, and the new database may need a schema check.
    /// </summary>
    void ConnectionChanged()
    {
        AcworldStore.InvalidateSchemaCache();
        historyCache = [];
        historyError = null;
        historySourceKey = null;
        historyLoading = false;
        if (pendingWindow != null)
        {
            // The old rows belong to the previous connection; clear them so no stale Apply/Revert
            // button stays visible even for the few seconds before the next reload.
            pendingWindow.RefreshHistory([], "Connection changed. Refresh the history.", CurrentSourceKey());
        }
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
                historyError = "Connect a world database to track changes.";
                historySourceKey = null;
                pendingWindow.RefreshHistory(historyCache, historyError, null);
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
                    historyError = error;
                    historySourceKey = sourceKey;
                    pendingWindow.RefreshHistory(historyCache, error, sourceKey);
                });
                return;
            }

            List<ChangeRecord> existing;
            try
            {
                // Include every source so notes and timestamps are preserved for staged rows written
                // from another open world, even while this window only shows the current source.
                existing = await AcworldStore.ListAsync(acworld, sourceKey, includeAllSources: true, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception listError)
            {
                error = "Change history is unreachable: " + CleanError(listError, acworld);
                var cached = historyCache;
                PostToMainThread(() =>
                {
                    historyError = error;
                    pendingWindow.RefreshHistory(cached, error, sourceKey);
                });
                return;
            }

            var byChangeKey = new Dictionary<string, ChangeRecord>(StringComparer.Ordinal);
            foreach (var record in existing)
            {
                byChangeKey.TryAdd(record.ChangeKey, record);
                byChangeKey.TryAdd(record.Fingerprint, record);
            }

            var toStage = ChangeLogMerge.MergeStaged(staged, byChangeKey, SchemaFor, DialectFor, now);
            var changed = toStage.Where(record =>
            {
                if (!byChangeKey.TryGetValue(record.ChangeKey, out var prior))
                {
                    return true;
                }

                // The script header is stable, so an unchanged draft produces identical SQL. Skip
                // the write: the five-second poll must not rewrite every row and rebuild the window.
                return prior.Label != record.Label || prior.ApplySql != record.ApplySql ||
                    prior.RevertSql != record.RevertSql || prior.Dialect != record.Dialect;
            }).ToList();

            if (changed.Count > 0)
            {
                error = await ChangeHistoryOperations.StageAsync(
                    AcworldHistoryStore.Instance, acworld, changed, e => CleanError(e, acworld), CancellationToken.None).ConfigureAwait(false);
            }

            if (sourceKey != null)
            {
                try
                {
                    // Remove staged rows for this source that no draft owns any more. Applied and
                    // reverted history is never touched; another source is left alone.
                    await AcworldStore.ReconcileStagedAsync(
                        acworld, sourceKey, toStage.Select(r => r.ChangeKey).ToList(), CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception reconcileError)
                {
                    error ??= "Some old staged changes could not be cleaned up. " + CleanError(reconcileError, acworld);
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
                PostToMainThread(() =>
                {
                    historyError = error;
                    pendingWindow.RefreshHistory(cached, error, sourceKey);
                });
                return;
            }

            var snapshot = records;
            PostToMainThread(() =>
            {
                historyCache = snapshot;
                historyError = error;
                historySourceKey = sourceKey;
                pendingWindow.RefreshHistory(historyCache, error, sourceKey);
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
        // A stable secondary key keeps same-instant records in a defined order; the history promises
        // chronological order and the database assigns one timestamp per refresh batch.
        selected.Sort((a, b) =>
        {
            int byTime = revert ? b.CreatedAt.CompareTo(a.CreatedAt) : a.CreatedAt.CompareTo(b.CreatedAt);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.ChangeKey, b.ChangeKey);
        });
        return selected;
    }

    /// <summary>
    /// The checked records that may run against the open world. Rows from another source are
    /// reported and never reach the applier, and a known history failure or a stale cache disables
    /// the operation instead of running SQL from an unverified list.
    /// </summary>
    bool CanRunHistory(IReadOnlyList<ChangeRecord> requested, string? sourceKey, out List<ChangeRecord> runnable, out string? problem)
    {
        runnable = [];
        problem = null;
        if (sourceKey == null)
        {
            problem = "Connect a world database first.";
            return false;
        }

        if (!string.IsNullOrEmpty(historyError))
        {
            problem = "Change history is unavailable: " + historyError;
            return false;
        }

        if (historySourceKey != sourceKey)
        {
            problem = "Change history is out of date for this connection. Refresh before applying.";
            return false;
        }

        var (allowed, foreign) = ChangeLogExport.ForSource(requested, sourceKey);
        runnable = allowed.ToList();
        if (foreign > 0)
        {
            problem = $"{foreign} selected change(s) belong to another world.";
        }

        return runnable.Count > 0;
    }

    /// <summary>
    /// Run the checked staged changes against the world in chronological order. Each success marks
    /// the row applied; the matching drafts are discarded on the main thread (the database now
    /// holds the values). Stale rows fail without writing anything. The locally generated script is
    /// always preferred over the stored copy.
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

        string? sourceKey = CurrentSourceKey();
        var selected = SelectedRecords(fingerprints, revert: false).Where(r => r.Status == ChangeStatus.Staged).ToList();
        if (!CanRunHistory(selected, sourceKey, out var ordered, out string? sourceProblem))
        {
            pendingWindow.SetStatus(sourceProblem ?? "Nothing staged is checked.");
            return;
        }

        if (sourceProblem != null)
        {
            pendingWindow.SetStatus(sourceProblem);
        }

        var staged = CollectStaged().ToDictionary(c => c.ChangeKey, StringComparer.Ordinal);
        var localScripts = new Dictionary<string, ChangeRecord>(StringComparer.Ordinal);
        var schemas = staged.Values.Select(c => c.Source).Distinct(StringComparer.Ordinal)
            .ToDictionary(s => s, SourceDialects.SchemaFor, StringComparer.Ordinal);
        DateTime now = DateTime.UtcNow;
        foreach (var change in staged.Values)
        {
            localScripts[change.ChangeKey] = ChangeLogMerge.BuildRecord(
                change, "", now, now, s => schemas.TryGetValue(s, out var schema) ? schema : WorldSchemas.For(WorldDialect.AzerothCore),
                SourceDialects.DialectOf);
        }

        var world = connection.World;
        var acworld = connection.Acworld;
        int total = ordered.Count;
        PostToMainThread(() => pendingWindow.SetStatus($"Applying {total} change(s)…"));

        var outcome = await ChangeHistoryOperations.ApplyAsync(
            ordered,
            localScripts,
            script => WorldApplier.ExecuteAsync(world, script, CancellationToken.None),
            AcworldHistoryStore.Instance,
            acworld,
            UserSettings.ClientId(),
            e => CleanError(e, acworld),
            CancellationToken.None).ConfigureAwait(false);

        var done = outcome.CompletedChangeKeys;
        string? failed = sourceProblem == null ? outcome.Error
            : outcome.Error == null ? sourceProblem
            : sourceProblem + " " + outcome.Error;
        PostToMainThread(() =>
        {
            var map = CollectStaged().ToDictionary(c => c.ChangeKey, StringComparer.Ordinal);
            int ok = 0;
            foreach (string changeKey in done)
            {
                if (map.TryGetValue(changeKey, out var change))
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

        string? sourceKey = CurrentSourceKey();
        var selected = SelectedRecords(fingerprints, revert: true).Where(r => r.Status == ChangeStatus.Applied).ToList();
        if (!CanRunHistory(selected, sourceKey, out var ordered, out string? sourceProblem))
        {
            pendingWindow.SetStatus(sourceProblem ?? "Nothing applied is checked.");
            return;
        }

        if (sourceProblem != null)
        {
            pendingWindow.SetStatus(sourceProblem);
        }

        var world = connection.World;
        var acworld = connection.Acworld;
        int total = ordered.Count;
        PostToMainThread(() => pendingWindow.SetStatus($"Reverting {total} change(s)…"));

        var outcome = await ChangeHistoryOperations.RevertAsync(
            ordered,
            UserSettings.ClientId(),
            script => WorldApplier.ExecuteAsync(world, script, CancellationToken.None),
            AcworldHistoryStore.Instance,
            acworld,
            e => CleanError(e, acworld),
            CancellationToken.None).ConfigureAwait(false);

        int done = outcome.CompletedChangeKeys.Count;
        string? failed = sourceProblem == null ? outcome.Error
            : outcome.Error == null ? sourceProblem
            : sourceProblem + " " + outcome.Error;
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
        // Only a staged row may be deleted: an applied/reverted row with the same local draft is
        // history and must survive the discard.
        var outcome = await ChangeHistoryOperations.DiscardAsync(
            changes, AcworldHistoryStore.Instance, acworld, e => CleanError(e, acworld), CancellationToken.None).ConfigureAwait(false);

        int count = outcome.DeletedChangeKeys.Count;
        string? failed = outcome.Error;
        PostToMainThread(() =>
        {
            var map = CollectStaged().ToDictionary(c => c.ChangeKey, StringComparer.Ordinal);
            foreach (string changeKey in outcome.DeletedChangeKeys)
            {
                if (map.TryGetValue(changeKey, out var current))
                {
                    DiscardPending(current);
                }
            }

            RefreshPending();
            LoadHistoryAsync();
            string result = failed == null ? $"Discarded {count} staged change(s)." : failed;
            pendingWindow.SetStatus(result);
            statusLog.Post(failed == null ? $"Discarded {count} staged change(s)." : result);
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
    /// Remove finished history older than the chosen age for the open world. Applied, reverted and
    /// failed rows are deleted; staged drafts stay, and other worlds are left alone.
    /// </summary>
    async void DeleteOldHistoryAsync(int days)
    {
        var connection = DatabaseConnection();
        string? sourceKey = connection.World.IsComplete ? CurrentSourceKey() : null;
        if (sourceKey == null)
        {
            pendingWindow.SetStatus("Connect a world database first.");
            return;
        }

        var acworld = connection.Acworld;
        DateTime olderThan = days <= 0 ? DateTime.UtcNow : DateTime.UtcNow.AddDays(-days);
        pendingWindow.SetStatus("Deleting changes…");
        var outcome = await ChangeHistoryOperations.DeleteOldAsync(
            AcworldHistoryStore.Instance, acworld, sourceKey, olderThan,
            e => CleanError(e, acworld), CancellationToken.None).ConfigureAwait(false);

        PostToMainThread(() =>
        {
            LoadHistoryAsync();
            string result = outcome.Error ?? $"Deleted {outcome.Deleted} change(s).";
            pendingWindow.SetStatus(result);
            statusLog.Post(result);
        });
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
        // Main thread: snapshot the staged rows. Delete only staged rows; applied/reverted history
        // stays even when a matching local draft exists.
        var stagedIds = CollectStaged().Select(c => c.Id).ToList();
        var acworld = DatabaseConnection().Acworld;
        string? firstError = null;
        foreach (string id in stagedIds)
        {
            try
            {
                await AcworldStore.DeleteStagedAsync(acworld, id, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                firstError ??= "Could not clear the staged history (" + CleanError(error, acworld) + "). Nothing was deleted.";
            }
        }

        if (firstError != null)
        {
            string failure = firstError;
            PostToMainThread(() =>
            {
                pendingWindow.SetStatus(failure);
                statusLog.Post(failure);
            });
            return;
        }

        graveyards.ClearDraft();
        drafts.Clear();
        pendingStore ??= new PendingChanges(ContentDraftFiles.LocalPath("exported.json"));
        pendingStore.ClearExported();

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
            string? historyError = await DeleteStagedHistoryForResetAsync();
            if (historyError != null)
            {
                statusLog.Post(historyError);
                pendingWindow.SetStatus(historyError);
                return;
            }

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
    /// Remove every staged history row for both client profiles before the drafts folder is deleted.
    /// A failure aborts the reset: otherwise the viewer would leave permanent ghost rows behind.
    /// </summary>
    async Task<string?> DeleteStagedHistoryForResetAsync()
    {
        var targets = new List<DatabaseSettings>();
        var current = DatabaseConnection();
        if (current.World.IsComplete)
        {
            targets.Add(current.Acworld);
        }

        WorldDialect other = current.Dialect == WorldDialect.TrinityCore ? WorldDialect.AzerothCore : WorldDialect.TrinityCore;
        try
        {
            var saved = UserSettings.LoadDatabase(other);
            if (saved.World.IsComplete)
            {
                targets.Add(saved.Acworld);
            }
        }
        catch
        {
            // A damaged other profile must not block resetting this one.
        }

        return await ChangeHistoryOperations.ResetAsync(
            targets.Distinct(), AcworldHistoryStore.Instance, (settings, error) => CleanError(error, settings), CancellationToken.None).ConfigureAwait(false);
    }

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

        // Preview each source under its own header rather than presenting mixed databases as one
        // runnable script.
        var groups = ChangeLogExport.GroupBySource(selected);
        string text = groups.Count == 1
            ? ChangeLogExport.BuildCombined(groups[0].Records, revert)
            : string.Join("\n\n", groups.Select(g =>
                $"-- ===== Source {g.SourceKey} =====\n" + ChangeLogExport.BuildCombined(g.Records, revert)));
        sqlPreview.OpenText(text);
    }

    /// <summary>
    /// Save the checked changes as SQL. One source writes one file; a show-all selection writes one
    /// file per source so a runnable script never mixes two databases.
    /// </summary>
    void ExportHistory(IReadOnlyList<string> fingerprints, bool revert)
    {
        var selected = SelectedRecords(fingerprints, revert);
        if (selected.Count == 0)
        {
            pendingWindow.SetStatus("Check one or more changes first.");
            return;
        }

        var groups = ChangeLogExport.GroupBySource(selected);
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
                var written = new List<string>();
                foreach (var group in groups)
                {
                    string target = groups.Count == 1
                        ? path
                        : Path.Combine(
                            Path.GetDirectoryName(path) ?? "",
                            Path.GetFileNameWithoutExtension(path) + "-" + ChangeLogExport.FileSuffix(group.SourceKey) + ".sql");
                    File.WriteAllText(target, ChangeLogExport.BuildCombined(group.Records, revert), new UTF8Encoding(false));
                    written.Add(target);
                }

                string message = groups.Count == 1
                    ? $"Exported {selected.Count} change(s) to {written[0]}."
                    : $"Exported {selected.Count} change(s) from {groups.Count} worlds into separate files.";
                pendingWindow.SetStatus(message);
                statusLog.Post(message);
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
