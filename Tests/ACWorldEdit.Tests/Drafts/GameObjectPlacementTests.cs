using ACWorldEdit.Foundation;
using Xunit;

/// <summary>
/// GameObject placement maths: turning keeps the tilt, an object without a stored rotation turns about
/// Z, and headings wrap. Saving and exporting a placement is covered by SpawnPosesTests.
/// </summary>
public class GameObjectPlacementTests
{
    static readonly GameObjectPlacement Baseline = new(new WorldPose(0, 100, 200, 30, 1), 0, 0, Math.Sin(0.5), Math.Cos(0.5));

    [Fact]
    public void TurningKeepsPitchAndRollAndOnlyChangesTheHeading()
    {
        // A tilted object: 30 degrees about X, facing heading 1.
        double tilt = Math.PI / 6;
        var tilted = new GameObjectPlacement(new WorldPose(0, 0, 0, 0, 1), Math.Sin(tilt / 2), 0, 0, Math.Cos(tilt / 2));
        var turned = tilted.WithHeading(1 + Math.PI / 2);
        Assert.Equal(1 + Math.PI / 2, turned.Pose.Heading, 9);
        // The product stays a unit quaternion and its tilt component is unchanged in magnitude.
        var (x, y, z, w) = turned.FacingQuaternion();
        Assert.Equal(1, x * x + y * y + z * z + w * w, 9);
        Assert.Equal(Math.Sin(tilt / 2), Math.Sqrt(x * x + y * y), 9);
    }

    [Fact]
    public void AnObjectWithoutAStoredRotationGetsAPlainTurnAboutZ()
    {
        var bare = new GameObjectPlacement(new WorldPose(0, 0, 0, 0, 0.5), 0, 0, 0, 0);
        Assert.False(bare.HasRotation);
        var (x0, y0, z0, w0) = bare.FacingQuaternion();
        Assert.Equal((0, 0), (x0, y0));
        Assert.Equal(Math.Sin(0.25), z0, 9);
        var turned = bare.WithHeading(2);
        Assert.Equal(Math.Sin(1), turned.RotZ, 9);
        Assert.Equal(Math.Cos(1), turned.RotW, 9);
        Assert.Equal(0, turned.RotX, 9);
        Assert.Equal(0, turned.RotY, 9);
    }

    [Fact]
    public void HeadingsWrapIntoARange()
    {
        var turned = Baseline.WithHeading(-0.5);
        Assert.InRange(turned.Pose.Heading, 0, 2 * Math.PI);
    }
}
