namespace ACWorldEdit.Foundation;

/// <summary>
/// One graveyard_zone row: GhostZone is an AreaTable ID (the server checks the ghost's area first,
/// then its zone); Faction is 0 neutral, 469 Alliance or 67 Horde.
/// </summary>
public sealed record GraveyardLink(uint GhostZone, ushort Faction, string Comment)
{
    public const ushort Neutral = 0, Alliance = 469, Horde = 67;
}

/// <summary>
/// One game_graveyard row (server coordinates, no orientation) with its graveyard_zone links. Links
/// compare as a set ordered by GhostZone, so equality is value-based and order-independent once
/// normalized.
/// </summary>
public sealed record GraveyardRecord(uint Id, ushort Map, double X, double Y, double Z, string Comment, GraveyardLink[] Links)
{
    /// <summary>
    /// game_graveyard.Comment is varchar(255).
    /// </summary>
    public const int MaxCommentLength = 255;

    public bool Equals(GraveyardRecord? other) => other is not null && Id == other.Id && Map == other.Map && X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && Comment == other.Comment && Links.SequenceEqual(other.Links);

    public override int GetHashCode() => HashCode.Combine(Id, Map, X, Y, Z, Comment, Links.Length);

    /// <summary>
    /// Sort links by GhostZone and replace null comments, so database rows and drafts compare equal.
    /// </summary>
    public GraveyardRecord Normalized() => this with
    {
        Comment = Comment ?? "",
        Links = (Links ?? []).Select(l => l with { Comment = l.Comment ?? "" }).OrderBy(l => l.GhostZone).ToArray()
    };

    /// <summary>
    /// Mirror the server's graveyard loading rules (GameGraveyard.cpp): finite coordinates, a player
    /// faction or neutral, no duplicate (ID, GhostZone) link and, when a validator is supplied, a
    /// GhostZone that exists in AreaTable. Messages are safe to show in the UI.
    /// </summary>
    public void Validate(Func<uint, bool>? zoneExists = null)
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Z))
        {
            throw new InvalidDataException("Graveyard coordinates must be finite.");
        }

        if (Comment == null || Comment.Length > MaxCommentLength)
        {
            throw new InvalidDataException($"Graveyard name must be at most {MaxCommentLength} characters.");
        }

        var zones = new HashSet<uint>();
        foreach (var link in Links ?? [])
        {
            if (link.Faction is not (GraveyardLink.Neutral or GraveyardLink.Alliance or GraveyardLink.Horde))
            {
                throw new InvalidDataException($"Zone {link.GhostZone}: faction must be 0 (neutral), 469 (Alliance) or 67 (Horde).");
            }

            if (!zones.Add(link.GhostZone))
            {
                throw new InvalidDataException($"Zone {link.GhostZone} is linked twice; the server rejects duplicates.");
            }

            if (zoneExists?.Invoke(link.GhostZone) == false)
            {
                throw new InvalidDataException($"Zone {link.GhostZone} does not exist in AreaTable; the server skips it.");
            }
        }
    }
}

/// <summary>
/// One local history command for a graveyard; Before/After are whole normalized records.
/// </summary>
public sealed record GraveyardEdit(string Source, uint Id, GraveyardRecord Before, GraveyardRecord After);

/// <summary>
/// Local graveyard draft, independent of scene nodes and the database, persisted by DraftHistory.
/// Follows the creature draft rules: the earliest applied edit's Before must still equal the source
/// row, or the draft is reported as a conflict and not applied; a new edit discards the redo branch.
/// </summary>
public sealed class GraveyardDraft : IDisposable
{
    readonly DraftHistory<GraveyardEdit> history;
    public bool CanUndo => history.CanUndo;
    public bool CanRedo => history.CanRedo;

    public GraveyardDraft(string path) => history = new(path, "graveyard", Check);

    /// <summary>
    /// Verify and normalize every recovered record. Zone existence is not rechecked here because it
    /// needs client data; edits were validated when made.
    /// </summary>
    static GraveyardEdit Check(GraveyardEdit edit)
    {
        var before = edit.Before.Normalized();
        var after = edit.After.Normalized();
        before.Validate();
        after.Validate();
        if (before.Id != edit.Id || after.Id != edit.Id || before.Map != after.Map)
        {
            throw new InvalidDataException("Inconsistent graveyard draft.");
        }

        return edit with { Before = before, After = after };
    }

    IEnumerable<GraveyardEdit> Applied(string source, GraveyardRecord original) => history.State.AppliedEntries().Where(e => e.Source == source && e.Id == original.Id && e.Before.Map == original.Map);

    /// <summary>
    /// Last applied edit when its first baseline still equals the source row; otherwise the source.
    /// </summary>
    public GraveyardRecord Effective(string source, GraveyardRecord original)
    {
        original = original.Normalized();
        var edits = Applied(source, original).ToArray();
        return edits.Length > 0 && edits[0].Before == original ? edits[^1].After : original;
    }

    public bool Conflict(string source, GraveyardRecord original)
    {
        original = original.Normalized();
        var first = Applied(source, original).FirstOrDefault();
        return first != null && first.Before != original;
    }

    /// <summary>
    /// Validate and save one edit. Identity and map cannot change; a stale baseline is refused; a
    /// no-op adds no history.
    /// </summary>
    public void Edit(string source, GraveyardRecord original, GraveyardRecord after, Func<uint, bool>? zoneExists = null)
    {
        original = original.Normalized();
        after = after.Normalized();
        after.Validate(zoneExists);
        if (after.Id != original.Id || after.Map != original.Map)
        {
            throw new InvalidDataException("A graveyard edit cannot change its ID or map.");
        }

        if (Conflict(source, original))
        {
            throw new InvalidDataException("Source graveyard changed; draft requires review.");
        }

        var before = Effective(source, original);
        if (after != before)
        {
            history.Append(new GraveyardEdit(source, original.Id, before, after));
        }
    }

    /// <summary>
    /// Net change per (source, ID, map) over the applied history, omitting edits that return to
    /// the baseline.
    /// </summary>
    public IReadOnlyList<GraveyardEdit> NetEdits() => history.State.AppliedEntries()
        .GroupBy(e => (e.Source, e.Id, e.Before.Map))
        .Select(g => new GraveyardEdit(g.Key.Source, g.Key.Id, g.First().Before, g.Last().After))
        .Where(e => e.Before != e.After)
        .ToList();

    /// <summary>
    /// Discard one net change as a normal undoable command that returns the graveyard to its baseline.
    /// </summary>
    public void Revert(GraveyardEdit net) => history.Append(new GraveyardEdit(net.Source, net.Id, net.After, net.Before));

    public void Undo() => history.Undo();

    public void Redo() => history.Redo();

    public void Dispose() => history.Dispose();
}
