using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>The dialect descriptors that let the gossip editors draw and follow an option without knowing its columns.</summary>
public class GossipServiceTests
{
    static readonly GossipPageSpec Ac = AcSchema.Instance.GossipPage;
    static readonly GossipPageSpec Tc = TcSchema.Instance.GossipPage;

    static Row AcOption(uint icon, uint type) => Row.Of(("MenuID", 1u), ("OptionID", 0u), ("OptionIcon", icon), ("OptionType", type));

    static Row TcOption(uint npc) => Row.Of(("MenuID", 1u), ("OptionID", 0u), ("OptionNpc", npc));

    [Theory]
    [InlineData(1u, GossipService.Chat)]
    [InlineData(2u, GossipService.Quests)]
    [InlineData(3u, GossipService.Vendor)]
    [InlineData(15u, GossipService.Vendor)]
    [InlineData(4u, GossipService.Taxi)]
    [InlineData(5u, GossipService.Trainer)]
    [InlineData(9u, GossipService.Other)]
    public void AzerothCoreOptionTypesMapToTheServiceTheClientOpens(uint type, GossipService expected) =>
        Assert.Equal(expected, Ac.ServiceOf(AcOption(0, type)));

    [Theory]
    [InlineData(0u, GossipService.Chat)]
    [InlineData(1u, GossipService.Vendor)]
    [InlineData(2u, GossipService.Taxi)]
    [InlineData(3u, GossipService.Trainer)]
    [InlineData(6u, GossipService.Other)]
    [InlineData(40u, GossipService.Other)]
    public void TrinityCoreOptionNpcMapsToTheServiceTheClientOpens(uint npc, GossipService expected) =>
        Assert.Equal(expected, Tc.ServiceOf(TcOption(npc)));

    [Fact]
    public void AzerothCoreDrawsTheOptionIconColumn() => Assert.Equal(7u, Ac.IconOf(AcOption(7, 1)));

    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(1u, 1u)]
    [InlineData(3u, 3u)]
    [InlineData(9u, 9u)]
    [InlineData(10u, 0u)]
    [InlineData(61u, 0u)]
    public void TrinityCoreDrawsTheGossipIconWithTheSameNumberAsTheServiceOrTheChatIcon(uint npc, uint icon) =>
        Assert.Equal(icon, Tc.IconOf(TcOption(npc)));

    [Fact]
    public void EnablingGossipSetsOnlyThatBitOfTheNpcFlag()
    {
        var tc = Row.Of(("entry", 5u), ("npcflag", 0x1_0000_0082ul));
        var enabled = Tc.EnableGossip(tc);
        Assert.Equal(0x1_0000_0083ul, enabled.UL("npcflag"));
        Assert.Equal(enabled, Tc.EnableGossip(enabled));
        Assert.Equal(131u, Ac.EnableGossip(Row.Of(("entry", 5u), ("npcflag", 130u))).U("npcflag"));
    }
}
