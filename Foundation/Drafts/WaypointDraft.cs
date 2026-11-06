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
    /// True when this differs from <paramref name="other"/> only in properties a dialect stores.
    /// TrinityCore's waypoint_path_node holds just facing and delay; the rest belongs to the path or
    /// does not exist. Row-edit commands enforce the same rule by column, so this stays for the
    /// display model only.
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
/// Everything an edit can change about one waypoint: the display model of a node row.
/// </summary>
public readonly record struct WaypointState(WaypointPosition Position, WaypointProps Props)
{
    public WaypointState ToColumn() => new(Position.ToColumn(), Props.ToColumn());
}
