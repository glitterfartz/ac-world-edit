using ACWorldEdit.Assets;
using ACWorldEdit.Tests.Support;
using Xunit;

/// <summary>
/// The retail client's conversation lines: a chain walks NextConversationLineID from a start line,
/// line text is searchable through BroadcastText, and the camera and texture-kit names resolve.
/// </summary>
[Trait("Category", "Corpus")]
[Collection(RetailData.Collection)]
public class CinematicsClientTests
{
    [RetailClientFact]
    public void AChainWalksTheNextLineLinksWithoutRepeating()
    {
        var lookup = new RetailClientLookup(RetailData.Tables);
        uint start = RetailData.Tables.Table("ConversationLine").Values
            .Select(row => (uint)row.ID)
            .First(id => lookup.ConversationLine(id) is { NextLineId: not 0 });

        var chain = lookup.ConversationLineChain(start);

        Assert.True(chain.Count >= 2, $"chain from {start} has {chain.Count} lines");
        Assert.Equal(start, chain[0].Id);
        for (int i = 0; i < chain.Count - 1; i++)
        {
            Assert.Equal(chain[i].NextLineId, chain[i + 1].Id);
        }

        Assert.Equal(chain.Count, chain.Select(line => line.Id).Distinct().Count());
    }

    [RetailClientFact]
    public void LineTextIsFindableThroughItsBroadcastText()
    {
        var lookup = new RetailClientLookup(RetailData.Tables);
        uint line = RetailData.Tables.Table("ConversationLine").Values
            .Select(row => (uint)row.ID)
            .First(id => lookup.ConversationLine(id) is { BroadcastTextId: not 0 } found && lookup.BroadcastText(found.BroadcastTextId) is { Length: > 8 });

        var text = lookup.BroadcastText(lookup.ConversationLine(line)!.BroadcastTextId)!;
        string term = text.Split(' ')[0].Trim('<', '>', '"');
        var matches = lookup.SearchConversationLines(term, 50);

        Assert.Contains(matches, match => match.Id == line);
        Assert.All(matches, match => Assert.True(match.Text.Length > 0));
    }

    [RetailClientFact]
    public void CameraAndTextureKitNamesResolve()
    {
        var lookup = new RetailClientLookup(RetailData.Tables);
        uint camera = RetailData.Tables.Table("UiCamera").Values
            .Where(row => (row.Field<string>("Name") ?? "").Length > 0)
            .Select(row => (uint)row.ID)
            .First();
        Assert.False(string.IsNullOrWhiteSpace(lookup.UiCameraName(camera)));

        uint kit = RetailData.Tables.Table("UiTextureKit").Keys.Select(id => (uint)id).First();
        Assert.False(string.IsNullOrWhiteSpace(lookup.UiTextureKitPrefix(kit)));
        Assert.Null(lookup.ConversationLine(uint.MaxValue));
    }
}
