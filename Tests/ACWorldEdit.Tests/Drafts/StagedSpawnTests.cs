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

    static readonly GameObjectLook Chest = new("Large Solid Chest", 259, 1.5f, 3);

    [Fact]
    public void AGameObjectRowKeepsItsFacingStateAndTemplateLook()
    {
        var row = SpawnEdits.NewGameObject(TcSchema.Instance, 11, 2843, Here with { Heading = 1.0 }, "0,1");
        var spawn = SpawnPoses.GameObjectOf(row, Chest, WorldDialect.TrinityCore);
        Assert.Equal((11ul, 2843u, "Large Solid Chest", 259u, 1.5f, 3), (spawn.Guid, spawn.TemplateId, spawn.Name, spawn.DisplayId, spawn.Scale, spawn.Type));
        Assert.Equal(SpawnEdits.StateReady, (uint)spawn.State);
        Assert.Equal(Here with { Heading = 1.0 }, spawn.Pose);
        // The stored quaternion turns the object about the vertical axis by the heading.
        Assert.Equal(MathF.Sin(0.5f), spawn.Rotation.Z, 4);
        Assert.Equal(new SpawnPhasing("0,1", 0, 0, 0), spawn.Phasing);

        var ac = SpawnPoses.GameObjectOf(SpawnEdits.NewGameObject(AcSchema.Instance, 12, 2843, Here, spawnMask: 3), Chest, WorldDialect.AzerothCore);
        Assert.Equal((3u, 1u), (ac.SpawnMask, ac.PhaseMask));
        Assert.Null(ac.Phasing);
    }

    [Fact]
    public void AGameObjectWithoutAStoredRotationFacesByItsHeading()
    {
        var row = SpawnEdits.NewGameObject(TcSchema.Instance, 13, 2843, Here with { Heading = 2.0 }).With("rotation2", 0f).With("rotation3", 0f);
        var spawn = SpawnPoses.GameObjectOf(row, Chest, WorldDialect.TrinityCore);
        Assert.Equal(MathF.Sin(1.0f), spawn.Rotation.Z, 4);
        Assert.Equal(System.Numerics.Quaternion.Zero, spawn.StoredRotation);
    }

    static (ContentDrafts Drafts, SpawnPoses Poses, Row Spawn, Row Addon) LoadedCreature()
    {
        var drafts = Open(TcTables.Instance);
        var poses = new SpawnPoses(drafts, SpawnKind.Creature);
        var spawn = SpawnEdits.NewCreature(TcSchema.Instance, 5, 1234, Here, 1, 0);
        var addon = Row.Of(TcTables.SpawnCreatureAddon.Columns.Select(c => (c.Name, c.Name == "guid" ? 5ul : c.Kind == ColumnKind.Text ? "" : c.Kind == ColumnKind.Float ? (object)0f : 0)).ToArray());
        addon = addon.With("auras", null);
        var rows = new RowSet();
        rows.Set(TcTables.SpawnCreature, spawn);
        rows.Set(TcTables.SpawnCreatureAddon, addon);
        poses.Load(Source, rows);
        return (drafts, poses, spawn, addon);
    }

    [Fact]
    public void DeletingASpawnRemovesItsAddonFirstAndUndoRestoresBoth()
    {
        var (drafts, poses, spawn, addon) = LoadedCreature();
        var (ok, message) = poses.Delete(null, TcSchema.Instance, Source, 5);
        Assert.True(ok, message);
        Assert.True(poses.Deleted(Source, 5));
        var edits = drafts.Draft!.NetEdits().Select(e => e.Edit).ToArray();
        Assert.Equal(["creature_addon", "creature"], edits.Select(e => e.Table));
        Assert.Equal(spawn, edits[1].Before);
        string apply = SqlExport.Build(Source, [], edits, null, null, TcSchema.Instance).Apply;
        Assert.True(apply.IndexOf("DELETE FROM `creature_addon`", StringComparison.Ordinal) < apply.IndexOf("DELETE FROM `creature` WHERE", StringComparison.Ordinal));

        poses.Undo(null);
        Assert.False(poses.Deleted(Source, 5));
        Assert.Empty(drafts.Draft.NetEdits());
        _ = addon;
    }

    [Fact]
    public void DeletingAPlacedSpawnLeavesNothingToExport()
    {
        using var drafts = Open(TcTables.Instance);
        var poses = new SpawnPoses(drafts, SpawnKind.Creature);
        var row = SpawnEdits.NewCreature(TcSchema.Instance, 9_000_001, 1234, Here, 0, 0);
        poses.Commit(null, Source, "Place", SpawnEdits.Place(TcSchema.Instance, SpawnKind.Creature, row));

        var (ok, message) = poses.Delete(null, TcSchema.Instance, Source, 9_000_001);
        Assert.True(ok, message);
        Assert.Empty(drafts.Draft!.NetEdits());
        Assert.Empty(poses.Inserted(Source));
    }

    [Fact]
    public void ADuplicateKeepsEquipmentAndAddonUnderANewGuid()
    {
        var (drafts, poses, spawn, _) = LoadedCreature();
        var target = Here with { X = Here.X + 2 };
        var (ok, message) = poses.Duplicate(null, TcSchema.Instance, Source, 5, 6, target);
        Assert.True(ok, message);

        var copy = Assert.Single(poses.Inserted(Source));
        Assert.Equal((6ul, 1234u, 1), (copy.UL("guid"), copy.U("id"), copy.I("equipment_id")));
        Assert.Equal(target, SpawnEdits.PoseOf(copy));
        Assert.Equal(["creature", "creature_addon"], drafts.Draft!.NetEdits().Select(e => e.Edit.Table).OrderByDescending(t => t == "creature"));
        Assert.Equal("6", drafts.Draft.NetEdits().Single(e => e.Edit.Table == "creature_addon").Edit.Key);

        poses.Undo(null);
        Assert.Empty(poses.Inserted(Source));
        _ = spawn;
    }

    [Fact]
    public void ADuplicateCannotLeaveTheMapAndAnUnloadedSpawnIsRefused()
    {
        var (_, poses, _, _) = LoadedCreature();
        var (ok, message) = poses.Duplicate(null, TcSchema.Instance, Source, 5, 6, Here with { Map = 1 });
        Assert.False(ok);
        Assert.Contains("map", message);

        Assert.Contains("loading", poses.Delete(null, TcSchema.Instance, Source, 777).Message);
        Assert.Contains("loading", poses.Duplicate(null, TcSchema.Instance, Source, 777, 778, Here).Message);
    }

    [Theory]
    [InlineData(0, 0, 10, 0, 0.0)]
    [InlineData(0, 0, 0, 10, Math.PI / 2)]
    [InlineData(0, 0, -10, 0, Math.PI)]
    [InlineData(0, 0, 0, -10, 3 * Math.PI / 2)]
    [InlineData(5, 5, 5, 5, 0.0)]
    public void ASpawnFacesTheCameraThatPlacedIt(double x, double y, double toX, double toY, double heading) =>
        Assert.Equal(heading, SpawnPlacement.FacingHeading(x, y, toX, toY), 6);

    static (ContentDrafts Drafts, SpawnPoses Poses) ThreeLoaded()
    {
        var drafts = Open(TcTables.Instance);
        var poses = new SpawnPoses(drafts, SpawnKind.Creature, _ => WorldDialect.TrinityCore);
        poses.Load(Source, new ulong[] { 5, 6, 7 }.Select(g => SpawnEdits.NewCreature(TcSchema.Instance, g, 1234, Here with { X = Here.X + g }, 0, 0)));
        return (drafts, poses);
    }

    [Fact]
    public void AGroupMoveIsOneUndoableCommandForEverySpawn()
    {
        var (drafts, poses) = ThreeLoaded();
        var (ok, message) = poses.EditMany(null, Source, [5, 6, 7], "Move 3 spawns", row => SpawnEdits.WithPose(row, SpawnEdits.PoseOf(row) with { Y = 99 }));
        Assert.True(ok, message);
        Assert.Equal(3, drafts.Draft!.NetEdits().Count);
        Assert.All(new ulong[] { 5, 6, 7 }, g => Assert.Equal(99, SpawnEdits.PoseOf(poses.RowOf(Source, g)!).Y));

        poses.Undo(null);
        Assert.Empty(drafts.Draft.NetEdits());
        Assert.Equal(Here.Y, SpawnEdits.PoseOf(poses.RowOf(Source, 6)!).Y);
        Assert.True(poses.EditMany(null, Source, [5, 6], "Nothing", row => row).Ok);
        Assert.Empty(drafts.Draft.NetEdits());
    }

    [Fact]
    public void AGroupDeleteAndCopyEachAreOneCommandAndRefuseWhenARowIsMissing()
    {
        var (drafts, poses) = ThreeLoaded();
        Assert.Contains("loading", poses.DeleteMany(null, TcSchema.Instance, Source, [5, 777]).Message);
        Assert.Empty(drafts.Draft!.NetEdits());

        Assert.True(poses.DeleteMany(null, TcSchema.Instance, Source, [5, 6, 7]).Ok);
        Assert.Equal(3, drafts.Draft.NetEdits().Count);
        Assert.All(new ulong[] { 5, 6, 7 }, g => Assert.True(poses.Deleted(Source, g)));
        poses.Undo(null);
        Assert.Empty(drafts.Draft.NetEdits());

        var guids = poses.NextGuids(Source, 100, 2);
        Assert.Equal([101ul, 102], guids);
        var (ok, message) = poses.DuplicateMany(null, TcSchema.Instance, Source, [(5, guids[0], Here with { X = 1 }), (6, guids[1], Here with { X = 2 })]);
        Assert.True(ok, message);
        Assert.Equal([101ul, 102], poses.Inserted(Source).Select(r => r.UL("guid")));
        poses.Undo(null);
        Assert.Empty(poses.Inserted(Source));
        // The reserved block counts staged GUIDs even once undone, so a redo cannot collide with a new copy.
        Assert.Equal([103ul], poses.NextGuids(Source, 100, 1));
    }
}
