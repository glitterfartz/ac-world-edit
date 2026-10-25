using ACWorldEdit.Assets;
using Xunit;

/// <summary>Geoset variant selection for customized retail humanoids.</summary>
public class GeosetVariantsTests
{
    [Fact]
    public void TextFormRoundTripsAndComparesByValue()
    {
        var variants = GeosetVariants.From(new Dictionary<int, int> { [32] = 2, [0] = 7, [35] = 0 });

        Assert.Equal("0:7,32:2,35:0", variants.Text);
        Assert.Equal(variants, GeosetVariants.From(new Dictionary<int, int> { [35] = 0, [32] = 2, [0] = 7 }));
        Assert.Equal(new Dictionary<int, int> { [0] = 7, [32] = 2, [35] = 0 }, variants.Parse());
        Assert.Empty(GeosetVariants.From(new Dictionary<int, int>()).Parse());
    }

    [Fact]
    public void ShowsTheChosenVariantOfEachGroup()
    {
        var variants = new Dictionary<int, int> { [0] = 7, [32] = 2, [35] = 0 };

        Assert.True(GeosetVariants.Shows(variants, 0));
        Assert.True(GeosetVariants.Shows(variants, 7));
        Assert.False(GeosetVariants.Shows(variants, 1));
        Assert.True(GeosetVariants.Shows(variants, 3202));
        Assert.False(GeosetVariants.Shows(variants, 3201));
        // Variant 0 hides the group; groups without a choice show variant 1.
        Assert.False(GeosetVariants.Shows(variants, 3501));
        Assert.True(GeosetVariants.Shows(variants, 401));
        Assert.False(GeosetVariants.Shows(variants, 402));
    }

    [Fact]
    public void HiddenHairKeepsTheSkin()
    {
        var bald = new Dictionary<int, int> { [0] = 0 };

        Assert.True(GeosetVariants.Shows(bald, 0));
        Assert.False(GeosetVariants.Shows(bald, 1));
    }
}
