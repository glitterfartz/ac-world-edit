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

    readonly Func<string, WorldDialect>? dialectOf;

    /// <summary>What the editor knows about the world; the rules that need a lookup run only when it is set.</summary>
    public SpawnFacts Facts { get; set; } = new();

    /// <param name="dialectOf">The dialect of a source, so each command is checked against that server's loader rules; without it nothing is checked.</param>
    public SpawnPoses(ContentDrafts drafts, SpawnKind kind, Func<string, WorldDialect>? dialectOf = null)
    {
        this.drafts = drafts;
        this.kind = kind;
        this.dialectOf = dialectOf;
        drafts.Changed += _ => net = null;
    }

    public bool Available => drafts.Draft != null;
    public bool CanUndo => drafts.Draft?.CanUndo == true;
    public bool CanRedo => drafts.Draft?.CanRedo == true;

    string TableName => kind == SpawnKind.Creature ? "creature" : "gameobject";

    TableSchema Table(string source) => drafts.Draft!.Tables(source).Find(TableName);

    RowSet Snapshot(string source) => snapshots.TryGetValue(source, out var rows) ? rows : snapshots[source] = new RowSet();

    /// <summary>The rules of the source's server, applied to every spawn row; none without a dialect.</summary>
    Func<RowSet, IEnumerable<InteractionIssue>> Rules(string source) => dialectOf == null
        ? _ => []
        : rows => SpawnRules.Check(dialectOf(source), kind, rows, Table(source), Facts);

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
        if (!Available)
        {
            return;
        }

        Load(source, rows.All(Table(source)));
        // The addon row is part of the spawn: a delete or a copy must carry it along.
        var addon = drafts.Draft!.Tables(source).Find(kind == SpawnKind.Creature ? "creature_addon" : "gameobject_addon");
        foreach (var row in rows.All(addon))
        {
            Snapshot(source).Set(addon, row);
        }
    }

    /// <summary>The GUID for a new spawn: past every database row and every spawn the draft has staged, undone ones included.</summary>
    public ulong NextGuid(string source, ulong databaseMax) => IdAllocator.Next(databaseMax, drafts.Draft?.MaxStaged(source, Table(source), "guid") ?? 0);

    /// <summary>Whether the database row of a spawn is loaded, so it can be edited.</summary>
    public bool Has(string source, ulong guid) => Available && Snapshot(source).Get(Table(source), Key(guid)) != null;

    static string Key(ulong guid) => guid.ToString(CultureInfo.InvariantCulture);

    Dictionary<(string Source, ulong Guid), RowEdit> NetMap()
    {
        if (drafts.Draft is not { } draft)
        {
            return [];
        }

        return net ??= draft.NetEdits()
            .Where(e => e.Edit.Table == TableName && ulong.TryParse(e.Edit.Key, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .ToDictionary(e => (e.Source, ulong.Parse(e.Edit.Key, CultureInfo.InvariantCulture)), e => e.Edit);
    }

    RowEdit? Net(string source, ulong guid) => NetMap().GetValueOrDefault((source, guid));

    /// <summary>The spawn rows the draft adds to <paramref name="source"/> (placed or duplicated, not yet exported), in key order.</summary>
    public IReadOnlyList<Row> Inserted(string source) => NetMap()
        .Where(n => n.Key.Source == source && n.Value is { Before: null, After: not null })
        .OrderBy(n => n.Key.Guid).Select(n => n.Value.After!).ToArray();

    /// <summary>True when the draft deletes this spawn's database row.</summary>
    public bool Deleted(string source, ulong guid) => Net(source, guid) is { After: null, Before: not null };

    /// <summary>
    /// A creature spawn row as the overlay draws it. A TrinityCore row carries a difficulty list and a
    /// phase (NULL phase columns read as 0); an AzerothCore row carries masks. The model is the row's
    /// own override or the template's.
    /// </summary>
    public static CreatureSpawn CreatureOf(Row row, CreatureLook look, WorldDialect dialect)
    {
        var pose = SpawnEdits.PoseOf(row);
        ulong guid = ulong.Parse(row["guid"], CultureInfo.InvariantCulture);
        if (dialect != WorldDialect.TrinityCore)
        {
            return new(guid, row.U("id"), look.Name, pose, row.U("spawnMask"), row.U("phaseMask"), look.DisplayId, look.Scale);
        }

        uint display = row.U("modelid") != 0 ? row.U("modelid") : look.DisplayId;
        return new(guid, row.U("id"), look.Name, pose, 0, 0, display, look.Scale, null,
            new SpawnPhasing(row["spawnDifficulties"], OrZero(row, "PhaseId"), OrZero(row, "PhaseGroup"), row.U("phaseUseFlags")));
    }

    /// <summary>
    /// A GameObject spawn row as the overlay draws it, mirroring the region read: the stored rotation
    /// is normalized, and a spawn without one faces by its heading.
    /// </summary>
    public static GameObjectSpawn GameObjectOf(Row row, GameObjectLook look, WorldDialect dialect)
    {
        var pose = SpawnEdits.PoseOf(row);
        var stored = new System.Numerics.Quaternion(row.F("rotation0"), row.F("rotation1"), row.F("rotation2"), row.F("rotation3"));
        var rotation = stored;
        if (!float.IsFinite(rotation.LengthSquared()))
        {
            rotation = System.Numerics.Quaternion.Identity;
            stored = System.Numerics.Quaternion.Zero;
        }
        else if (rotation.LengthSquared() < 0.00001f)
        {
            rotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, (float)pose.Heading);
        }
        else
        {
            rotation = System.Numerics.Quaternion.Normalize(rotation);
        }

        ulong guid = ulong.Parse(row["guid"], CultureInfo.InvariantCulture);
        var visibility = new SpawnVisibility([], 0, false, "", false);
        return dialect == WorldDialect.TrinityCore
            ? new(guid, row.U("id"), look.Name, pose, 0, 0, look.DisplayId, look.Scale, rotation, row.I("state"), look.Type, visibility,
                new SpawnPhasing(row["spawnDifficulties"], OrZero(row, "PhaseId"), OrZero(row, "PhaseGroup"), row.U("phaseUseFlags")), stored)
            : new(guid, row.U("id"), look.Name, pose, row.U("spawnMask"), row.U("phaseMask"), look.DisplayId, look.Scale, rotation, row.I("state"), look.Type, visibility, null, stored);
    }

    static uint OrZero(Row row, string column) => row.IsNull(column) ? 0 : (uint)Math.Max(0, row.I(column));

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
            return drafts.Commit(sender, source, snapshot, effective, label, [edit], Rules(source));
        }
        catch (InvalidDataException error)
        {
            return (false, "Not saved: " + error.Message);
        }
    }

    /// <summary>Record edits built by <see cref="SpawnEdits"/> (place, duplicate, delete) as one undoable command.</summary>
    public (bool Ok, string Message) Commit(object? sender, string source, string label, IReadOnlyList<RowEdit> edits)
    {
        if (drafts.Draft == null)
        {
            return (false, drafts.Error ?? "Editing is disabled.");
        }

        var snapshot = Snapshot(source);
        return drafts.Commit(sender, source, snapshot, drafts.Draft.Effective(source, snapshot), label, edits, Rules(source));
    }

    /// <summary>Delete a spawn and its addon row as one undoable command. The spawn's row must be loaded (or staged in the draft).</summary>
    public (bool Ok, string Message) Delete(object? sender, IWorldSchema schema, string source, ulong guid, string label = "Delete")
    {
        if (drafts.Draft == null)
        {
            return (false, drafts.Error ?? "Editing is disabled.");
        }

        var snapshot = Snapshot(source);
        var effective = drafts.Draft.Effective(source, snapshot);
        return effective.Get(Table(source), Key(guid)) is not { } row
            ? (false, "The spawn is still loading; try again.")
            : Commit(sender, source, label, SpawnEdits.Delete(schema, kind, effective, row));
    }

    /// <summary>Copy a spawn, with its addon, to <paramref name="pose"/> under <paramref name="newGuid"/> as one undoable command.</summary>
    public (bool Ok, string Message) Duplicate(object? sender, IWorldSchema schema, string source, ulong guid, ulong newGuid, WorldPose pose, string label = "Duplicate")
    {
        if (drafts.Draft == null)
        {
            return (false, drafts.Error ?? "Editing is disabled.");
        }

        var snapshot = Snapshot(source);
        var effective = drafts.Draft.Effective(source, snapshot);
        if (effective.Get(Table(source), Key(guid)) is not { } row)
        {
            return (false, "The spawn is still loading; try again.");
        }

        try
        {
            return Commit(sender, source, label, SpawnEdits.Duplicate(schema, kind, effective, row, newGuid, pose));
        }
        catch (InvalidDataException error)
        {
            return (false, "Not saved: " + error.Message);
        }
    }

    public string Undo(object? sender) => drafts.History(sender, false);

    public string Redo(object? sender) => drafts.History(sender, true);
}
