namespace ACWorldEdit.Foundation;

/// <summary>
/// A waypoint_data position in server coordinates. The columns are FLOAT, so edited values are
/// rounded to float precision; otherwise SQL guards compared against a double would read STALE.
/// </summary>
public readonly record struct WaypointPosition(double X, double Y, double Z)
{
    public WaypointPosition ToColumn() => new((float)X, (float)Y, (float)Z);

    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
}

/// <summary>
/// The editable waypoint_data columns other than the position. Orientation is NULL when the point
/// has no facing (the server only applies it to points with a delay); Velocity 0 means the
/// creature's own speed; MoveType is the server's WaypointMoveType (0 walk, 1 run, 2 land,
/// 3 take off, anything above is rejected on load); Action is a waypoint_scripts ID.
/// </summary>
public readonly record struct WaypointProps(double? Orientation, double Velocity, uint Delay, bool Smooth, int MoveType, int Action, int ActionChance)
{
    public const int MaxMoveType = 3;

    /// <summary>
    /// True when this differs from <paramref name="other"/> only in properties the dialect can store.
    /// TrinityCore's waypoint_path_node holds just facing and delay; the rest belongs to the path or
    /// does not exist.
    /// </summary>
    public bool EditableOn(WorldDialect dialect, WaypointProps other) =>
        dialect != WorldDialect.TrinityCore || this with { Orientation = other.Orientation, Delay = other.Delay } == other;

    public WaypointProps ToColumn() => this with { Orientation = Orientation is double o ? (float)o : null, Velocity = (float)Velocity };

    /// <summary>
    /// Mirror the server's loading rules and the column types; messages are safe to show in the UI.
    /// </summary>
    public void Validate()
    {
        if (Orientation is double o && !double.IsFinite(o))
        {
            throw new InvalidDataException("Facing must be a number.");
        }

        if (!double.IsFinite(Velocity) || Velocity < 0)
        {
            throw new InvalidDataException("Velocity must be zero or more.");
        }

        if (MoveType is < 0 or > MaxMoveType)
        {
            throw new InvalidDataException("Move type must be 0 (walk), 1 (run), 2 (land) or 3 (take off); the server skips other values.");
        }

        if (ActionChance is < short.MinValue or > short.MaxValue)
        {
            throw new InvalidDataException("Action chance must fit a 16-bit number.");
        }
    }
}

/// <summary>
/// Everything an edit can change about one waypoint.
/// </summary>
public readonly record struct WaypointState(WaypointPosition Position, WaypointProps Props)
{
    public WaypointState ToColumn() => new(Position.ToColumn(), Props.ToColumn());
}

/// <summary>
/// One local history command: point Point of path Path changed from Before to After. The (id, point)
/// key never changes, so exports are plain guarded UPDATEs. Draft files written before properties
/// were editable carry no props: both sides then mean "properties as in the source", which is why
/// the props are nullable and the JSON names stay as they were.
/// </summary>
public sealed record WaypointEdit(string Source, uint Path, uint Point, WaypointPosition Before, WaypointPosition After, WaypointProps? BeforeProps = null, WaypointProps? AfterProps = null)
{
    /// <summary>
    /// Both full states, filling props from a source baseline when this edit has none.
    /// </summary>
    public (WaypointState Before, WaypointState After) Resolve(WaypointProps source) =>
        (new(Before, BeforeProps ?? source), new(After, AfterProps ?? source));

    /// <summary>
    /// True when the edit changes anything, comparing props only where both sides carry them.
    /// </summary>
    public bool Changes => Before != After || (BeforeProps is { } b && AfterProps is { } a && b != a);
}

/// <summary>
/// Local waypoint_data draft, independent of scene nodes and the database, persisted by
/// DraftHistory. Follows the creature draft rules: the earliest applied edit's Before must still
/// equal the source row, or the draft is reported as a conflict and not applied.
/// </summary>
public sealed class WaypointDraft : IDisposable
{
    readonly DraftHistory<WaypointEdit> history;
    public bool CanUndo => history.CanUndo;
    public bool CanRedo => history.CanRedo;

    public WaypointDraft(string path) => history = new(path, "waypoint", Check);

    static WaypointEdit Check(WaypointEdit edit)
    {
        if (!edit.Before.IsFinite || !edit.After.IsFinite)
        {
            throw new InvalidDataException("Waypoint coordinates must be finite.");
        }

        edit.BeforeProps?.Validate();
        edit.AfterProps?.Validate();
        return edit with { Before = edit.Before.ToColumn(), After = edit.After.ToColumn(), BeforeProps = edit.BeforeProps?.ToColumn(), AfterProps = edit.AfterProps?.ToColumn() };
    }

    IEnumerable<WaypointEdit> Applied(string source, uint path, uint point) => history.State.AppliedEntries().Where(e => e.Source == source && e.Path == path && e.Point == point);

    /// <summary>
    /// Last applied state when its first baseline still equals the source; otherwise the source.
    /// </summary>
    public WaypointState Effective(string source, uint path, uint point, WaypointState original)
    {
        original = original.ToColumn();
        var edits = Applied(source, path, point).ToArray();
        return edits.Length > 0 && edits[0].Resolve(original.Props).Before == original ? edits[^1].Resolve(original.Props).After : original;
    }

    public bool Conflict(string source, uint path, uint point, WaypointState original)
    {
        original = original.ToColumn();
        var first = Applied(source, path, point).FirstOrDefault();
        return first != null && first.Resolve(original.Props).Before != original;
    }

    /// <summary>
    /// Validate and save one edit. A stale baseline is refused; a no-op adds no history.
    /// </summary>
    public void Edit(string source, uint path, uint point, WaypointState original, WaypointState after)
    {
        if (!after.Position.IsFinite)
        {
            throw new InvalidDataException("Waypoint coordinates must be finite.");
        }

        after.Props.Validate();
        original = original.ToColumn();
        after = after.ToColumn();
        if (Conflict(source, path, point, original))
        {
            throw new InvalidDataException("Source waypoint changed; draft requires review.");
        }

        var before = Effective(source, path, point, original);
        if (after != before)
        {
            history.Append(new WaypointEdit(source, path, point, before.Position, after.Position, before.Props, after.Props));
        }
    }

    /// <summary>
    /// Net change per (source, path, point) over the applied history, omitting edits that return to
    /// the baseline. When only some of the history carries props, the baseline props come from the
    /// first edit that has them (legacy edits never changed props, so that is the source).
    /// </summary>
    public IReadOnlyList<WaypointEdit> NetEdits() => history.State.AppliedEntries()
        .GroupBy(e => (e.Source, e.Path, e.Point))
        .Select(g =>
        {
            var before = g.Select(e => e.BeforeProps).FirstOrDefault(p => p != null);
            var after = g.Last().AfterProps;
            return new WaypointEdit(g.Key.Source, g.Key.Path, g.Key.Point, g.First().Before, g.Last().After, after == null ? null : before, after);
        })
        .Where(e => e.Changes)
        .OrderBy(e => e.Path).ThenBy(e => e.Point)
        .ToList();

    public void Clear() => history.Clear();

    /// <summary>
    /// Discard one net change as a normal undoable command that returns the point to its baseline.
    /// </summary>
    public void Revert(WaypointEdit net) => history.Append(new WaypointEdit(net.Source, net.Path, net.Point, net.After, net.Before, net.AfterProps, net.BeforeProps));

    public void Undo() => history.Undo();

    public void Redo() => history.Redo();

    public void Dispose() => history.Dispose();
}
