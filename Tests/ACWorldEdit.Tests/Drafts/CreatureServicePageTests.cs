using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

public class CreatureServicePageTests
{
    public static TheoryData<WorldDialect> Dialects => new() { WorldDialect.AzerothCore, WorldDialect.TrinityCore };

    [Theory]
    [MemberData(nameof(Dialects))]
    public void TrainerSpellsHaveCompleteDefaultsAndPreserveRequirementsThroughEdits(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        var page = schema.TrainerPage;
        var spec = page.Spells;
        var rows = new RowSet();
        var first = spec.Add(rows, 42, ("SpellId", uint.MaxValue), ("ReqLevel", 20u), ("ReqSkillLine", 164u), ("ReqSkillRank", 75u)).After!;
        if (dialect == WorldDialect.TrinityCore) first = first.With("ReqAbility1", 123u).With("ReqAbility2", 456u).With("ReqAbility3", 789u);
        first.Validate(spec.Table);
        Assert.Equal(spec.Table.Columns.Length, first.Values.Count);
        rows.Set(spec.Table, first);
        var low = spec.Add(rows, 42, ("SpellId", 7u)).After!;
        rows.Set(spec.Table, low);
        rows.Set(spec.Table, spec.Add(rows, 43, ("SpellId", 1u)).After!);
        Assert.Equal(new[] { low, first }, spec.Select(rows, 42));
        Assert.All(spec.Fields, f =>
        {
            Assert.Same(schema.Tables.Find(f.Table.Name), f.Table);
            Assert.True(f.Table.Has(f.Column));
            Assert.Equal(RowBinding.Item, f.Row);
        });
        Assert.Equal(spec.Table.Columns.Length - 2, spec.Fields.Length);
        var binding = page.Bindings.Add(rows, 9).After!;
        binding.Validate(page.Bindings.Table);
        Assert.Equal(page.Bindings.Table.Columns.Length, binding.Values.Count);
        Assert.Same(schema.Tables.Find(page.Trainer.Name), page.Trainer);
        using var draft = Draft(schema);
        var changed = first.With("MoneyCost", uint.MaxValue).With("ReqLevel", 30u);
        draft.Execute("source", rows, "Edit trainer spell", ItemListSpec.Change(spec.Table, first, changed));
        Assert.Contains(changed, spec.Select(draft.Effective("source", rows), 42));
        Assert.Equal(first.U("ReqSkillRank"), changed.U("ReqSkillRank"));
        var sql = Export(schema, draft);
        Assert.Contains("`MoneyCost` = 4294967295", sql.Apply);
        Assert.Contains("`ReqLevel` = 20", sql.Revert);
        draft.Undo();
        Assert.Empty(draft.NetEdits());
        draft.Redo();
        Assert.Contains(changed, spec.Select(draft.Effective("source", rows), 42));
        draft.Execute("source", rows, "Remove spell", [spec.Remove(changed)]);
        Assert.DoesNotContain(changed, spec.Select(draft.Effective("source", rows), 42));
        Assert.Contains("DELETE FROM `trainer_spell`", Export(schema, draft).Apply);
        draft.Undo();
        Assert.Contains(changed, spec.Select(draft.Effective("source", rows), 42));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void QuestRolesChangeTogetherAndDoNotTouchOtherCreaturesOrQuests(WorldDialect dialect)
    {
        var schema = WorldSchemas.For(dialect);
        var page = schema.QuestLinksPage;
        var rows = new RowSet();
        rows.Set(page.Starts.Table, page.Starts.Add(rows, 8, ("quest", 100u)).After!);
        rows.Set(page.Starts.Table, page.Starts.Add(rows, 9, ("quest", 101u)).After!);
        using var draft = Draft(schema);
        draft.Execute("source", rows, "Link both roles", page.SetRoles(rows, 9, 100, true, true));
        var effective = draft.Effective("source", rows);
        Assert.Single(page.Ends.Select(effective, 9));
        Assert.Equal(new uint[] { 100, 101 }, page.Starts.Select(effective, 9).Select(r => r.U("quest")));
        Assert.Empty(page.SetRoles(effective, 9, 100, true, true));
        var sql = Export(schema, draft);
        Assert.Contains("INSERT INTO `creature_queststarter`", sql.Apply);
        Assert.Contains("INSERT INTO `creature_questender`", sql.Apply);
        Assert.Contains("DELETE FROM `creature_queststarter`", sql.Revert);
        draft.Execute("source", rows, "Accept only", page.SetRoles(effective, 9, 100, false, true));
        effective = draft.Effective("source", rows);
        Assert.Equal(101u, Assert.Single(page.Starts.Select(effective, 9)).U("quest"));
        Assert.Single(page.Starts.Select(effective, 8));
        Assert.Single(page.Ends.Select(effective, 9));
        draft.Undo();
        Assert.Equal(2, page.Starts.Select(draft.Effective("source", rows), 9).Count());
        draft.Redo();
        effective = draft.Effective("source", rows);
        draft.Execute("source", rows, "Unlink", page.SetRoles(effective, 9, 100, false, false));
        Assert.Empty(draft.NetEdits());
        Assert.Single(page.Starts.Select(draft.Effective("source", rows), 8));
    }

    [Fact]
    public void RetailTrainerBindingsKeepDistinctMenuOptions()
    {
        var spec = TcSchema.Instance.TrainerPage.Bindings;
        var rows = new RowSet();
        foreach (var (menu, option, trainer) in new[] { (2u, 9u, 42u), (0u, 0u, 43u), (2u, 1u, 42u) })
            rows.Set(spec.Table, spec.Add(rows, 9, ("MenuID", menu), ("OptionID", option), ("TrainerID", trainer)).After!);
        Assert.Equal(new uint[] { 0, 1, 9 }, spec.Select(rows, 9).Select(r => r.U("OptionID")));
        Assert.Empty(spec.Select(rows, 10));
    }

    static InteractionDraft Draft(IWorldSchema schema) => new(
        Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"), "services.json"), schema.Tables);
    static SqlScripts Export(IWorldSchema schema, InteractionDraft draft) => SqlExport.Build("source", [], [],
        draft.NetEdits().Select(e => e.Edit).ToArray(), DateTime.UnixEpoch, schema: schema);
}
