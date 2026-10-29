using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Placement;

/// <summary>The words the visibility card uses must match what the mask and policy actually mean.</summary>
public class PhasePreviewTextTests
{
    [Theory]
    [InlineData("1", 1u)]
    [InlineData(" 3 ", 3u)]
    [InlineData("0x3", 3u)]
    [InlineData("0XFFFFFFFF", uint.MaxValue)]
    [InlineData("4294967295", uint.MaxValue)]
    public void MasksParseInDecimalAndHex(string text, uint expected)
    {
        Assert.True(AcPhaseMasks.TryParse(text, out uint mask));
        Assert.Equal(expected, mask);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0x")]
    [InlineData("-1")]
    [InlineData("phase 2")]
    [InlineData("4294967296")]
    public void NonMasksAreRefused(string text) => Assert.False(AcPhaseMasks.TryParse(text, out _));

    [Fact]
    public void BitsAreNumberedFromPhaseOne()
    {
        Assert.Equal([1, 3], AcPhaseMasks.Phases(0b101));
        Assert.Equal(32, AcPhaseMasks.Phases(AcPhaseMasks.All).Count());
        Assert.Equal(0b101u, AcPhaseMasks.With(0b001, 3, true));
        Assert.Equal(0b001u, AcPhaseMasks.With(0b101, 3, false));
        Assert.True(AcPhaseMasks.Has(0x80000000, 32));
        Assert.False(AcPhaseMasks.Has(1, 33));
    }

    [Theory]
    [InlineData(0u, "no phase")]
    [InlineData(1u, "phase 1 (normal world)")]
    [InlineData(2u, "phase 2")]
    [InlineData(5u, "phases 1 and 3")]
    [InlineData(0b10101u, "phases 1, 3 and 5")]
    [InlineData(0b1111111u, "7 phases")]
    [InlineData(uint.MaxValue, "all phases")]
    public void MasksReadAsPhases(uint mask, string expected) => Assert.Equal(expected, AcPhaseMasks.Describe(mask));

    [Fact]
    public void TheSummarySaysWhatIsShown()
    {
        Assert.Equal("Showing spawns in phase 1 (normal world). Events follow the database schedule.", PreviewSummary.Describe(new SpawnPreview()));
        Assert.StartsWith("Showing every stored spawn", PreviewSummary.Describe(SpawnPreview.AllStored));
        Assert.Contains("All events.", PreviewSummary.Describe(SpawnPreview.AllStored));
        Assert.Contains("Also: invisible helpers, pool alternatives, conditional spawns, objects that start despawned.", PreviewSummary.Describe(SpawnPreview.AllStored));
        Assert.Equal("Showing spawns in phases 1 and 3. 2 chosen events. Also: without script-controlled spawns.",
            PreviewSummary.Describe(new SpawnPreview { PhaseMask = 5, Events = EventPreviewMode.Selected, ActiveEvents = [1, 2], IncludeScripted = false }));
        Assert.StartsWith("Showing spawns outside any phase.", PreviewSummary.Describe(new SpawnPreview { Dialect = WorldDialect.TrinityCore }));
        Assert.StartsWith("Showing spawns outside any phase and spawns in phase 169.", PreviewSummary.Describe(new SpawnPreview { Dialect = WorldDialect.TrinityCore, PhaseId = 169 }));
    }

    [Fact]
    public void SpawnsAreCountedInEveryPhaseOfTheirMask()
    {
        var catalog = AcPhaseMasks.Catalog([(1u, 100), (3u, 5), (4u, 2), (192u, 7), (AcPhaseMasks.All, 30)]);
        Assert.Equal(30, catalog.EveryPhase);
        Assert.Equal([(1u, 105), (2u, 5), (3u, 2), (7u, 7), (8u, 7)], catalog.Phases.Select(p => (p.Id, p.Spawns)));
        Assert.Equal("Normal world", catalog.Phases[0].Name);
    }

    [Fact]
    public void TwoCatalogsAddUpAndKeepTheNames()
    {
        var creatures = new PhaseCatalog([new(169, "Named", 10), new(170, "", 1)], 2);
        var objects = new PhaseCatalog([new(169, "", 5), new(171, "Other", 3)], 1);
        var both = PhaseCatalog.Merge(creatures, objects);
        Assert.Equal([(169u, "Named", 15), (170u, "", 1), (171u, "Other", 3)], both.Phases.Select(p => (p.Id, p.Name, p.Spawns)));
        Assert.Equal(3, both.EveryPhase);
    }
}
