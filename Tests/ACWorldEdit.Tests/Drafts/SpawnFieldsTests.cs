using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>The inspector's field lists must describe real columns, and typed values must land in them.</summary>
public class SpawnFieldsTests
{
    static readonly WorldPose Here = new(0, -9449.5, 64.25, 56.5, 1.5);

    public static IEnumerable<object[]> Cases() =>
        from dialect in new[] { WorldDialect.TrinityCore, WorldDialect.AzerothCore }
        from kind in new[] { SpawnKind.Creature, SpawnKind.GameObject }
        select new object[] { dialect, kind };

    static Row NewRow(WorldDialect dialect, SpawnKind kind)
    {
        IWorldSchema schema = WorldSchemas.For(dialect);
        return kind == SpawnKind.Creature ? SpawnEdits.NewCreature(schema, 5, 1234, Here, 0, 0) : SpawnEdits.NewGameObject(schema, 5, 4321, Here);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryFieldIsAColumnOfTheSpawnTableAndShowsAndParsesItsOwnDefault(WorldDialect dialect, SpawnKind kind)
    {
        var schema = WorldSchemas.For(dialect);
        var spawnTable = SpawnEdits.SpawnTable(schema, kind);
        var addonTable = SpawnEdits.AddonTable(schema, kind);
        var spawnRow = NewRow(dialect, kind);
        var addonRow = SpawnEdits.NewAddon(schema, kind, 5);
        addonRow.Validate(addonTable);
        var fields = SpawnFields.All(dialect, kind);
        Assert.Equal(fields.Count, fields.Select(f => (f.Addon, f.Column)).Distinct().Count());
        foreach (var field in fields)
        {
            var table = field.Addon ? addonTable : spawnTable;
            var row = field.Addon ? addonRow : spawnRow;
            Assert.True(table.Has(field.Column), $"{dialect} {kind}: {field.Column} is not a column of {table.Name}");
            // What the row shows must be accepted back, unchanged, by the same field.
            var (ok, value, error) = field.Parse(field.Show(row));
            Assert.True(ok, $"{field.Column}: {error}");
            var (changed, problem) = SpawnFields.Apply(field, row, field.Show(row));
            Assert.True(changed != null, $"{field.Column}: {problem}");
            Assert.Equal(row[field.Column], changed![field.Column]);
        }
    }

    [Fact]
    public void TrinityCoreInheritsByNullAndAzerothCoreByZero()
    {
        var tc = SpawnFields.For(WorldDialect.TrinityCore, SpawnKind.Creature).Single(f => f.Column == "npcflag");
        var row = NewRow(WorldDialect.TrinityCore, SpawnKind.Creature);
        Assert.False(tc.IsOverridden(row));
        Assert.Equal("", tc.Show(row));
        var overridden = row.With("npcflag", 3L);
        Assert.True(tc.IsOverridden(overridden));
        Assert.Equal("3", tc.Show(overridden));
        // Clearing the field goes back to the template; an explicit 0 is a real override on TrinityCore.
        Assert.True(overridden.With("npcflag", tc.Parse("").Value).IsNull("npcflag"));
        Assert.Equal("0", tc.Show(row.With("npcflag", 0L)));

        var ac = SpawnFields.For(WorldDialect.AzerothCore, SpawnKind.Creature).Single(f => f.Column == "npcflag");
        var acRow = NewRow(WorldDialect.AzerothCore, SpawnKind.Creature);
        Assert.False(ac.IsOverridden(acRow));
        Assert.True(ac.IsOverridden(acRow.With("npcflag", 3u)));
        Assert.Equal("0", acRow.With("npcflag", ac.Parse("").Value)["npcflag"]);
        Assert.Contains("cannot be overridden to 0", ac.Tip);
    }

    [Fact]
    public void ParsingRejectsWhatTheColumnCannotHold()
    {
        var fields = SpawnFields.For(WorldDialect.TrinityCore, SpawnKind.Creature).ToDictionary(f => f.Column);
        Assert.False(fields["spawntimesecs"].Parse("-5").Ok);
        Assert.False(fields["spawntimesecs"].Parse("soon").Ok);
        Assert.Contains("use . for decimals", fields["wander_distance"].Parse("5,5").Error);
        Assert.False(fields["curHealthPct"].Parse("101").Ok);
        Assert.True(fields["equipment_id"].Parse("-1").Ok);
        Assert.False(fields["equipment_id"].Parse("-2").Ok);
        Assert.Equal(1L, fields["MovementType"].Parse("Wanders").Value is uint u ? u : 0);
        Assert.Contains("Stands still", fields["MovementType"].Parse("fly").Error);
        Assert.Equal(0x10L, fields["unit_flags"].Parse("0x10").Value);
        Assert.False(fields["phaseUseFlags"].Parse("4").Ok);
        Assert.Equal("a;b", fields["ScriptName"].Parse("  a;b ").Value);

        var objects = SpawnFields.For(WorldDialect.AzerothCore, SpawnKind.GameObject).ToDictionary(f => f.Column);
        Assert.True(objects["spawntimesecs"].Parse("-1").Ok);
        Assert.False(objects["animprogress"].Parse("256").Ok);
    }

    [Fact]
    public void MovementAndWanderRadiusMoveTogetherLikeTheServerCommands()
    {
        var fields = SpawnFields.For(WorldDialect.TrinityCore, SpawnKind.Creature).ToDictionary(f => f.Column);
        var still = NewRow(WorldDialect.TrinityCore, SpawnKind.Creature);

        var radius = SpawnFields.Apply(fields["wander_distance"], still, "8").Row!;
        Assert.Equal((1u, 8f), (radius.U("MovementType"), radius.F("wander_distance")));

        var back = SpawnFields.Apply(fields["wander_distance"], radius, "0").Row!;
        Assert.Equal((0u, 0f), (back.U("MovementType"), back.F("wander_distance")));

        var wanders = SpawnFields.Apply(fields["MovementType"], still, "Wanders").Row!;
        Assert.Equal((1u, 5f), (wanders.U("MovementType"), wanders.F("wander_distance")));
        var stops = SpawnFields.Apply(fields["MovementType"], wanders, "Stands still").Row!;
        Assert.Equal((0u, 0f), (stops.U("MovementType"), stops.F("wander_distance")));

        // A creature on a path keeps it when its radius is cleared.
        var path = still.With("MovementType", 2u);
        Assert.Equal(2u, SpawnFields.Apply(fields["wander_distance"], path, "0").Row!.U("MovementType"));
        Assert.Equal("", SpawnFields.Apply(fields["wander_distance"], still, "x").Row?.ToString() ?? "");
        Assert.NotEqual("", SpawnFields.Apply(fields["wander_distance"], still, "x").Error);
    }

    [Fact]
    public void AnEditIsOneUndoableCommandAndAnUnchangedValueRecordsNothing()
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "content.json"));
        using var drafts = new ContentDrafts(path, _ => TcTables.Instance);
        var poses = new SpawnPoses(drafts, SpawnKind.Creature, _ => WorldDialect.TrinityCore);
        var row = NewRow(WorldDialect.TrinityCore, SpawnKind.Creature);
        poses.Load("src", [row]);
        var field = SpawnFields.For(WorldDialect.TrinityCore, SpawnKind.Creature).Single(f => f.Column == "spawntimesecs");

        Assert.True(poses.Edit(null, "src", 5, "Set respawn", r => SpawnFields.Apply(field, r, "300").Row!).Ok);
        Assert.Empty(drafts.Draft!.NetEdits());

        var (ok, message) = poses.Edit(null, "src", 5, "Set respawn", r => SpawnFields.Apply(field, r, "900").Row!);
        Assert.True(ok, message);
        Assert.Equal(900u, poses.RowOf("src", 5)!.U("spawntimesecs"));
        string apply = SqlExport.Build("src", [], drafts.Draft.NetEdits().Select(e => e.Edit), null, null, TcSchema.Instance).Apply;
        Assert.Contains("SET `spawntimesecs` = 900", apply);

        poses.Undo(null);
        Assert.Equal(300u, poses.RowOf("src", 5)!.U("spawntimesecs"));
    }

    [Fact]
    public void AnEditThatBreaksALoaderRuleIsRefusedWithItsReason()
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "content.json"));
        using var drafts = new ContentDrafts(path, _ => TcTables.Instance);
        var poses = new SpawnPoses(drafts, SpawnKind.Creature, _ => WorldDialect.TrinityCore);
        poses.Load("src", [NewRow(WorldDialect.TrinityCore, SpawnKind.Creature)]);
        var field = SpawnFields.For(WorldDialect.TrinityCore, SpawnKind.Creature).Single(f => f.Column == "spawnDifficulties");

        var (ok, message) = poses.Edit(null, "src", 5, "Set difficulties", r => SpawnFields.Apply(field, r, "").Row!);
        Assert.False(ok);
        Assert.Contains("at least one difficulty", message);
        Assert.Empty(drafts.Draft!.NetEdits());
    }

    [Fact]
    public void PhaseHasNoTemplateToFallBackToAndEmptyHintsSayWhatEmptyMeans()
    {
        var fields = SpawnFields.For(WorldDialect.TrinityCore, SpawnKind.Creature).ToDictionary(f => f.Column);
        var row = NewRow(WorldDialect.TrinityCore, SpawnKind.Creature);
        foreach (string column in new[] { "PhaseId", "PhaseGroup" })
        {
            // 0 means no phase: no "Use template" control, and a NULL column reads as an empty field that saves as 0.
            Assert.False(fields[column].Inheritable);
            Assert.False(fields[column].IsOverridden(row));
            Assert.Equal("", fields[column].Show(row.With(column, null)));
            Assert.Equal(0L, fields[column].Parse("").Value);
        }

        Assert.Equal("template", fields["npcflag"].EmptyHint);
        Assert.Equal("default", fields["curHealthPct"].EmptyHint);
        Assert.Equal("none", fields["StringId"].EmptyHint);
        // An ordinary number is still not allowed to be blank.
        Assert.False(fields["spawntimesecs"].Parse("").Ok);
    }

    static (ContentDrafts Drafts, SpawnPoses Poses) Loaded(WorldDialect dialect, Row spawn, Row? addon = null)
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "content.json"));
        var drafts = new ContentDrafts(path, _ => dialect == WorldDialect.TrinityCore ? TcTables.Instance : AcTables.Instance);
        var poses = new SpawnPoses(drafts, SpawnKind.Creature, _ => dialect);
        var rows = new RowSet();
        var schema = WorldSchemas.For(dialect);
        rows.Set(SpawnEdits.SpawnTable(schema, SpawnKind.Creature), spawn);
        if (addon != null)
        {
            rows.Set(SpawnEdits.AddonTable(schema, SpawnKind.Creature), addon);
        }

        poses.Load("src", rows);
        return (drafts, poses);
    }

    [Fact]
    public void ASpawnWithoutAnAddonGetsOneWhenAnAppearanceFieldIsSetAndLosesItOnUndo()
    {
        var spawn = NewRow(WorldDialect.TrinityCore, SpawnKind.Creature);
        var (drafts, poses) = Loaded(WorldDialect.TrinityCore, spawn);
        using var _ = drafts;
        Assert.Null(poses.AddonOf("src", 5));
        var field = SpawnFields.AddonFor(WorldDialect.TrinityCore, SpawnKind.Creature).Single(f => f.Column == "emote");
        var blank = SpawnEdits.NewAddon(TcSchema.Instance, SpawnKind.Creature, 5);

        // Setting a field to what a blank addon already holds records nothing.
        Assert.True(poses.EditAddon(null, "src", 5, "Set emote", blank, r => SpawnFields.Apply(field, r, "0").Row!).Ok);
        Assert.Empty(drafts.Draft!.NetEdits());

        var (ok, message) = poses.EditAddon(null, "src", 5, "Set emote", blank, r => SpawnFields.Apply(field, r, "69").Row!);
        Assert.True(ok, message);
        var edit = Assert.Single(drafts.Draft.NetEdits()).Edit;
        Assert.Equal(("creature_addon", null), (edit.Table, edit.Before));
        Assert.Equal(69u, poses.AddonOf("src", 5)!.U("emote"));
        Assert.Contains("INSERT INTO `creature_addon`", SqlExport.Build("src", [], [edit], null, null, TcSchema.Instance).Apply);

        // A second edit updates the staged row: still one insert.
        Assert.True(poses.EditAddon(null, "src", 5, "Set emote", blank, r => SpawnFields.Apply(field, r, "70").Row!).Ok);
        Assert.Equal(70u, SpawnEdits.AddonTable(TcSchema.Instance, SpawnKind.Creature) is { } t ? drafts.Draft.NetEdits().Single().Edit.After!.U("emote") : 0);
        poses.Undo(null);
        poses.Undo(null);
        Assert.Null(poses.AddonOf("src", 5));
    }

    [Fact]
    public void AnExistingAddonRowIsUpdatedNotReplaced()
    {
        var addon = SpawnEdits.NewAddon(TcSchema.Instance, SpawnKind.Creature, 5).With("mount", 14337u);
        var (drafts, poses) = Loaded(WorldDialect.TrinityCore, NewRow(WorldDialect.TrinityCore, SpawnKind.Creature), addon);
        using var _ = drafts;
        var stance = SpawnFields.AddonFor(WorldDialect.TrinityCore, SpawnKind.Creature).Single(f => f.Column == "StandState");
        var blank = SpawnEdits.NewAddon(TcSchema.Instance, SpawnKind.Creature, 5);

        Assert.True(poses.EditAddon(null, "src", 5, "Set stance", blank, r => SpawnFields.Apply(stance, r, "Sleeping").Row!).Ok);
        string apply = SqlExport.Build("src", [], drafts.Draft!.NetEdits().Select(e => e.Edit), null, null, TcSchema.Instance).Apply;
        Assert.Contains("UPDATE `creature_addon` SET `StandState` = 3", apply);
        Assert.DoesNotContain("mount`", apply.Split('\n').Single(l => l.StartsWith("UPDATE `creature_addon`", StringComparison.Ordinal)).Split("WHERE")[0]);
    }

    [Fact]
    public void AzerothCoreKeepsTheOtherBytesWhenStanceAndWeaponsAreSet()
    {
        // bytes1 and bytes2 pack more than the stance and the weapons: only the low byte is the field's.
        var addon = SpawnEdits.NewAddon(AcSchema.Instance, SpawnKind.Creature, 5).With("bytes1", 0x01020300u).With("bytes2", 0x0000_0100u);
        var fields = SpawnFields.AddonFor(WorldDialect.AzerothCore, SpawnKind.Creature).ToDictionary(f => f.Column);
        Assert.Equal("Standing", fields["bytes1"].Show(addon));
        Assert.Equal("Hands free", fields["bytes2"].Show(addon));

        var sitting = SpawnFields.Apply(fields["bytes1"], addon, "Sitting").Row!;
        Assert.Equal(0x01020301u, sitting.U("bytes1"));
        Assert.Equal("Sitting", fields["bytes1"].Show(sitting));
        var drawn = SpawnFields.Apply(fields["bytes2"], sitting, "Melee weapon drawn").Row!;
        Assert.Equal(0x0000_0101u, drawn.U("bytes2"));
        Assert.Equal(0x01020301u, drawn.U("bytes1"));
    }

    [Fact]
    public void TheLoadersAddonChecksRefuseABadStanceAndRepeatedOrNonNumericAuras()
    {
        var (drafts, poses) = Loaded(WorldDialect.TrinityCore, NewRow(WorldDialect.TrinityCore, SpawnKind.Creature));
        using var _ = drafts;
        var blank = SpawnEdits.NewAddon(TcSchema.Instance, SpawnKind.Creature, 5);
        var fields = SpawnFields.AddonFor(WorldDialect.TrinityCore, SpawnKind.Creature).ToDictionary(f => f.Column);

        Assert.Contains("listed twice", poses.EditAddon(null, "src", 5, "Set auras", blank, r => SpawnFields.Apply(fields["auras"], r, "12345 12345").Row!).Message);
        Assert.Contains("not a spell ID", poses.EditAddon(null, "src", 5, "Set auras", blank, r => SpawnFields.Apply(fields["auras"], r, "12345 fire").Row!).Message);
        Assert.Contains("stance 12", poses.EditAddon(null, "src", 5, "Set stance", blank, r => r.With("StandState", 12u)).Message);
        Assert.Empty(drafts.Draft!.NetEdits());

        Assert.True(poses.EditAddon(null, "src", 5, "Set auras", blank, r => SpawnFields.Apply(fields["auras"], r, "12345 67890").Row!).Ok);
        Assert.Equal("12345 67890", poses.AddonOf("src", 5)!["auras"]);
    }
}
