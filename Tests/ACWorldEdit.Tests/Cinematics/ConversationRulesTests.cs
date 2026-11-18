using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Cinematics;

/// <summary>
/// TcConversationRules over in-memory rows: complete new rows, the actor kind the server derives from
/// the four column combinations, and one loader check per test.
/// </summary>
public class ConversationRulesTests
{
    sealed class Names : IContentNames
    {
        public string? Spell(uint id) => null;
        public string? Creature(uint id) => id == 99426 ? "Anduin" : null;
        public string? GameObject(uint id) => null;
        public string? Quest(uint id) => null;
        public string? Item(uint id) => null;
        public string? Text(uint entry, uint group) => null;
        public bool CreatureSpawnExists(ulong guid) => guid == 280710;
        public string? Emote(uint id) => null;
        public string? TextEmote(uint id) => null;
        public string? FactionTemplate(uint id) => null;
        public string? Faction(uint id) => null;
    }

    static readonly Names Lookup = new();

    static RowSet Set(params (TableSchema Table, Row Row)[] rows)
    {
        var set = new RowSet();
        foreach (var (table, row) in rows)
        {
            row.Validate(table);
            set.Set(table, row);
        }

        return set;
    }

    static List<InteractionIssue> Check(RowSet rows, uint[]? chain = null) =>
        TcConversationRules.Check(754, rows, Lookup, chain).ToList();

    static RowSet Complete()
    {
        var actor = TcConversationRules.NewActor(754, 0).With("CreatureId", 99426u);
        var spawn = TcConversationRules.NewActor(754, 1).With("ConversationActorGuid", 280710UL);
        return Set(
            (TcTables.ConversationTemplate, TcConversationRules.NewConversation(754, 1790)),
            (TcTables.ConversationActors, actor),
            (TcTables.ConversationActors, spawn),
            (TcTables.ConversationLineTemplate, TcConversationRules.NewLineOverride(1790).With("ActorIdx", 0u)),
            (TcTables.ConversationLineTemplate, TcConversationRules.NewLineOverride(1791).With("ActorIdx", 1u)));
    }

    [Fact]
    public void NewConversationFillsEveryColumn()
    {
        var row = TcConversationRules.NewConversation(754, 1790);
        row.Validate(TcTables.ConversationTemplate);

        Assert.Equal(754u, row.U("Id"));
        Assert.Equal(1790u, row.U("FirstLineId"));
        Assert.Equal("", row.T("ScriptName"));
        Assert.Equal(TcTables.ConversationTemplate.Columns.Length, row.Values.Count);
    }

    [Fact]
    public void NewActorLeavesTheObjectFlagsOff()
    {
        var row = TcConversationRules.NewActor(754, 1);
        row.Validate(TcTables.ConversationActors);

        Assert.Equal(1u, row.U("Idx"));
        Assert.Equal(0u, row.U("ConversationActorId"));
        Assert.Equal(0UL, row.UL("ConversationActorGuid"));
        Assert.Equal(0u, row.U("NoActorObject"));
        Assert.Equal(0u, row.U("ActivePlayerObject"));
    }

    [Fact]
    public void ActorKindFollowsTheColumnCombination()
    {
        Assert.Equal(TcConversationRules.ActorKind.Creature, TcConversationRules.Kind(TcConversationRules.NewActor(754, 0).With("CreatureId", 99426u)));
        Assert.Equal(TcConversationRules.ActorKind.Spawn, TcConversationRules.Kind(TcConversationRules.NewActor(754, 0).With("ConversationActorGuid", 280710UL)));
        Assert.Equal(TcConversationRules.ActorKind.NoObject,
            TcConversationRules.Kind(TcConversationRules.NewActor(754, 0).With("NoActorObject", 1u).With("CreatureId", 99426u).With("CreatureDisplayInfoId", 12345u)));
        Assert.Equal(TcConversationRules.ActorKind.ActivePlayer, TcConversationRules.Kind(TcConversationRules.NewActor(754, 0).With("ActivePlayerObject", 1u)));
    }

    [Fact]
    public void CheckAcceptsAConversationWhoseOverridesNameItsActors()
    {
        Assert.Empty(Check(Complete(), [1790, 1791, 1792]));
    }

    [Fact]
    public void CheckRejectsAnOverrideThatNamesNoActor()
    {
        var rows = Set(
            (TcTables.ConversationTemplate, TcConversationRules.NewConversation(754, 1790)),
            (TcTables.ConversationActors, TcConversationRules.NewActor(754, 0).With("CreatureId", 99426u)),
            (TcTables.ConversationLineTemplate, TcConversationRules.NewLineOverride(1790).With("ActorIdx", 7u)));

        Assert.Contains(Check(rows, [1790]), issue => issue.Error && issue.Message.Contains("actor 7"));
    }

    [Fact]
    public void CheckRejectsASpawnActorThatDoesNotExist()
    {
        var rows = Set(
            (TcTables.ConversationTemplate, TcConversationRules.NewConversation(754, 1790)),
            (TcTables.ConversationActors, TcConversationRules.NewActor(754, 0).With("ConversationActorGuid", 999999UL)));

        Assert.Contains(Check(rows), issue => issue.Error && issue.Message.Contains("spawn 999999"));
    }

    [Fact]
    public void CheckWarnsAboutAnOverrideForALineOutsideTheChain()
    {
        var rows = Set(
            (TcTables.ConversationTemplate, TcConversationRules.NewConversation(754, 1790)),
            (TcTables.ConversationActors, TcConversationRules.NewActor(754, 0).With("CreatureId", 99426u)),
            (TcTables.ConversationLineTemplate, TcConversationRules.NewLineOverride(9999)));

        Assert.Contains(Check(rows, [1790, 1791]), issue => !issue.Error && issue.Message.Contains("line 9999"));
    }

    [Fact]
    public void NextActorIdxFollowsTheHighestIndex()
    {
        Assert.Equal(2u, TcConversationRules.NextActorIdx(Complete(), 754));
        Assert.Equal(0u, TcConversationRules.NextActorIdx(new RowSet(), 754));
    }
}
