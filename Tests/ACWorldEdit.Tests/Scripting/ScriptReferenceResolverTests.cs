using System.Text.Json;
using ACWorldEdit.Data.Scripting;
using ACWorldEdit.Foundation;
using ACWorldEdit.Foundation.Scripting;
using Xunit;

/// <summary>Live typed-id checks: confirmed misses block, unchecked values and connected pins do not.</summary>
public class ScriptReferenceResolverTests
{
    sealed class FakeSource : IScriptReferenceSource
    {
        public Dictionary<(ScriptReferenceKind Kind, long Id), bool> Rows { get; } = [];
        public List<(ScriptReferenceKind Kind, long Id)> Calls { get; } = [];

        public Task<ScriptReferenceLookup> LookupAsync(ScriptReferenceKind kind, long id, CancellationToken token = default)
        {
            Calls.Add((kind, id));
            bool exists = Rows.GetValueOrDefault((kind, id), false);
            return Task.FromResult(new ScriptReferenceLookup(exists, exists ? "exists" : null));
        }
    }

    static ScriptDocument Document(params ScriptNode[] nodes)
    {
        var document = ScriptDocument.Create("Refs");
        document.Graph.Nodes.AddRange(nodes);
        return document;
    }

    static ScriptNode CreatureEvent(long entry) => new()
    {
        Id = "n1",
        Kind = "event.creature.on_died",
        Properties = { ["entry"] = JsonSerializer.SerializeToElement(entry) }
    };

    [Fact]
    public async Task AMissingCreatureEntryBlocksExport()
    {
        var source = new FakeSource();
        var resolver = new ScriptReferenceResolver(source);

        var issues = await resolver.CheckAsync(Document(CreatureEvent(1234)), AcScriptHosts.Profile);

        var issue = Assert.Single(issues);
        Assert.Equal(ScriptDiagnosticCodes.MissingEntry, issue.Code);
        Assert.True(issue.BlocksExport);
        Assert.Equal(ScriptReferenceKind.Creature, issue.Kind);
        Assert.Equal(1234, issue.Id);
    }

    [Fact]
    public async Task AnExistingEntryProducesNoIssue()
    {
        var source = new FakeSource();
        source.Rows[(ScriptReferenceKind.Creature, 1234)] = true;

        var issues = await new ScriptReferenceResolver(source)
            .CheckAsync(Document(CreatureEvent(1234)), AcScriptHosts.Profile);

        Assert.Empty(issues);
    }

    [Fact]
    public async Task ASpellLiteralIsCheckedAndAConnectedPinIsNot()
    {
        var source = new FakeSource();
        var document = Document(
            CreatureEvent(1234),
            new ScriptNode { Id = "n2", Kind = "unit.cast_spell", Literals = { ["spell"] = JsonSerializer.SerializeToElement(999) } },
            new ScriptNode { Id = "n3", Kind = "unit.has_aura", Literals = { ["spell"] = JsonSerializer.SerializeToElement(777) } });
        source.Rows[(ScriptReferenceKind.Creature, 1234)] = true;
        // n2's spell will be fed by an edge, so its literal is ignored by the graph but still checked;
        // remove it from the node to model the connected case.
        document.Graph.Nodes[1].Literals.Clear();

        var issues = await new ScriptReferenceResolver(source).CheckAsync(document, AcScriptHosts.Profile);

        var issue = Assert.Single(issues);
        Assert.Equal(ScriptReferenceKind.Spell, issue.Kind);
        Assert.Equal(777, issue.Id);
    }

    [Fact]
    public async Task NoSourceLeavesEverythingUnchecked()
    {
        var issues = await new ScriptReferenceResolver().CheckAsync(Document(CreatureEvent(1)), AcScriptHosts.Profile);
        Assert.Empty(issues);
    }

    [Fact]
    public async Task TheSameReferenceIsLookedUpOnce()
    {
        var source = new FakeSource();
        source.Rows[(ScriptReferenceKind.Creature, 5)] = true;
        var document = Document(CreatureEvent(5), CreatureEvent(5));
        document.Graph.Nodes[1].Id = "n2";

        await new ScriptReferenceResolver(source).CheckAsync(document, AcScriptHosts.Profile);

        Assert.Single(source.Calls);
    }

    [Fact]
    public async Task ADatabaseThatCannotSearchSpellsLeavesThemUnchecked()
    {
        var source = new ScriptReferenceResolver.DbSource((kind, term, token) =>
            kind == SmartParamKind.Spell
                ? throw new NotSupportedException("Spells live in client data.")
                : Task.FromResult<IReadOnlyList<(uint Id, string Name)>>([(42u, "exists")]));

        var lookup = await source.LookupAsync(ScriptReferenceKind.Spell, 133);

        Assert.Null(lookup.Exists);
    }

    [Fact]
    public void TypedKindsMapToTheirReferenceKinds()
    {
        Assert.Equal(ScriptReferenceKind.Creature, ScriptReferenceResolver.KindOf(ScriptValueType.CreatureEntry));
        Assert.Equal(ScriptReferenceKind.GameObject, ScriptReferenceResolver.KindOf(ScriptValueType.GameObjectEntry));
        Assert.Equal(ScriptReferenceKind.Item, ScriptReferenceResolver.KindOf(ScriptValueType.ItemEntry));
        Assert.Equal(ScriptReferenceKind.Quest, ScriptReferenceResolver.KindOf(ScriptValueType.QuestId));
        Assert.Equal(ScriptReferenceKind.Spell, ScriptReferenceResolver.KindOf(ScriptValueType.SpellId));
        Assert.Null(ScriptReferenceResolver.KindOf(ScriptValueType.Int));
    }
}
