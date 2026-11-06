using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>
/// A spawn's own addon row replaces its template's completely, so an override must start as a copy of the
/// template row, and an edit of one must never touch the other.
/// </summary>
public class TemplateAddonTests
{
    const string Source = "src";
    static readonly WorldPose Here = new(0, -9449.5, 64.25, 56.5, 1.5);

    public static IEnumerable<object[]> Dialects() => [[WorldDialect.TrinityCore], [WorldDialect.AzerothCore]];

    static ContentDrafts Open(WorldDialect dialect) => new(
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "content.json")),
        _ => dialect == WorldDialect.TrinityCore ? TcTables.Instance : AcTables.Instance);

    static Row FilledTemplate(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        var row = SpawnEdits.NewTemplateAddon(schema, 328).With("mount", 14337u).With("emote", 69u).With("auras", "12345 67890");
        return dialect == WorldDialect.TrinityCore ? row.With("StandState", 1u).With("SheathState", 2u) : row.With("bytes1", 0x01000001u).With("bytes2", 2u);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void TheTemplateAddonIsCataloguedAndSharesItsFieldsWithTheSpawnAddon(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        var template = SpawnEdits.TemplateAddonTable(schema);
        var spawn = SpawnEdits.AddonTable(schema, SpawnKind.Creature);
        Assert.Equal(["entry"], template.Key);
        // Same columns apart from the key, so one field list edits either row.
        Assert.Equal(spawn.Columns.Select(c => c.Name).Where(n => n != "guid"), template.Columns.Select(c => c.Name).Where(n => n != "entry"));
        foreach (var field in SpawnFields.AddonFor(dialect, SpawnKind.Creature))
        {
            Assert.True(template.Has(field.Column), $"{dialect}: {field.Column} is not a column of the template addon");
        }

        SpawnEdits.NewTemplateAddon(schema, 328).Validate(template);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void AnOverrideStartsAsAnExactCopyOfTheTemplateKeyedBySpawn(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        var template = FilledTemplate(dialect);
        var copy = SpawnEdits.AddonFromTemplate(schema, template, 5);
        copy.Validate(SpawnEdits.AddonTable(schema, SpawnKind.Creature));
        Assert.Equal("5", copy["guid"]);
        foreach (var column in SpawnEdits.AddonTable(schema, SpawnKind.Creature).Columns.Select(c => c.Name).Where(n => n != "guid"))
        {
            Assert.Equal(template[column], copy[column]);
        }

        // A NULL in the template stays NULL in the copy.
        if (dialect == WorldDialect.TrinityCore)
        {
            Assert.True(SpawnEdits.AddonFromTemplate(schema, SpawnEdits.NewTemplateAddon(schema, 328), 5).IsNull("auras"));
        }
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void BatchAddonReadsMatchOneByOneReads(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        using var drafts = Open(dialect);
        var poses = new SpawnPoses(drafts, SpawnKind.Creature, _ => dialect);
        var rows = new RowSet();
        rows.Set(SpawnEdits.SpawnTable(schema, SpawnKind.Creature), SpawnEdits.NewCreature(schema, 5, 328, Here, 0, 0));
        rows.Set(SpawnEdits.SpawnTable(schema, SpawnKind.Creature), SpawnEdits.NewCreature(schema, 6, 329, Here, 0, 0));
        rows.Set(SpawnEdits.AddonTable(schema, SpawnKind.Creature), SpawnEdits.AddonFromTemplate(schema, FilledTemplate(dialect), 5));
        poses.Load(Source, rows);

        // The per-frame appearance pass shares one effective view per table; results must equal the single lookups.
        var batch = poses.AddonsOf(Source, [5, 6, 777]);
        Assert.Equal(poses.AddonOf(Source, 5)?.U("mount"), batch[5]?.U("mount"));
        Assert.Equal(poses.AddonOf(Source, 6)?.U("mount"), batch[6]?.U("mount"));
        Assert.Null(batch[777]);

        var addons = new TemplateAddons(drafts, _ => dialect);
        addons.Load(Source, 328, FilledTemplate(dialect));
        addons.Load(Source, 329, null);
        var templates = addons.RowsOf(Source, [328, 329, 999]);
        Assert.Equal(addons.RowOf(Source, 328)?.U("mount"), templates[328]?.U("mount"));
        Assert.Equal(addons.RowOf(Source, 329)?.U("mount"), templates[329]?.U("mount"));
        Assert.Null(templates[999]);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void OverridingThenRemovingGoesBackToTheTemplateAndUndoRestoresEach(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        using var drafts = Open(dialect);
        var poses = new SpawnPoses(drafts, SpawnKind.Creature, _ => dialect);
        poses.Load(Source, [SpawnEdits.NewCreature(schema, 5, 328, Here, 0, 0)]);
        var template = FilledTemplate(dialect);

        Assert.Null(poses.AddonOf(Source, 5));
        var (ok, message) = poses.OverrideAddon(null, schema, Source, 5, template);
        Assert.True(ok, message);
        var own = poses.AddonOf(Source, 5)!;
        Assert.Equal(template["mount"], own["mount"]);
        Assert.Equal(template["auras"], own["auras"]);
        Assert.Contains("INSERT INTO `creature_addon`", SqlExport.Build(Source, [], drafts.Draft!.NetEdits().Select(e => e.Edit), null, null, schema).Apply);

        // Overriding again changes nothing.
        Assert.True(poses.OverrideAddon(null, schema, Source, 5, template).Ok);
        Assert.Single(drafts.Draft.NetEdits());

        // Removing a staged override leaves nothing to export; undoing the removal brings it back.
        Assert.True(poses.RemoveAddon(null, Source, 5).Ok);
        Assert.Null(poses.AddonOf(Source, 5));
        Assert.Empty(drafts.Draft.NetEdits());
        poses.Undo(null);
        Assert.NotNull(poses.AddonOf(Source, 5));
    }

    [Fact]
    public void RemovingARowTheDatabaseHoldsExportsAGuardedDelete()
    {
        var schema = TcSchema.Instance;
        using var drafts = Open(WorldDialect.TrinityCore);
        var poses = new SpawnPoses(drafts, SpawnKind.Creature, _ => WorldDialect.TrinityCore);
        var rows = new RowSet();
        rows.Set(SpawnEdits.SpawnTable(schema, SpawnKind.Creature), SpawnEdits.NewCreature(schema, 5, 328, Here, 0, 0));
        rows.Set(SpawnEdits.AddonTable(schema, SpawnKind.Creature), SpawnEdits.AddonFromTemplate(schema, FilledTemplate(WorldDialect.TrinityCore), 5));
        poses.Load(Source, rows);

        Assert.True(poses.RemoveAddon(null, Source, 5).Ok);
        string apply = SqlExport.Build(Source, [], drafts.Draft!.NetEdits().Select(e => e.Edit), null, null, schema).Apply;
        Assert.Contains("DELETE FROM `creature_addon` WHERE `guid` = 5", apply);
        Assert.True(poses.RemoveAddon(null, Source, 777).Ok);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void EditingTheTemplateNeverTouchesASpawnRowAndNeedsItsRowLoaded(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        using var drafts = Open(dialect);
        var addons = new TemplateAddons(drafts, _ => dialect);
        var blank = SpawnEdits.NewTemplateAddon(schema, 328);
        var emote = SpawnFields.AddonFor(dialect, SpawnKind.Creature).Single(f => f.Column == "emote");

        // The database's answer must be known before an edit, or an INSERT could collide with a row nobody read.
        Assert.Contains("loading", addons.Edit(null, Source, 328, "Set emote", blank, r => SpawnFields.Apply(emote, r, "69").Row!).Message);
        addons.Load(Source, 328, null);
        Assert.Null(addons.RowOf(Source, 328));

        var (ok, message) = addons.Edit(null, Source, 328, "Set emote", blank, r => SpawnFields.Apply(emote, r, "69").Row!);
        Assert.True(ok, message);
        var edit = Assert.Single(drafts.Draft!.NetEdits()).Edit;
        Assert.Equal("creature_template_addon", edit.Table);
        Assert.Contains("INSERT INTO `creature_template_addon`", SqlExport.Build(Source, [], [edit], null, null, schema).Apply);

        // A second edit updates the staged row; undo twice leaves no row.
        Assert.True(addons.Edit(null, Source, 328, "Set emote", blank, r => SpawnFields.Apply(emote, r, "70").Row!).Ok);
        Assert.Equal(70u, addons.RowOf(Source, 328)!.U("emote"));
        addons.Undo(null);
        addons.Undo(null);
        Assert.Null(addons.RowOf(Source, 328));
    }

    [Fact]
    public void TheTemplateAddonUpdatesOnlyTheChangedColumnAndRefusesWhatTheLoaderRefuses()
    {
        var schema = TcSchema.Instance;
        using var drafts = Open(WorldDialect.TrinityCore);
        var addons = new TemplateAddons(drafts, _ => WorldDialect.TrinityCore);
        addons.Load(Source, 328, FilledTemplate(WorldDialect.TrinityCore));
        var blank = SpawnEdits.NewTemplateAddon(schema, 328);
        var fields = SpawnFields.AddonFor(WorldDialect.TrinityCore, SpawnKind.Creature).ToDictionary(f => f.Column);

        Assert.True(addons.Edit(null, Source, 328, "Set stance", blank, r => SpawnFields.Apply(fields["StandState"], r, "Sleeping").Row!).Ok);
        string apply = SqlExport.Build(Source, [], drafts.Draft!.NetEdits().Select(e => e.Edit), null, null, schema).Apply;
        Assert.Contains("UPDATE `creature_template_addon` SET `StandState` = 3", apply);

        Assert.Contains("stance 12", addons.Edit(null, Source, 328, "Set stance", blank, r => r.With("StandState", 12u)).Message);
        Assert.Contains("listed twice", addons.Edit(null, Source, 328, "Set auras", blank, r => SpawnFields.Apply(fields["auras"], r, "1 1").Row!).Message);
        Assert.Single(drafts.Draft.NetEdits());
    }
}
