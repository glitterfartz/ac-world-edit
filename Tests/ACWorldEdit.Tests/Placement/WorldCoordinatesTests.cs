using System.Numerics;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;
using Xunit;

/// <summary>
/// WorldCoordinates basis/tile/heading round trips and ray hits, plus GroundProbe edge cases beyond
/// the ported PlacementTests.
/// </summary>
public class WorldCoordinatesTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(-8913.23, -133.909, 53.4)]
    [InlineData(17066.6, -17066.6, -500.25)]
    [InlineData(1e-9, -1e9, 123456.789)]
    public void ViewAndServerBasisRoundTripExactly(double x, double y, double z)
    {
        var server = new Point3(x, y, z);
        Assert.Equal(server, WorldCoordinates.ToServer(WorldCoordinates.ToView(server)));
        var view = new Point3(x, y, z);
        Assert.Equal(view, WorldCoordinates.ToView(WorldCoordinates.ToServer(view)));
    }

    [Fact]
    public void ViewBasisMatchesDocumentedAxes()
    {
        // Godot X = -server Y, Y = server Z, Z = -server X.
        Assert.Equal(new Point3(-2, 3, -1), WorldCoordinates.ToView(new(1, 2, 3)));
    }

    [Fact]
    public void PoseRoundTripKeepsHeadingAndPosition()
    {
        var pose = new WorldPose(1, -1234.5, 6789.25, 12.5, 4.0);
        var view = WorldCoordinates.ToView(new(pose.X, pose.Y, pose.Z));
        var back = WorldCoordinates.ToServer(view);
        var restored = pose with { X = back.X, Y = back.Y, Z = back.Z, Heading = WorldCoordinates.NormalizeHeading(pose.Heading) };
        Assert.Equal(pose, restored);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(Math.PI)]
    [InlineData(6.28)]
    public void NormalizeHeadingIsIdentityInsideRange(double heading)
    {
        Assert.Equal(heading, WorldCoordinates.NormalizeHeading(heading));
    }

    [Theory]
    [InlineData(-1e-18)]
    [InlineData(-1e-300)]
    [InlineData(-2 * Math.PI)]
    [InlineData(-100)]
    [InlineData(100)]
    [InlineData(1e6)]
    public void NormalizeHeadingStaysHalfOpenAndIsIdempotent(double heading)
    {
        double once = WorldCoordinates.NormalizeHeading(heading);
        Assert.InRange(once, 0, 2 * Math.PI);
        Assert.NotEqual(2 * Math.PI, once);
        Assert.Equal(once, WorldCoordinates.NormalizeHeading(once));
    }

    [Fact]
    public void NormalizeHeadingAgreesModuloFullTurns()
    {
        double a = WorldCoordinates.NormalizeHeading(1.25);
        Assert.Equal(a, WorldCoordinates.NormalizeHeading(1.25 + 4 * Math.PI), 9);
        Assert.Equal(a, WorldCoordinates.NormalizeHeading(1.25 - 6 * Math.PI), 9);
    }

    [Fact]
    public void NormalizeHeadingPropagatesNonFinite()
    {
        Assert.True(double.IsNaN(WorldCoordinates.NormalizeHeading(double.NaN)));
        Assert.True(double.IsNaN(WorldCoordinates.NormalizeHeading(double.PositiveInfinity)));
    }

    [Theory]
    [InlineData(0, 0, 32, 32)]
    [InlineData(-0.001, -0.001, 32, 32)]
    [InlineData(533.0, 533.0, 31, 31)]
    [InlineData(533.34, 0, 32, 30)]
    [InlineData(-533.34, 0, 32, 33)]
    [InlineData(-8913.23, -133.909, 32, 48)]
    [InlineData(17066.6, -17066.6, 63, 0)]
    public void TileUsesFloorIncludingNegativeCoordinates(double x, double y, int tileX, int tileY)
    {
        Assert.Equal((tileX, tileY), WorldCoordinates.Tile(new(x, y, 0)));
    }

    [Fact]
    public void TileBoundaryBelongsToHigherIndexSide()
    {
        // Exactly on a tile edge floors into the tile whose index is the larger value (server Y negative of the edge).
        double edge = WorldCoordinates.TileSize;
        Assert.Equal((32, 31), WorldCoordinates.Tile(new(edge, 0, 0)));
        Assert.Equal((31, 32), WorldCoordinates.Tile(new(0, edge, 0)));
    }

    [Fact]
    public void HitReturnsRayParameterForFrontAndBackFaces()
    {
        Point3 a = new(0, 0, 0), b = new(10, 0, 0), c = new(0, 0, 10);
        var down = new Point3(0, -1, 0);
        var up = new Point3(0, 1, 0);
        Assert.Equal(5, WorldCoordinates.Hit(new(2, 5, 2), down, a, b, c)!.Value, 9);
        Assert.Equal(5, WorldCoordinates.Hit(new(2, -5, 2), up, a, b, c)!.Value, 9);
    }

    [Fact]
    public void HitRejectsMissParallelBehindAndDegenerate()
    {
        Point3 a = new(0, 0, 0), b = new(10, 0, 0), c = new(0, 0, 10);
        var down = new Point3(0, -1, 0);
        Assert.Null(WorldCoordinates.Hit(new(8, 5, 8), down, a, b, c));
        Assert.Null(WorldCoordinates.Hit(new(2, 5, 2), new(1, 0, 0), a, b, c));
        Assert.Null(WorldCoordinates.Hit(new(2, -5, 2), down, a, b, c));
        Assert.Null(WorldCoordinates.Hit(new(2, 5, 2), down, a, a, a));
    }

    [Fact]
    public void HitAcceptsEdgesAndVerticesWithinTolerance()
    {
        Point3 a = new(0, 0, 0), b = new(10, 0, 0), c = new(0, 0, 10);
        var down = new Point3(0, -1, 0);
        Assert.NotNull(WorldCoordinates.Hit(new(0, 5, 0), down, a, b, c));
        Assert.NotNull(WorldCoordinates.Hit(new(5, 5, 5), down, a, b, c));
        Assert.NotNull(WorldCoordinates.Hit(new(10, 5, 0), down, a, b, c));
        Assert.Null(WorldCoordinates.Hit(new(5.01, 5, 5.01), down, a, b, c));
    }

    [Fact]
    public void ChooseIgnoresNonFiniteAndBoundaryIsInclusive()
    {
        Assert.Null(GroundProbe.Choose([float.NaN, float.PositiveInfinity, float.NegativeInfinity], 0));
        Assert.Equal(3f, GroundProbe.Choose([3f, float.NaN], 0));
        Assert.Equal(GroundProbe.StepUp + 12, GroundProbe.Choose([GroundProbe.StepUp + 12], 12));
        Assert.Null(GroundProbe.Choose([GroundProbe.StepUp + 12.01f], 12));
    }

    [Fact]
    public void ChoosePicksHighestAtOrBelowAndAllowsNegativeHeights()
    {
        Assert.Equal(-5f, GroundProbe.Choose([-20, -5, 100], -4));
        Assert.Equal(7f, GroundProbe.Choose([7, 7, 2], 7));
    }

    static WorldObject Placed(Vector3? min, Vector3? max, float scale = 1) =>
        new(1, "fixture.wmo", new(100, 50, 200), new(0, 90, 0), scale, 0, BoundsMinimum: min, BoundsMaximum: max);

    [Fact]
    public void CoversTreatsMissingBoundsAsCandidateAndAcceptsSwappedCorners()
    {
        Assert.True(GroundProbe.Covers(Placed(null, null), new(1e6f, 0, -1e6f)));
        Assert.True(GroundProbe.Covers(Placed(new(90, 0, 190), null), new(1e6f, 0, 0)));
        var swapped = Placed(new(110, 70, 210), new(90, 40, 190));
        Assert.True(GroundProbe.Covers(swapped, new(104, 0, 197)));
        Assert.True(GroundProbe.Covers(swapped, new(90, 0, 190)));
        Assert.True(GroundProbe.Covers(swapped, new(110, 0, 210)));
        Assert.False(GroundProbe.Covers(swapped, new(89.9f, 0, 200)));
        Assert.False(GroundProbe.Covers(swapped, new(100, 0, 210.1f)));
    }

    static ModelSurface Quad(float y, float half, uint? liquid = null) => new ModelSurface(
        [new(-half, y, -half), new(half, y, -half), new(half, y, half), new(-half, y, half)],
        [Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY],
        [Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero],
        [0, 1, 2, 0, 2, 3],
        "",
        0,
        false) with
    { LiquidType = liquid };

    [Fact]
    public void WmoHeightsReturnsEveryStackedFloorSoCallersCanPickAStep()
    {
        var data = new ModelData([Quad(5, 10), Quad(25, 10)], [], 0, [], []);
        var heights = GroundProbe.WmoHeights(Placed(null, null), data, new(104, 0, 197));
        Assert.Equal([55f, 75f], heights.Order().Select(h => MathF.Round(h, 3)));
        Assert.Equal(55f, MathF.Round(GroundProbe.Choose(heights, 52)!.Value, 3));
        Assert.Equal(75f, MathF.Round(GroundProbe.Choose(heights, 72)!.Value, 3));
    }

    [Fact]
    public void WmoHeightsOnASharedTriangleEdgeReportBothHitsAndChooseIsUnaffected()
    {
        // The quad's diagonal is shared by both triangles, so a probe on it crosses each one.
        var data = new ModelData([Quad(5, 10)], [], 0, [], []);
        var heights = GroundProbe.WmoHeights(Placed(null, null), data, new(100, 0, 200));
        Assert.Equal(2, heights.Count);
        Assert.All(heights, h => Assert.Equal(55f, MathF.Round(h, 3)));
        Assert.Equal(55f, MathF.Round(GroundProbe.Choose(heights, 52)!.Value, 3));
    }

    [Fact]
    public void WmoHeightsScalesWithPlacementScale()
    {
        var data = new ModelData([Quad(5, 10)], [], 0, [], []);
        var heights = GroundProbe.WmoHeights(Placed(null, null, 2), data, new(104, 0, 197));
        Assert.Equal(60f, MathF.Round(Assert.Single(heights), 3));
    }

    [Fact]
    public void WmoHeightsEmptyForLiquidOnlyDegenerateScaleAndNoSurfaces()
    {
        Assert.Empty(GroundProbe.WmoHeights(Placed(null, null), new ModelData([Quad(9, 10, 1)], [], 0, [], []), new(104, 0, 197)));
        Assert.Empty(GroundProbe.WmoHeights(Placed(null, null), new ModelData([], [], 0, [], []), new(104, 0, 197)));
        Assert.Empty(GroundProbe.WmoHeights(Placed(null, null, 0), new ModelData([Quad(5, 10)], [], 0, [], []), new(104, 0, 197)));
    }

    [Fact]
    public void WmoHeightsIgnoresTrailingPartialTriangleIndices()
    {
        var surface = Quad(5, 10) with { Indices = [0, 1, 2, 0, 2] };
        var heights = GroundProbe.WmoHeights(Placed(null, null), new ModelData([surface], [], 0, [], []), new(104, 0, 197));
        Assert.Single(heights);
    }
}
