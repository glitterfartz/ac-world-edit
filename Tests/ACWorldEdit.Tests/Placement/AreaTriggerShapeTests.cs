using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Placement;

/// <summary>
/// The server membership test of every AreaTrigger shape (Player::IsInAreaTrigger for client triggers,
/// AreaTrigger::SearchUnitIn* for TrinityCore spawns), and how a record names itself and its rules.
/// </summary>
public class AreaTriggerShapeTests
{
    static AreaTriggerRecord At(AreaTriggerShape shape, double radius = 0, double length = 0, double width = 0, double height = 0, double orientation = 0) =>
        new(1, 0, 100, 200, 50, radius, length, width, height, orientation) { Volume = shape };

    static Point3 P(double dx, double dy, double dz = 0) => new(100 + dx, 200 + dy, 50 + dz);

    [Fact]
    public void RowsWithoutAShapeAreSpheresOrBoxesByRadius()
    {
        Assert.Equal(AreaTriggerShape.Sphere, new AreaTriggerRecord(1, 0, 0, 0, 0, 5, 0, 0, 0, 0).Volume);
        Assert.Equal(AreaTriggerShape.Box, new AreaTriggerRecord(1, 0, 0, 0, 0, 0, 4, 4, 4, 0).Volume);
    }

    [Fact]
    public void SpheresMeasureInThreeDimensions()
    {
        var sphere = At(AreaTriggerShape.Sphere, radius: 5);
        Assert.True(sphere.Contains(P(3, 0, 3.9)));
        Assert.False(sphere.Contains(P(3, 0, 4.1)));
        Assert.True(sphere.Contains(P(5.5, 0), 1));
    }

    [Fact]
    public void BoxesTurnWithTheirOrientation()
    {
        // 10 long along the facing, 2 wide; facing +Y (π/2) makes the length run north-south.
        var box = At(AreaTriggerShape.Box, length: 10, width: 2, height: 4, orientation: Math.PI / 2);
        Assert.True(box.Contains(P(0, 4.5)));
        Assert.False(box.Contains(P(4.5, 0)));
        Assert.False(box.Contains(P(0, 0, 2.5)));
    }

    [Fact]
    public void ClientPolygonsIgnoreOrientationAndHaveNoFloor()
    {
        // An L shape: the notch at (+x, +y) is outside.
        (double, double)[] corners = [(-5, -5), (5, -5), (5, 0), (0, 0), (0, 5), (-5, 5)];
        var polygon = At(AreaTriggerShape.Polygon, orientation: 1) with { Vertices = corners, Bottom = double.NegativeInfinity, Top = 3 };
        Assert.True(polygon.Contains(P(2, -2)));
        Assert.True(polygon.Contains(P(-2, 2)));
        Assert.False(polygon.Contains(P(2, 2)));
        Assert.False(polygon.Contains(P(6, 0)));
        Assert.True(polygon.Contains(P(-2, -2, -500)));
        Assert.False(polygon.Contains(P(-2, -2, 3.5)));
        Assert.Contains("no floor", polygon.Shape);
        Assert.Equal(Math.Sqrt(50), polygon.Reach, 6);
    }

    [Fact]
    public void APolygonIsWhereItsCornersAre()
    {
        // Many client polygons sit at the map origin with world-space corners.
        (double, double)[] corners = [(1000, 2000), (1010, 2000), (1010, 2010), (1000, 2010)];
        var polygon = new AreaTriggerRecord(9, 1136, 0, 0, 0, 0, 0, 0, 0, 0) { Volume = AreaTriggerShape.Polygon, Vertices = corners, Bottom = double.NegativeInfinity, Top = double.PositiveInfinity, Ground = -317 };
        Assert.Equal(new Point3(1005, 2005, -317), polygon.Focus);
        Assert.True(polygon.Contains(new(1005, 2005, -317)));
        // Ground is an offset from Z, as the corners are.
        Assert.Equal(-307, (polygon with { Z = 10 }).Focus.Z);
        Assert.Equal(new Point3(100, 200, 50), At(AreaTriggerShape.Box, length: 2, width: 2, height: 2).Focus);
    }

    [Fact]
    public void CylindersStandOnTheirFloor()
    {
        var cylinder = At(AreaTriggerShape.Cylinder, radius: 4) with { Bottom = 0, Top = 10 };
        Assert.True(cylinder.Contains(P(3, 0, 9)));
        Assert.False(cylinder.Contains(P(3, 0, -0.5)));
        Assert.False(cylinder.Contains(P(3, 3, 1)));
    }

    [Fact]
    public void DisksLeaveTheirInnerRadiusOut()
    {
        var disk = At(AreaTriggerShape.Disk, radius: 10) with { InnerRadius = 4, Bottom = -1, Top = 1 };
        Assert.True(disk.Contains(P(6, 0)));
        Assert.False(disk.Contains(P(2, 0)));
        Assert.False(disk.Contains(P(11, 0)));
        Assert.False(disk.Contains(P(6, 0, 2)));
    }

    [Fact]
    public void BoundedPlanesCatchUnitsWithinReach()
    {
        // A 6 × 4 wall across a facing of 0 (+X): the wall spans Y and Z.
        var plane = At(AreaTriggerShape.BoundedPlane, width: 6, height: 4);
        Assert.True(plane.Contains(P(1, 2)));
        Assert.False(plane.Contains(P(2, 0)));
        Assert.True(plane.Contains(P(0, 4)));
        Assert.False(plane.Contains(P(0, 5)));
    }

    [Fact]
    public void SpawnsKeyByTheirSpawnAndRunTemplateRules()
    {
        var client = new AreaTriggerRecord(1125, 0, 0, 0, 0, 5, 0, 0, 0, 0, AreaTriggerRecord.SmartTrigger);
        var spawn = new AreaTriggerRecord(188, 0, 0, 0, 0, 5, 0, 0, 0, 0, AreaTriggerRecord.SmartEntity) { SpawnId = 290, IsCustom = true };
        Assert.Equal(new AreaTriggerKey(false, 1125), client.Key);
        Assert.Equal(new AreaTriggerKey(true, 290), spawn.Key);
        Assert.Equal(new AreaTriggerOwner(1125), client.Owner);
        Assert.Equal(new AreaTriggerOwner(188, SmartScope.AreaTriggerEntityCustom, 290), spawn.Owner);
        Assert.Equal(SmartScope.AreaTriggerEntity, (spawn with { IsCustom = false }).Owner.SourceType);
        Assert.Equal(AreaTriggerKind.SmartAI, client.Kind);
        Assert.Equal(AreaTriggerKind.SmartAI, spawn.Kind);
        // Each script name belongs to one kind of trigger.
        Assert.Equal(AreaTriggerKind.Script, (spawn with { ScriptName = AreaTriggerRecord.SmartTrigger }).Kind);
        Assert.Equal(AreaTriggerKind.Script, (client with { ScriptName = AreaTriggerRecord.SmartEntity }).Kind);
    }

    [Fact]
    public void ListIdsKeepClientTriggersAndSpawnsApart()
    {
        foreach (var key in new[] { new AreaTriggerKey(false, 1125), new AreaTriggerKey(true, 1125), new AreaTriggerKey(true, 5_000_000_000) })
        {
            Assert.Equal(key, AreaTriggerKey.FromListId(key.ListId));
        }

        Assert.NotEqual(new AreaTriggerKey(false, 7).ListId, new AreaTriggerKey(true, 7).ListId);
        Assert.Equal(7ul, new AreaTriggerKey(false, 7).ListId);
    }
}
