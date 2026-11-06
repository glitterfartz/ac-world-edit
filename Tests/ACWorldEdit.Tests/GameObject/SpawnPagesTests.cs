using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.GameObject;

/// <summary>The spawn-list paging both Spawns pages share: page counts, slices and clamping.</summary>
public class SpawnPagesTests
{
    [Fact]
    public void EmptyListHasNoPages()
    {
        Assert.Equal(0, SpawnPages.PageCount(0));
        Assert.Equal(0, SpawnPages.Clamp(3, 0));
        Assert.Equal((0, 0), SpawnPages.Slice(0, 0));
    }

    [Fact]
    public void PartialPageCountsOnce()
    {
        Assert.Equal(1, SpawnPages.PageCount(1));
        Assert.Equal(1, SpawnPages.PageCount(SpawnPages.PageSize));
        Assert.Equal((0, 7), SpawnPages.Slice(0, 7));
    }

    [Fact]
    public void FullPagesSliceEvenly()
    {
        Assert.Equal(2, SpawnPages.PageCount(SpawnPages.PageSize + 1));
        Assert.Equal((0, SpawnPages.PageSize), SpawnPages.Slice(0, SpawnPages.PageSize + 1));
        Assert.Equal((SpawnPages.PageSize, 1), SpawnPages.Slice(1, SpawnPages.PageSize + 1));
    }

    [Fact]
    public void OutOfRangePagesClamp()
    {
        Assert.Equal(0, SpawnPages.Clamp(-1, 10));
        Assert.Equal(0, SpawnPages.Clamp(5, 10));
        Assert.Equal((0, 10), SpawnPages.Slice(9, 10));
        Assert.Equal((0, 10), SpawnPages.Slice(-2, 10));
    }
}
