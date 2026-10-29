using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>What the creature overlay draws for spawns that exist only in the draft.</summary>
public class StagedSpawnTests
{
    static readonly WorldPose Here = new(0, -9449.5, 64.25, 56.5, 1.5);
    static readonly CreatureLook Guard = new("Stormwind Guard", 3167, 1.25f);
    const string Source = "src";

    static ContentDrafts Open(ITableCatalog tables) => new(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "content.json")), _ => tables);

    [Fact]
    public void APlacedSpawnIsInsertedUntilItIsUndone()
    {
        using var drafts = Open(TcTables.Instance);
        var poses = new SpawnPoses(drafts, SpawnKind.Creature);
        var row = SpawnEdits.NewCreature(TcSchema.Instance, 9_000_001, 1234, Here, 0, 0);
        Assert.Empty(poses.Inserted(Source));

        var (ok, message) = poses.Commit(null, Source, "Place", SpawnEdits.Place(TcSchema.Instance, SpawnKind.Creature, row));
        Assert.True(ok, message);
        Assert.Equal(row, Assert.Single(poses.Inserted(Source)));
        Assert.Empty(poses.Inserted("other"));
        Assert.Equal(Here, poses.Effective(Source, 9_000_001, new WorldPose(0, 0, 0, 0, 0)));

        poses.Undo(null);
        Assert.Empty(poses.Inserted(Source));
        poses.Redo(null);
        Assert.Single(poses.Inserted(Source));
    }

    [Fact]
    public void AMovedPlacedSpawnStaysOneInsertWithTheNewPose()
    {
        using var drafts = Open(TcTables.Instance);
        var poses = new SpawnPoses(drafts, SpawnKind.Creature);
        var row = SpawnEdits.NewCreature(TcSchema.Instance, 9_000_001, 1234, Here, 0, 0);
        poses.Commit(null, Source, "Place", SpawnEdits.Place(TcSchema.Instance, SpawnKind.Creature, row));
        poses.Load(Source, [row]);

        poses.Move(null, Source, 9_000_001, Here with { X = -9400 });
        Assert.Equal(-9400, SpawnEdits.PoseOf(Assert.Single(poses.Inserted(Source))).X);
        Assert.Single(drafts.Draft!.NetEdits());
    }

    [Fact]
    public void ADeletedDatabaseSpawnIsReportedDeleted()
    {
        using var drafts = Open(TcTables.Instance);
        var poses = new SpawnPoses(drafts, SpawnKind.Creature);
        var row = SpawnEdits.NewCreature(TcSchema.Instance, 5, 1234, Here, 0, 0);
        poses.Load(Source, [row]);
        Assert.False(poses.Deleted(Source, 5));

        var effective = drafts.Draft!.Effective(Source, new RowSet());
        poses.Commit(null, Source, "Delete", [InteractionEdits.Delete(TcTables.SpawnCreature, row)]);
        Assert.True(poses.Deleted(Source, 5));
        Assert.Empty(poses.Inserted(Source));
        poses.Undo(null);
        Assert.False(poses.Deleted(Source, 5));
    }

    [Fact]
    public void ATrinityCoreRowReadsItsPhaseAndTheModelOverride()
    {
        var row = SpawnEdits.NewCreature(TcSchema.Instance, 7, 1234, Here, 0, 0, "0,1").With("PhaseId", 169).With("modelid", 555u);
        var spawn = SpawnPoses.CreatureOf(row, Guard, WorldDialect.TrinityCore);
        Assert.Equal((7ul, 1234u, "Stormwind Guard", 555u, 1.25f), (spawn.Guid, spawn.TemplateId, spawn.Name, spawn.DisplayId, spawn.DisplayScale));
        Assert.Equal(new SpawnPhasing("0,1", 169, 0, 0), spawn.Phasing);
        Assert.Equal(Here, spawn.Pose);

        // A NULL phase (inherit) reads as no phase, and no model override uses the template's.
        var plain = SpawnPoses.CreatureOf(SpawnEdits.NewCreature(TcSchema.Instance, 8, 1234, Here, 0, 0), Guard, WorldDialect.TrinityCore);
        Assert.Equal(3167u, plain.DisplayId);
        Assert.Equal(0u, plain.Phasing!.PhaseId);
    }

    [Fact]
    public void AnAzerothCoreRowReadsItsMasks()
    {
        var row = SpawnEdits.NewCreature(AcSchema.Instance, 7, 1234, Here, 0, 0, spawnMask: 3);
        var spawn = SpawnPoses.CreatureOf(row, Guard, WorldDialect.AzerothCore);
        Assert.Equal((3u, 1u, 3167u), (spawn.SpawnMask, spawn.PhaseMask, spawn.DisplayId));
        Assert.Null(spawn.Phasing);
    }
}
