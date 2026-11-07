using ACWorldEdit.Assets;
using ACWorldEdit.Tests.Support;
using Xunit;

namespace ACWorldEdit.Tests.Corpus;

/// <summary>
/// Retail WMO dynamic lights (MNLD): entry counts and field sanity against the real
/// client. CPU checks only; the brightness they add is verified in the viewer.
/// </summary>
[Trait("Category", "Corpus")]
[Collection(RetailData.Collection)]
public class WmoLightCorpusTests
{
    static IReadOnlyList<WmoLight> Lights(string path)
    {
        var files = RetailData.Files;
        files.PathResolver ??= PathListfile.Load(ListfileEnvironment.Path()!).Resolve;
        return WmoModel.Read(files, path).Lights ?? [];
    }

    static void CheckSane(IReadOnlyList<WmoLight> lights, string path)
    {
        foreach (var l in lights)
        {
            Assert.True(l.Type is 0 or 1, $"{path}: unexpected light type {l.Type}");
            Assert.True(float.IsFinite(l.Position.X + l.Position.Y + l.Position.Z), $"{path}: non-finite position");
            Assert.True(l.AttenEnd >= l.AttenStart && l.AttenStart >= 0, $"{path}: bad range [{l.AttenStart},{l.AttenEnd}]");
            Assert.True(float.IsFinite(l.Intensity) && l.Intensity >= 0, $"{path}: bad intensity {l.Intensity}");
            Assert.True(l.Color is { X: >= 0 } && l.Color.X <= 1 && l.Color.Y >= 0 && l.Color.Y <= 1 && l.Color.Z >= 0 && l.Color.Z <= 1, $"{path}: bad color {l.Color}");
            if (l.Type == 1)
            {
                Assert.True(l.InnerAngle >= 0 && l.OuterAngle >= l.InnerAngle, $"{path}: bad cone [{l.InnerAngle},{l.OuterAngle}]");
                Assert.True(Math.Abs(l.Direction.Length() - 1) < 0.01, $"{path}: spot direction not unit {l.Direction}");
            }
        }
    }

    [RetailListfileFact]
    public void KhazAlgarCaveCarriesFourtyFivePointLights()
    {
        var lights = Lights("fdid:5305801.wmo");
        Assert.Equal(45, lights.Count);
        Assert.All(lights, l => Assert.Equal(0, l.Type));
        CheckSane(lights, "fdid:5305801.wmo");
    }

    [RetailListfileFact]
    public void StormwindPortalRoomCarriesSpotsAmongItsLights()
    {
        var lights = Lights("fdid:2320850.wmo");
        Assert.Equal(161, lights.Count);
        Assert.Equal(16, lights.Count(l => l.Type == 1));
        CheckSane(lights, "fdid:2320850.wmo");
    }
}
