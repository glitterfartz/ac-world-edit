using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Conditions;

public class ConditionCatalogTests
{
    public static TheoryData<WorldDialect> Dialects => new() { WorldDialect.AzerothCore, WorldDialect.TrinityCore };

    static ConditionCatalog Of(WorldDialect dialect) => WorldSchemas.For(dialect).Conditions;

    [Theory]
    [MemberData(nameof(Dialects))]
    public void IdsAreUnique(WorldDialect dialect)
    {
        var catalog = Of(dialect);
        Assert.Equal(catalog.Types.Count, catalog.Types.Select(t => t.Id).Distinct().Count());
        Assert.Equal(catalog.Sources.Count, catalog.Sources.Select(s => s.Id).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void EveryTypeHasASentenceThatUsesOnlyNamedParameters(WorldDialect dialect)
    {
        foreach (var type in Of(dialect).Types)
        {
            Assert.True(type.Sentence.Length > 0, $"{type.Id} {type.Code} has no sentence");
            for (int slot = 1; slot <= 3; slot++)
            {
                if (type.Sentence.Contains("{" + slot + "}"))
                {
                    Assert.True(type.Params.Length >= slot && type.Params[slot - 1].Label.Length > 0, $"{type.Id} {type.Code} sentence uses {{{slot}}} without naming it");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void GossipSourcesAreKnown(WorldDialect dialect)
    {
        var catalog = Of(dialect);
        Assert.Equal("Gossip menu", catalog.Source(14)!.GroupLabel);
        Assert.Equal("Text id", catalog.Source(14)!.EntryLabel);
        Assert.Equal("Option id", catalog.Source(15)!.EntryLabel);
    }

    [Fact]
    public void AzerothCoreOwnTypesStartAt101AndTerrainSwapIsNotForThisClient()
    {
        var catalog = ConditionCatalog.Ac;
        Assert.Null(catalog.Type(50));
        Assert.Equal("CONDITION_QUEST_SATISFY_EXCLUSIVE", catalog.Type(101)!.Code);
        Assert.Equal(ConditionStatus.NotForClient, catalog.Type(41)!.Status);
        Assert.Equal(ConditionStatus.NotForClient, catalog.Source(26)!.Status);
        Assert.Null(catalog.Source(31));
    }

    [Fact]
    public void TrinityCoreSourceTwentyIsUnusedAndLegacyTypesAreDeprecated()
    {
        var catalog = ConditionCatalog.Trinity;
        Assert.Null(catalog.Source(20));
        Assert.Equal("CONDITION_SOURCE_TYPE_VEHICLE_SPELL", catalog.Source(21)!.Code);
        Assert.Equal(36, catalog.Sources.Max(s => s.Id));
        Assert.Equal(ConditionStatus.Deprecated, catalog.Type(31)!.Status);
        Assert.Equal("CONDITION_OBJECT_ENTRY_GUID", catalog.Type(51)!.Code);
        Assert.Equal(60, catalog.Types.Max(t => t.Id));
    }

    [Fact]
    public void ADifferingTypeKeepsItsOwnDialectsParameters()
    {
        // AzerothCore's aura check can test the target instead of the player; TrinityCore's cannot.
        Assert.Equal(3, ConditionCatalog.Ac.Type(1)!.Params.Length);
        Assert.Equal(2, ConditionCatalog.Trinity.Type(1)!.Params.Length);
        Assert.Equal("CONDITION_PHASEMASK", ConditionCatalog.Ac.Type(26)!.Code);
        Assert.Equal("CONDITION_PHASEID", ConditionCatalog.Trinity.Type(26)!.Code);
    }

    [Fact]
    public void DescribeFillsValuesAndNegates()
    {
        var catalog = ConditionCatalog.Trinity;
        Assert.Equal("the target has aura 1459 (effect 0)", catalog.Describe(1, 1459, 0, 0));
        Assert.Equal("not: the target is in zone 12", catalog.Describe(4, 12, 0, 0, negative: true));
        Assert.Equal("always", catalog.Describe(0, 0, 0, 0));
        Assert.Equal("condition type 999 (1, 2, 3)", catalog.Describe(999, 1, 2, 3));
    }
}
