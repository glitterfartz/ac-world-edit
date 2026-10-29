using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>The rules mirror the server loaders; each test breaks exactly one thing in an otherwise valid row.</summary>
public class SpawnRulesTests
{
    static readonly WorldPose Here = new(0, -9449.5, 64.25, 56.5, 1.5);

    static Row Tc() => SpawnEdits.NewCreature(TcSchema.Instance, 5, 1234, Here, 0, 0);

    static Row Ac() => SpawnEdits.NewCreature(AcSchema.Instance, 5, 1234, Here, 0, 0);

    static string[] Errors(WorldDialect dialect, Row row, SpawnFacts? facts = null, SpawnKind kind = SpawnKind.Creature) =>
        SpawnRules.CheckRow(dialect, kind, row, facts ?? new()).Where(i => i.Error).Select(i => i.Message).ToArray();

    [Fact]
    public void ARowFromTheEditorsOwnDefaultsPassesOnBothServers()
    {
        Assert.Empty(Errors(WorldDialect.TrinityCore, Tc()));
        Assert.Empty(Errors(WorldDialect.AzerothCore, Ac()));
        Assert.Empty(Errors(WorldDialect.TrinityCore, SpawnEdits.NewGameObject(TcSchema.Instance, 6, 4321, Here), kind: SpawnKind.GameObject));
        Assert.Empty(Errors(WorldDialect.AzerothCore, SpawnEdits.NewGameObject(AcSchema.Instance, 6, 4321, Here), kind: SpawnKind.GameObject));
    }

    [Theory]
    [InlineData(3u, 0f, "cannot be set in the database")]
    [InlineData(1u, 0f, "random movement needs a wander distance")]
    [InlineData(0u, 5f, "stands still cannot have a wander distance")]
    [InlineData(1u, -2f, "cannot be negative")]
    public void MovementAndWanderFollowTheLoader(uint movement, float wander, string message)
    {
        foreach (var (dialect, row) in new[] { (WorldDialect.TrinityCore, Tc()), (WorldDialect.AzerothCore, Ac()) })
        {
            Assert.Contains(message, Assert.Single(Errors(dialect, row.With("MovementType", movement).With("wander_distance", wander))));
        }

        // A random walker with a radius, and a waypoint walker, are fine.
        Assert.Empty(Errors(WorldDialect.TrinityCore, Tc().With("MovementType", 1u).With("wander_distance", 5f)));
        Assert.Empty(Errors(WorldDialect.AzerothCore, Ac().With("MovementType", 2u)));
    }

    [Fact]
    public void OnlyTrinityCoreRefusesATinyWanderDistance()
    {
        Assert.Contains("below 0.1", Assert.Single(Errors(WorldDialect.TrinityCore, Tc().With("MovementType", 1u).With("wander_distance", 0.05f))));
        Assert.Empty(Errors(WorldDialect.AzerothCore, Ac().With("MovementType", 1u).With("wander_distance", 0.05f)));
    }

    [Fact]
    public void EquipmentTemplateAndMapMustExistWhenTheEditorCanTell()
    {
        var facts = new SpawnFacts(TemplateExists: e => e != 1234, MapExists: m => m != 0, EquipmentExists: (_, id) => id == 1);
        var errors = Errors(WorldDialect.TrinityCore, Tc().With("equipment_id", 2), facts);
        Assert.Equal(3, errors.Length);
        Assert.Contains(errors, e => e.Contains("template 1234 does not exist"));
        Assert.Contains(errors, e => e.Contains("map 0 does not exist"));
        Assert.Contains(errors, e => e.Contains("equipment set 2"));
        // No equipment, and a set that exists, are fine; without facts nothing is looked up.
        Assert.DoesNotContain(Errors(WorldDialect.TrinityCore, Tc(), facts), e => e.Contains("equipment"));
        Assert.DoesNotContain(Errors(WorldDialect.TrinityCore, Tc().With("equipment_id", 1), facts), e => e.Contains("equipment"));
        Assert.Empty(Errors(WorldDialect.TrinityCore, Tc().With("equipment_id", 9)));
    }

    [Fact]
    public void TrinityCoreDifficultiesAndPhasesFollowTheLoader()
    {
        Assert.Contains("at least one difficulty", Assert.Single(Errors(WorldDialect.TrinityCore, Tc().With("spawnDifficulties", ""))));
        Assert.Contains("\"x\" is not a number", Assert.Single(Errors(WorldDialect.TrinityCore, Tc().With("spawnDifficulties", "0,x"))));
        var facts = new SpawnFacts(DifficultyValid: (_, d) => d == 0, PhaseExists: p => p == 169);
        Assert.Contains("difficulty 14 is not valid", Assert.Single(Errors(WorldDialect.TrinityCore, Tc().With("spawnDifficulties", "0,14"), facts)));

        Assert.Contains("not both", Assert.Single(Errors(WorldDialect.TrinityCore, Tc().With("PhaseId", 169).With("PhaseGroup", 5), facts)));
        Assert.Contains("phase 170 does not exist", Assert.Single(Errors(WorldDialect.TrinityCore, Tc().With("PhaseId", 170), facts)));
        Assert.Empty(Errors(WorldDialect.TrinityCore, Tc().With("PhaseId", 169), facts));
        Assert.Contains("unknown bits", Assert.Single(Errors(WorldDialect.TrinityCore, Tc().With("phaseUseFlags", 4u))));
        Assert.Contains("always visible and inverse", Assert.Single(Errors(WorldDialect.TrinityCore, Tc().With("phaseUseFlags", 3u))));
        // NULL phase columns are the editor's default and mean no phase.
        Assert.Empty(Errors(WorldDialect.TrinityCore, Tc().With("PhaseId", null).With("PhaseGroup", null)));
    }

    [Fact]
    public void AzerothCoreChecksItsSpawnAndPhaseMasks()
    {
        Assert.Contains("phase mask 0", Assert.Single(Errors(WorldDialect.AzerothCore, Ac().With("phaseMask", 0u))));
        var facts = new SpawnFacts(SpawnMaskValid: (_, mask) => mask == 1);
        Assert.Contains("spawn mask 3", Assert.Single(Errors(WorldDialect.AzerothCore, Ac().With("spawnMask", 3u), facts)));
        Assert.Empty(Errors(WorldDialect.AzerothCore, Ac(), facts));
    }

    [Fact]
    public void ACommandIsRefusedOnlyForAnErrorItIntroduces()
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "content.json"));
        using var drafts = new ContentDrafts(path, _ => TcTables.Instance);
        var poses = new SpawnPoses(drafts, SpawnKind.Creature, _ => WorldDialect.TrinityCore);
        // A row the database already holds in a state the loader rejects: moving it must still work.
        var broken = Tc().With("MovementType", 1u).With("wander_distance", 0f);
        poses.Load("src", [broken]);
        var (moved, message) = poses.Move(null, "src", 5, Here with { X = -9400 });
        Assert.True(moved, message);

        // Placing a new one that breaks a rule is refused, with the rule's message.
        var bad = SpawnEdits.NewCreature(TcSchema.Instance, 9, 1234, Here, 0, 0).With("spawnDifficulties", "");
        var (placed, why) = poses.Commit(null, "src", "Place", SpawnEdits.Place(TcSchema.Instance, SpawnKind.Creature, bad));
        Assert.False(placed);
        Assert.Contains("at least one difficulty", why);
        Assert.Single(drafts.Draft!.NetEdits());
    }
}
