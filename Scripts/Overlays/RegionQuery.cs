using ACWorldEdit.Data;

namespace ACWorldEdit;

/// <summary>
/// The one-slot, cancellable, generation-checked database query shared by the creature, GameObject,
/// graveyard and AreaTrigger overlays. Restart cancels the running query and bumps the generation;
/// only a result whose generation is still current is published, and a replacement starts only after
/// the previous task has finished, so each layer has at most one query in flight. All members run on
/// the main thread; the factory itself runs on a worker, so callers capture every UI value first.
/// </summary>
public sealed class RegionQuery<T> : IDisposable
{
    Task<T>? pending;
    CancellationTokenSource cancellation = new();
    int generation, pendingGeneration, requested = -1;
    long searchChanged;
    bool searchPending;

    public bool IsLoading => pending != null;
    /// <summary>
    /// No task running and the current generation already requested (nothing left to start).
    /// </summary>
    public bool IsIdle => pending == null && requested == generation;
    /// <summary>
    /// A restarted generation is waiting for the single query slot to be free.
    /// </summary>
    public bool WantsStart => requested != generation && pending == null;

    /// <summary>
    /// Cancel the running query and invalidate its result.
    /// </summary>
    public void Restart()
    {
        generation++;
        cancellation.Cancel();
        cancellation.Dispose();
        cancellation = new();
    }

    /// <summary>
    /// Debounce a search box: Restart-worthy once the text has been still for 0.3 s.
    /// </summary>
    public void MarkSearchChanged()
    {
        searchPending = true;
        searchChanged = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// True once, when a pending search edit has settled.
    /// </summary>
    public bool TakeSettledSearch()
    {
        if (!searchPending || System.Diagnostics.Stopwatch.GetElapsedTime(searchChanged).TotalSeconds < 0.3)
        {
            return false;
        }

        searchPending = false;
        return true;
    }

    /// <summary>
    /// Publish a finished current-generation result, or report its failure through onError. Results
    /// of cancelled or superseded queries are dropped silently.
    /// </summary>
    public void Poll(Action<T> publish, Action<string> onError)
    {
        if (pending is not { IsCompleted: true })
        {
            return;
        }

        try
        {
            var result = pending.GetAwaiter().GetResult();
            if (pendingGeneration == generation)
            {
                publish(result);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (pendingGeneration == generation)
            {
                onError(DbErrors.Safe(error));
            }
        }

        pending = null;
    }

    /// <summary>
    /// Start the current generation on a worker. Call only when WantsStart.
    /// </summary>
    public void Start(Func<CancellationToken, Task<T>> read)
    {
        requested = pendingGeneration = generation;
        var token = cancellation.Token;
        pending = PerfDiagnostics.Enabled ? Task.Run(() => Timed(read, token), token) : Task.Run(() => read(token), token);
    }

    /// <summary>The query wrapped with a stopwatch for --perf-smoke; never used in normal viewing.</summary>
    static async Task<T> Timed(Func<CancellationToken, Task<T>> read, CancellationToken token)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        bool cancelled = true;
        try
        {
            var result = await read(token).ConfigureAwait(false);
            cancelled = token.IsCancellationRequested;
            return result;
        }
        finally
        {
            PerfDiagnostics.Queries.Enqueue((typeof(T).Name, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, cancelled));
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        cancellation.Dispose();
    }
}
