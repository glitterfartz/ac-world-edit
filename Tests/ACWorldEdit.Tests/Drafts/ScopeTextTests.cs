using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Drafts;

/// <summary>The wording that says how far an edit reaches: one source for the picker headers and the banners.</summary>
public class ScopeTextTests
{
    [Fact]
    public void HeadersNameTheSpawnOrTheTemplateAndItsSpawnCount()
    {
        Assert.Equal("TEMPLATE · Entry 328 · 12 spawns", ScopeText.Header(PropertyScope.Template, 328, 280707, 12));
        Assert.Equal("TEMPLATE · Entry 328 · 1 spawn", ScopeText.Header(PropertyScope.Template, 328, 0, 1));
        Assert.Equal("THIS SPAWN · GUID 280707", ScopeText.Header(PropertyScope.Spawn, 328, 280707, 12));
        Assert.Equal("THIS SPAWN · none selected", ScopeText.Header(PropertyScope.Spawn, 328, 0, 12));
    }

    [Fact]
    public void EveryBannerSaysWhoIsAffectedAndTheThreeScopesReadDifferently()
    {
        string template = ScopeText.Banner(PropertyScope.Template, "Stormwind Guard", 68, 280707, 40);
        string spawn = ScopeText.Banner(PropertyScope.Spawn, "Stormwind Guard", 68, 280707, 40);
        string both = ScopeText.Banner(PropertyScope.Both, "Stormwind Guard", 68, 280707, 40);
        Assert.Contains("every spawn of Stormwind Guard (68, 40 spawns)", template);
        Assert.Contains("keeps it", template);
        Assert.Contains("This spawn only: GUID 280707", spawn);
        Assert.Contains("Other spawns of this template do not change", spawn);
        Assert.Contains("Both scopes", both);
        Assert.Equal(3, new[] { template, spawn, both }.Distinct().Count());
    }

    [Fact]
    public void WithoutASpawnTheSpawnPagesAskForOneAndTheTemplatePagesStillSpeak()
    {
        Assert.Contains("Select a spawn in the world", ScopeText.Banner(PropertyScope.Spawn, "Stormwind Guard", 68, 0, 40));
        Assert.Contains("every spawn of Stormwind Guard", ScopeText.Banner(PropertyScope.Template, "Stormwind Guard", 68, 0, 40));
        Assert.Contains("Select a spawn to see its own rules too", ScopeText.Banner(PropertyScope.Both, "Stormwind Guard", 68, 0, 40));
    }
}
