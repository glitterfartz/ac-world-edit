namespace ACWorldEdit.Foundation;

/// <summary>
/// One local history command isolated by database identity and GUID. Before/After are server
/// poses; commands never own or write database rows.
/// </summary>
public sealed record CreatureMove(string Source, uint Guid, WorldPose Before, WorldPose After);

/// <summary>
/// Position-only draft independent of scene nodes and the database, persisted by DraftHistory. Source
/// identity and the first baseline prevent replaying stale edits.
/// </summary>
public sealed class CreatureDraft : IDisposable
{
    readonly DraftHistory<CreatureMove> history;
    public bool CanUndo => history.CanUndo;
    public bool CanRedo => history.CanRedo;

    public CreatureDraft(string path) => history = new(path, "creature", Check);

    /// <summary>
    /// Pose and map constraints for recovered history; invalid files are rejected, not reset.
    /// </summary>
    static CreatureMove Check(CreatureMove move)
    {
        move.Before.Validate();
        move.After.Validate();
        if (move.Before.Map != move.After.Map)
        {
            throw new InvalidDataException("Cross-map creature draft.");
        }

        return move;
    }

    IEnumerable<CreatureMove> Applied(string source, uint guid, WorldPose original) => history.State.AppliedEntries().Where(m => m.Source == source && m.Guid == guid && m.Before.Map == original.Map);

    /// <summary>
    /// Resolve the last applied move only when its first baseline still equals the source pose.
    /// Conflicts return the source position so outdated drafts cannot silently win.
    /// </summary>
    public WorldPose Effective(string source, uint guid, WorldPose original)
    {
        var moves = Applied(source, guid, original).ToArray();
        return moves.Length > 0 && moves[0].Before == original ? moves[^1].After : original;
    }

    /// <summary>
    /// Compare the earliest applied command for this source/GUID/map with the current database
    /// baseline. Undone commands do not create an active conflict.
    /// </summary>
    public bool Conflict(string source, uint guid, WorldPose original)
    {
        var first = Applied(source, guid, original).FirstOrDefault();
        return first != null && first.Before != original;
    }

    /// <summary>
    /// Validate a same-map movement, reject a stale baseline and save a command after discarding any
    /// redo branch. No-op moves do not add history.
    /// </summary>
    public void Move(string source, uint guid, WorldPose original, WorldPose after)
    {
        after.Validate();
        if (after.Map != original.Map)
        {
            throw new InvalidDataException("Cross-map move.");
        }

        if (Conflict(source, guid, original))
        {
            throw new InvalidDataException("Source position changed; draft requires review.");
        }

        var before = Effective(source, guid, original);
        if (after != before)
        {
            history.Append(new CreatureMove(source, guid, before, after));
        }
    }

    public void Undo() => history.Undo();

    public void Redo() => history.Redo();

    public void Dispose() => history.Dispose();
}
