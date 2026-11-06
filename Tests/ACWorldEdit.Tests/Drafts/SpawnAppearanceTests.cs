using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>
/// A spawn's own addon row replaces its template's completely, so the editor keeps that row only while it differs
/// from what the template supplies: the first edit starts from the template's values, and an edit that returns to
/// them removes the row.
/// </summary>
public class SpawnAppearanceTests
{
    const string Source = "src";
    const ulong Guid = 5;
    const uint Entry = 1234;
    static readonly WorldPose Here = new(0, -9449.5, 64.25, 56.5, 1.5);

    public static IEnumerable<object[]> Dialects() => [[WorldDialect.TrinityCore], [WorldDialect.AzerothCore]];

    static IWorldSchema Schema(WorldDialect dialect) => WorldSchemas.For(dialect);

    static string PathColumn(WorldDialect dialect) => dialect == WorldDialect.TrinityCore ? "PathId" : "path_id";

    /// <summary>A template whose appearance is a mount, an emote and two auras.</summary>
    static Row Template(WorldDialect dialect) => SpawnEdits.NewTemplateAddon(Schema(dialect), Entry).With("mount", 777u).With("emote", 5u).With("auras", "111 222");

    static (ContentDrafts Drafts, SpawnPoses Poses) Open(WorldDialect dialect, Row? ownAddon = null)
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "acwe-tests", System.Guid.NewGuid().ToString("N"), "content.json"));
        var drafts = new ContentDrafts(path, _ => dialect == WorldDialect.TrinityCore ? TcTables.Instance : AcTables.Instance);
        var poses = new SpawnPoses(drafts, SpawnKind.Creature, _ => dialect);
        var rows = new RowSet();
        rows.Set(SpawnEdits.SpawnTable(Schema(dialect), SpawnKind.Creature), SpawnEdits.NewCreature(Schema(dialect), Guid, Entry, Here, 0, 0));
        if (ownAddon != null)
        {
            rows.Set(SpawnEdits.AddonTable(Schema(dialect), SpawnKind.Creature), ownAddon);
        }

        poses.Load(Source, rows);
        return (drafts, poses);
    }

    static Row Baseline(WorldDialect dialect, Row? template) => SpawnEdits.AddonBaseline(Schema(dialect), SpawnKind.Creature, template, Guid);

    [Theory]
    [MemberData(nameof(Dialects))]
    public void TheFirstEditStartsFromTheTemplatesValuesSoNothingItSuppliesIsLost(WorldDialect dialect)
    {
        var (drafts, poses) = Open(dialect);
        using var _ = drafts;
        var result = poses.EditAddon(null, Source, Guid, "Set emote", Baseline(dialect, Template(dialect)), row => row.With("emote", 69u));
        Assert.True(result.Ok, result.Message);

        var own = poses.AddonOf(Source, Guid)!;
        Assert.Equal((69u, 777u, "111 222"), (own.U("emote"), own.U("mount"), own["auras"]));
        Assert.Equal(Guid, own.UL("guid"));
        Assert.Contains("INSERT INTO `creature_addon`", SqlExport.Build(Source, [], drafts.Draft!.NetEdits().Select(e => e.Edit), null, null, Schema(dialect)).Apply);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void ReturningToTheTemplatesValuesDeletesTheRowAndUndoBringsItBack(WorldDialect dialect)
    {
        var template = Template(dialect);
        var own = SpawnEdits.AddonFromTemplate(Schema(dialect), template, Guid).With("emote", 69u);
        var (drafts, poses) = Open(dialect, own);
        using var _ = drafts;

        var result = poses.EditAddon(null, Source, Guid, "Set emote", Baseline(dialect, template), row => row.With("emote", 5u));
        Assert.True(result.Ok && result.Removed, result.Message);
        Assert.Null(poses.AddonOf(Source, Guid));
        var edit = Assert.Single(drafts.Draft!.NetEdits()).Edit;
        Assert.Null(edit.After);
        Assert.Contains("DELETE FROM `creature_addon` WHERE `guid` = 5", SqlExport.Build(Source, [], [edit], null, null, Schema(dialect)).Apply);

        poses.Undo(null);
        Assert.Equal(69u, poses.AddonOf(Source, Guid)!.U("emote"));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void PlacingWithAnAppearanceThenRevertingLeavesOnlyTheSpawn(WorldDialect dialect)
    {
        var template = Template(dialect);
        var (drafts, poses) = Open(dialect);
        using var _ = drafts;
        var spawn = SpawnEdits.NewCreature(Schema(dialect), 9_000_001, Entry, Here, 0, 0);
        var addon = SpawnEdits.AddonFromTemplate(Schema(dialect), template, 9_000_001).With("emote", 69u);
        Assert.True(poses.Commit(null, Source, "Place", SpawnEdits.Place(Schema(dialect), SpawnKind.Creature, spawn, addon)).Ok);
        Assert.Equal(2, drafts.Draft!.NetEdits().Count);

        var baseline = SpawnEdits.AddonBaseline(Schema(dialect), SpawnKind.Creature, template, 9_000_001);
        var result = poses.EditAddon(null, Source, 9_000_001, "Set emote", baseline, row => row.With("emote", 5u));
        Assert.True(result.Ok && result.Removed, result.Message);
        Assert.Equal("creature", Assert.Single(drafts.Draft.NetEdits()).Edit.Table);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void ARowThatHoldsAPathIsNeverPrunedAndSaysSo(WorldDialect dialect)
    {
        var template = Template(dialect);
        var own = SpawnEdits.AddonFromTemplate(Schema(dialect), template, Guid).With(PathColumn(dialect), 802630u).With("emote", 69u);
        var (drafts, poses) = Open(dialect, own);
        using var _ = drafts;

        var result = poses.UseTemplateAppearance(null, Schema(dialect), dialect, Source, Guid, template);
        Assert.True(result.Ok && !result.Removed);
        Assert.Contains("waypoint path 802630", result.Message);
        var kept = poses.AddonOf(Source, Guid)!;
        Assert.Equal((5u, 777u, 802630u), (kept.U("emote"), kept.U("mount"), kept.U(PathColumn(dialect))));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void UsingTheTemplatesAppearanceDeletesARowThatOnlyDiffersInTheAppearanceFields(WorldDialect dialect)
    {
        var template = Template(dialect);
        var own = SpawnEdits.AddonFromTemplate(Schema(dialect), template, Guid).With("emote", 69u).With("mount", 0u).With("auras", "999");
        var (drafts, poses) = Open(dialect, own);
        using var _ = drafts;

        var result = poses.UseTemplateAppearance(null, Schema(dialect), dialect, Source, Guid, template);
        Assert.True(result.Ok && result.Removed, result.Message);
        Assert.Null(poses.AddonOf(Source, Guid));
        // A spawn with no row has nothing to remove.
        Assert.True(poses.UseTemplateAppearance(null, Schema(dialect), dialect, Source, Guid, template) is { Ok: true, Removed: false });
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void WithoutATemplateAddonTheBaselineIsBlankAndReturningToDefaultsPrunes(WorldDialect dialect)
    {
        var own = SpawnEdits.NewAddon(Schema(dialect), SpawnKind.Creature, Guid).With("emote", 69u);
        var (drafts, poses) = Open(dialect, own);
        using var _ = drafts;

        // Mount 0 under a template with mount X would be a real override; with no template addon it is nothing.
        var result = poses.EditAddon(null, Source, Guid, "Set emote", Baseline(dialect, null), row => row.With("emote", 0u));
        Assert.True(result.Ok && result.Removed, result.Message);
        Assert.Null(poses.AddonOf(Source, Guid));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void AMountOfZeroUnderATemplateWithAMountIsARealOverrideAndStays(WorldDialect dialect)
    {
        var (drafts, poses) = Open(dialect);
        using var _ = drafts;
        var result = poses.EditAddon(null, Source, Guid, "Set mount", Baseline(dialect, Template(dialect)), row => row.With("mount", 0u));
        Assert.True(result.Ok && !result.Removed, result.Message);
        Assert.Equal(0u, poses.AddonOf(Source, Guid)!.U("mount"));
        Assert.Equal(5u, poses.AddonOf(Source, Guid)!.U("emote"));
    }

    [Fact]
    public void NullAndEmptyAurasAreTheSameAppearance()
    {
        var table = TcTables.SpawnCreatureAddon;
        var a = SpawnEdits.NewAddon(TcSchema.Instance, SpawnKind.Creature, 5);
        Assert.True(a.IsNull("auras"));
        Assert.True(SpawnEdits.SameAddon(table, a, a.With("auras", "")));
        Assert.True(SpawnEdits.SameAddon(table, a.With("auras", "1  2"), a.With("auras", " 1 2 ")));
        Assert.False(SpawnEdits.SameAddon(table, a, a.With("auras", "1")));
        Assert.False(SpawnEdits.SameAddon(table, a, a.With("emote", 1u)));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void ATemplateEditChangesNoSpawnRow(WorldDialect dialect)
    {
        var own = SpawnEdits.AddonFromTemplate(Schema(dialect), Template(dialect), Guid);
        var (drafts, poses) = Open(dialect, own);
        using var _ = drafts;
        var templates = new TemplateAddons(drafts, _ => dialect);
        templates.Load(Source, Entry, Template(dialect));

        var result = templates.Edit(null, Source, Entry, "Set emote", SpawnEdits.NewTemplateAddon(Schema(dialect), Entry), row => row.With("emote", 99u));
        Assert.True(result.Ok, result.Message);
        Assert.Equal("creature_template_addon", Assert.Single(drafts.Draft!.NetEdits()).Edit.Table);
        Assert.Equal(5u, poses.AddonOf(Source, Guid)!.U("emote"));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void TheTemplateAndSpawnAddonTablesLineUpColumnForColumn(WorldDialect dialect)
    {
        var spawn = SpawnEdits.AddonTable(Schema(dialect), SpawnKind.Creature);
        var template = SpawnEdits.TemplateAddonTable(Schema(dialect));
        // The copy of a template's row into a spawn's reads every column by name, so the two must agree except for the key.
        Assert.Equal(spawn.Columns.Where(c => c.Name != "guid").Select(c => (c.Name, c.Kind)).OrderBy(c => c.Name),
            template.Columns.Where(c => c.Name != "entry").Select(c => (c.Name, c.Kind)).OrderBy(c => c.Name));
        Assert.Equal(spawn.IsNullable("auras"), template.IsNullable("auras"));
    }
}
