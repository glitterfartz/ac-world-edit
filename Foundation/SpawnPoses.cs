using System.Globalization;

namespace ACWorldEdit.Foundation;

/// <summary>
/// Where the creature and GameObject overlays keep the poses of existing spawns: as updates of the
/// `creature` / `gameobject` rows on the shared content draft, so a move shares one undo history and
/// one export with every other edit and a move followed by a delete collapses to a delete of the
/// database row. The overlays only know a spawn's pose, so the full database row is loaded when a
/// spawn is selected (<see cref="Load"/>) and is the baseline an edit replaces.
/// </summary>
public sealed class SpawnPoses
{
    /// <summary>A pose closer than this to the one in a draft's baseline is the same pose (float columns).</summary>
    const double Tolerance = 0.0001;

    readonly ContentDrafts drafts;
    readonly SpawnKind kind;
    readonly Dictionary<string, RowSet> snapshots = [];
    Dictionary<(string Source, ulong Guid), RowEdit>? net;

    public SpawnPoses(ContentDrafts drafts, SpawnKind kind)
    {
        this.drafts = drafts;
        this.kind = kind;
        drafts.Changed += _ => net = null;
    }

    public bool Available => drafts.Draft != null;
    public bool CanUndo => drafts.Draft?.CanUndo == true;
    public bool CanRedo => drafts.Draft?.CanRedo == true;

    string TableName => kind == SpawnKind.Creature ? "creature" : "gameobject";

    TableSchema Table(string source) => drafts.Draft!.Tables(source).Find(TableName);

    RowSet Snapshot(string source) => snapshots.TryGetValue(source, out var rows) ? rows : snapshots[source] = new RowSet();

    /// <summary>Remember database rows as the baseline of later edits (replaces a row loaded before).</summary>
    public void Load(string source, IEnumerable<Row> rows)
    {
        if (!Available)
        {
            return;
        }

        var table = Table(source);
        foreach (var row in rows)
        {
            Snapshot(source).Set(table, row);
        }
    }

    /// <summary>Remember the spawn rows of a database read as the baseline of later edits.</summary>
    public void Load(string source, RowSet rows)
    {
        if (Available)
        {
            Load(source, rows.All(Table(source)));
        }
    }

    /// <summary>Whether the database row of a spawn is loaded, so it can be edited.</summary>
    public bool Has(string source, ulong guid) => Available && Snapshot(source).Get(Table(source), Key(guid)) != null;

    static string Key(ulong guid) => guid.ToString(CultureInfo.InvariantCulture);

    RowEdit? Net(string source, ulong guid)
    {
        if (drafts.Draft is not { } draft)
        {
            return null;
        }

        net ??= draft.NetEdits()
            .Where(e => e.Edit.Table == TableName && ulong.TryParse(e.Edit.Key, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .ToDictionary(e => (e.Source, ulong.Parse(e.Edit.Key, CultureInfo.InvariantCulture)), e => e.Edit);
        return net.GetValueOrDefault((source, guid));
    }

    static bool Same(WorldPose a, WorldPose b) => a.Map == b.Map && Math.Abs(a.X - b.X) < Tolerance && Math.Abs(a.Y - b.Y) < Tolerance && Math.Abs(a.Z - b.Z) < Tolerance && Math.Abs(a.Heading - b.Heading) < Tolerance;

    /// <summary>The pose with the draft applied: the drafted pose, or <paramref name="original"/> when the draft holds none.</summary>
    public WorldPose Effective(string source, ulong guid, WorldPose original) => Net(source, guid) is { After: { } after } ? SpawnEdits.PoseOf(after) : original;

    /// <summary>True when the draft was made from a pose the database no longer holds.</summary>
    public bool Conflict(string source, ulong guid, WorldPose original) => Net(source, guid) is { Before: { } before } && !Same(SpawnEdits.PoseOf(before), original);

    public static GameObjectPlacement PlacementOf(Row row) => new(SpawnEdits.PoseOf(row), row.F("rotation0"), row.F("rotation1"), row.F("rotation2"), row.F("rotation3"));

    public static Row WithPlacement(Row row, GameObjectPlacement placement) => SpawnEdits.WithPose(row, placement.Pose)
        .With("rotation0", (float)placement.RotX).With("rotation1", (float)placement.RotY).With("rotation2", (float)placement.RotZ).With("rotation3", (float)placement.RotW);

    /// <summary>The GameObject placement with the draft applied, or <paramref name="original"/> when the draft holds none.</summary>
    public GameObjectPlacement Effective(string source, ulong guid, GameObjectPlacement original) => Net(source, guid) is { After: { } after } ? PlacementOf(after) : original;

    public bool Conflict(string source, ulong guid, GameObjectPlacement original) => Net(source, guid) is { Before: { } before }
        && (!Same(SpawnEdits.PoseOf(before), original.Pose) || !Same(original, PlacementOf(before)));

    static bool Same(GameObjectPlacement a, GameObjectPlacement b) => Math.Abs(a.RotX - b.RotX) < Tolerance && Math.Abs(a.RotY - b.RotY) < Tolerance && Math.Abs(a.RotZ - b.RotZ) < Tolerance && Math.Abs(a.RotW - b.RotW) < Tolerance;

    /// <summary>Record a move of a creature (or the pose of a GameObject) as one undoable command.</summary>
    public (bool Ok, string Message) Move(object? sender, string source, ulong guid, WorldPose after, string label = "Move")
    {
        after.Validate();
        return Change(sender, source, guid, label, row =>
        {
            if (SpawnEdits.PoseOf(row).Map != after.Map)
            {
                throw new InvalidDataException("A spawn cannot move to another map.");
            }

            return SpawnEdits.WithPose(row, after);
        });
    }

    /// <summary>Record a new GameObject placement (position and rotation).</summary>
    public (bool Ok, string Message) Move(object? sender, string source, ulong guid, GameObjectPlacement after, string label = "Move")
    {
        after.Validate();
        return Change(sender, source, guid, label, row =>
        {
            if (SpawnEdits.PoseOf(row).Map != after.Pose.Map)
            {
                throw new InvalidDataException("A spawn cannot move to another map.");
            }

            return WithPlacement(row, after);
        });
    }

    (bool Ok, string Message) Change(object? sender, string source, ulong guid, string label, Func<Row, Row> apply)
    {
        if (drafts.Draft == null)
        {
            return (false, drafts.Error ?? "Editing is disabled.");
        }

        var table = Table(source);
        var snapshot = Snapshot(source);
        var effective = drafts.Draft.Effective(source, snapshot);
        if (effective.Get(table, Key(guid)) is not { } before)
        {
            return (false, "The spawn is still loading; try again.");
        }

        try
        {
            var edit = InteractionEdits.Update(table, before, apply(before));
            return drafts.Commit(sender, source, snapshot, effective, label, [edit], _ => []);
        }
        catch (InvalidDataException error)
        {
            return (false, "Not saved: " + error.Message);
        }
    }

    public string Undo(object? sender) => drafts.History(sender, false);

    public string Redo(object? sender) => drafts.History(sender, true);
}
