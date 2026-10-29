using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class SpawnEditsTests
{
    static readonly DateTime Stamp = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    static readonly IWorldSchema Tc = TcSchema.Instance;
    static readonly IWorldSchema Ac = AcSchema.Instance;
    static readonly WorldPose Here = new(0, -9449.5, 64.25, 56.5, 1.5);

    static string Path() => System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "content.json"));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NewRowsAreCompleteForBothDialects(bool trinity)
    {
        var schema = trinity ? Tc : Ac;
        var creature = SpawnEdits.NewCreature(schema, 5, 1234, Here, equipmentId: 1, templateMovementType: 0);
        var gameObject = SpawnEdits.NewGameObject(schema, 6, 4321, Here);
        creature.Validate(SpawnEdits.SpawnTable(schema, SpawnKind.Creature));
        gameObject.Validate(SpawnEdits.SpawnTable(schema, SpawnKind.GameObject));
        Assert.Equal(Here, SpawnEdits.PoseOf(creature));
        Assert.Equal(300u, creature.U("spawntimesecs"));
        Assert.Equal(255u, gameObject.U("animprogress"));
        Assert.Equal(1u, gameObject.U("state"));
    }

    [Fact]
    public void ATrinityCoreSpawnInheritsItsTemplateUntilSomethingIsOverridden()
    {
        var row = SpawnEdits.NewCreature(Tc, 5, 1234, Here, 1, 0);
        Assert.True(row.IsNull("npcflag"));
        Assert.True(row.IsNull("unit_flags"));
        Assert.True(row.IsNull("StringId"));
        Assert.Equal(100u, row.U("curHealthPct"));
        Assert.Equal("0", row["spawnDifficulties"]);
    }

    [Fact]
    public void AnAzerothCoreSpawnWritesZeroFlagsWhichTheServerReadsAsTheTemplates()
    {
        var row = SpawnEdits.NewCreature(Ac, 5, 1234, Here, 1, 0);
        Assert.Equal(0u, row.U("npcflag"));
        Assert.Equal(0u, row.U("unit_flags"));
        Assert.Equal(1u, row.U("spawnMask"));
        Assert.Equal(1u, row.U("phaseMask"));
    }

    [Fact]
    public void ARandomWalkerWithoutARadiusIsPlacedIdleAsTheServerNormalizesIt()
    {
        Assert.Equal(0u, SpawnEdits.NewCreature(Tc, 5, 1, Here, 0, templateMovementType: 1).U("MovementType"));
        Assert.Equal(2u, SpawnEdits.NewCreature(Tc, 5, 1, Here, 0, templateMovementType: 2).U("MovementType"));
    }

    [Fact]
    public void AGameObjectFacesByTurningAboutTheVerticalAxis()
    {
        var row = SpawnEdits.NewGameObject(Tc, 6, 1, Here with { Heading = Math.PI });
        Assert.Equal(1f, row.F("rotation2"), 4);
        Assert.Equal(0f, row.F("rotation3"), 4);
        Assert.Equal(0f, row.F("rotation0"));
    }

    [Fact]
    public void AMoveIsAFloatTolerantUpdateAndStaysOnItsMap()
    {
        var before = SpawnEdits.NewCreature(Tc, 5, 1, Here, 0, 0);
        var edit = SpawnEdits.Move(Tc, SpawnKind.Creature, before, Here with { X = -9450 });
        string apply = SqlExport.Build("src", [], [], [edit], Stamp, null, Tc).Apply;
        Assert.Contains("UPDATE `creature` SET `position_x` = -9450 WHERE `guid` = 5 AND ABS(`position_x` - (-9449.5)) < 0.0001;", apply);
        Assert.Throws<InvalidDataException>(() => SpawnEdits.Move(Tc, SpawnKind.Creature, before, Here with { Map = 1 }));
    }

    [Fact]
    public void ADuplicateKeepsNoEquipmentOverridesAndTheAddon()
    {
        var spawn = SpawnEdits.NewCreature(Tc, 5, 1, Here, equipmentId: 0, 0).With("npcflag", 3UL);
        var addonTable = SpawnEdits.AddonTable(Tc, SpawnKind.Creature);
        var addon = Row.Of(addonTable.Columns.Select(c => (c.Name, c.Kind == ColumnKind.Text ? "" : (object)0)).ToArray()).With("guid", 5UL).With("emote", 69u).With("auras", null);
        var effective = new RowSet();
        effective.Set(addonTable, addon);

        var edits = SpawnEdits.Duplicate(Tc, SpawnKind.Creature, effective, spawn, 99, Here with { X = -9440 });
        Assert.Equal(2, edits.Length);
        var copy = edits[0].After!;
        Assert.Equal(99UL, copy.UL("guid"));
        Assert.Equal(0, copy.I("equipment_id"));
        Assert.Equal(3UL, copy.UL("npcflag"));
        Assert.Equal(-9440, SpawnEdits.PoseOf(copy).X);
        Assert.Equal(99UL, edits[1].After!.UL("guid"));
        Assert.Equal(69u, edits[1].After!.U("emote"));
        Assert.True(edits[1].After!.IsNull("auras"));
    }

    [Fact]
    public void ADeleteTakesTheAddonRowAlong()
    {
        var spawn = SpawnEdits.NewGameObject(Tc, 6, 1, Here);
        var addonTable = SpawnEdits.AddonTable(Tc, SpawnKind.GameObject);
        var addon = Row.Of(addonTable.Columns.Select(c => (c.Name, (object)0)).ToArray()).With("guid", 6UL);
        var effective = new RowSet();
        effective.Set(addonTable, addon);

        var edits = SpawnEdits.Delete(Tc, SpawnKind.GameObject, effective, spawn);
        Assert.Equal(["gameobject_addon", "gameobject"], edits.Select(e => e.Table));
        Assert.All(edits, e => Assert.Null(e.After));
        Assert.Single(SpawnEdits.Delete(Tc, SpawnKind.GameObject, new RowSet(), spawn));
    }

    [Fact]
    public void MovingThenDeletingASpawnExportsADeleteOfTheRowThatIsInTheDatabase()
    {
        // The case two separate pose and row drafts got wrong: the delete guard must hold the
        // original pose, because the move is never applied to the database.
        var original = SpawnEdits.NewCreature(Tc, 5, 1, Here, 0, 0);
        var table = SpawnEdits.SpawnTable(Tc, SpawnKind.Creature);
        var snapshot = new RowSet();
        snapshot.Set(table, original);

        using var draft = new InteractionDraft(Path(), TcTables.Instance);
        var moved = SpawnEdits.Move(Tc, SpawnKind.Creature, original, Here with { X = -9400 });
        draft.Execute("src", snapshot, "Move", [moved]);
        var effective = draft.Effective("src", snapshot);
        draft.Execute("src", snapshot, "Delete", SpawnEdits.Delete(Tc, SpawnKind.Creature, effective, effective.Get(table, "5")!));

        var net = Assert.Single(draft.NetEdits());
        Assert.Null(net.Edit.After);
        Assert.Equal(original, net.Edit.Before);
        string apply = SqlExport.Build("src", [], [], [net.Edit], Stamp, null, Tc).Apply;
        Assert.Contains("ABS(`position_x` - (-9449.5)) < 0.0001", apply);
        Assert.DoesNotContain("-9400", apply);

        // Undo puts the moved row back, then the original.
        draft.Undo();
        Assert.Equal(-9400, SpawnEdits.PoseOf(draft.Effective("src", snapshot).Get(table, "5")!).X);
        draft.Undo();
        Assert.Equal(original, draft.Effective("src", snapshot).Get(table, "5"));
    }

    [Fact]
    public void ReferencesNameTheDialectsOwnTables()
    {
        var tc = SpawnEdits.References(WorldDialect.TrinityCore, SpawnKind.Creature).Select(r => r.Table).ToArray();
        var ac = SpawnEdits.References(WorldDialect.AzerothCore, SpawnKind.Creature).Select(r => r.Table).ToArray();
        Assert.Contains("spawn_group", tc);
        Assert.Contains("pool_members", tc);
        Assert.DoesNotContain("spawn_group", ac);
        Assert.Contains("pool_creature", ac);
        Assert.Contains("pool_gameobject", SpawnEdits.References(WorldDialect.AzerothCore, SpawnKind.GameObject).Select(r => r.Table));
        Assert.DoesNotContain("creature_formations", SpawnEdits.References(WorldDialect.TrinityCore, SpawnKind.GameObject).Select(r => r.Table));
    }
}
