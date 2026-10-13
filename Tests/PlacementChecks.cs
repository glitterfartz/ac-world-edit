using System.Numerics;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

/// <summary>
/// Engine-independent placement checks: heading normalization, terrain height probes over synthetic
/// ADT data (flat, sloped, holed) and WMO floor probes over a synthetic placed quad.
/// </summary>
public static class PlacementChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    static bool Near(double a, double b, double tolerance = 1e-4) => Math.Abs(a - b) <= tolerance;

    public static void Run()
    {
        // Server range is [0, 2π) (Position::NormalizeOrientation).
        Check(WorldCoordinates.NormalizeHeading(0) == 0, "Zero heading changed");
        Check(Near(WorldCoordinates.NormalizeHeading(-Math.PI / 2), 3 * Math.PI / 2), "Negative heading not wrapped");
        Check(WorldCoordinates.NormalizeHeading(2 * Math.PI) == 0, "2π not wrapped to 0");
        Check(Near(WorldCoordinates.NormalizeHeading(7 * Math.PI + 0.25), Math.PI + 0.25), "Large heading not wrapped");
        double tiny = WorldCoordinates.NormalizeHeading(-1e-18);
        Check(tiny >= 0 && tiny < 2 * Math.PI, "Tiny negative heading left half-open range");

        var tile = new TerrainTile(new(32, 32), SyntheticTerrain.Tile(), false);
        var flat = new TerrainHeights(tile);
        Check(flat.Probe(1, 1) is float f && Near(f, 10), "Flat terrain probe");
        Check(flat.Probe(TerrainTile.Size - 0.01f, TerrainTile.Size - 0.01f) is float edge && Near(edge, 10), "Far tile corner probe");
        Check(flat.Probe(-1, 1) == null && flat.Probe(1, TerrainTile.Size + 1) == null, "Probe outside tile returned a height");

        // Slope chunk 0 along X: relative height equals the vertex's column position in yards.
        var sloped = tile.Chunks.First(c => c.X == 0 && c.Y == 0);
        for (int row = 0, index = 0; row < 17; row++)
        {
            for (int column = 0; column < ((row & 1) == 0 ? 9 : 8); column++, index++)
            {
                float x = column + ((row & 1) == 1 ? 0.5f : 0);
                sloped.Heights[index] = x / 8 * TerrainTile.ChunkSize;
            }
        }

        // Holes affect only the hole cell; mark chunk (1,0) cell block 0.
        tile.Chunks.First(c => c.X == 1 && c.Y == 0).Holes = 1;
        var shaped = new TerrainHeights(tile);
        float sampleX = TerrainTile.ChunkSize * 0.3f;
        Check(shaped.Probe(sampleX, 5) is float slope && Near(slope, 10 + sampleX, 1e-3), "Sloped terrain probe");
        Check(shaped.Probe(TerrainTile.ChunkSize + 1, 1) == null, "Probe hit terrain through a hole");
        Check(shaped.Probe(TerrainTile.ChunkSize * 1.9f, TerrainTile.ChunkSize * 0.9f) is float beside && Near(beside, 10), "Hole suppressed neighbouring cells");
        Check(shaped.Bytes < 200_000, "Height cache larger than expected");

        Check(GroundProbe.Choose([10, 14, 30], 12) == 14, "Did not choose highest surface within step-up");
        Check(GroundProbe.Choose([10, 30], 12) == 10, "Chose a ceiling above step-up");
        Check(GroundProbe.Choose([30], 12) == null, "Snapped up to an unreachable surface");
        Check(GroundProbe.Choose([], 12) == null, "Snapped with no surfaces");

        // A 20x20 floor quad at local height 5, placed at (100, 50, 200) with a 90° yaw; the
        // WorldObjects transform adds its own half-turn, which a centered square tolerates.
        var floor = new ModelSurface(
            [new(-10, 5, -10), new(10, 5, -10), new(10, 5, 10), new(-10, 5, 10)],
            [Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY],
            [Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero],
            [0, 1, 2, 0, 2, 3],
            "",
            0,
            false);
        var water = floor with { Positions = [new(-10, 9, -10), new(10, 9, -10), new(10, 9, 10), new(-10, 9, 10)], LiquidType = 1 };
        var data = new ModelData([floor, water], [], 0, [], []);
        var placed = new WorldObject(1, "fixture.wmo", new(100, 50, 200), new(0, 90, 0), 1, 0, BoundsMinimum: new(90, 40, 190), BoundsMaximum: new(110, 70, 210));
        var heights = GroundProbe.WmoHeights(placed, data, new(104, 0, 197));
        Check(heights.Count == 1 && Near(heights[0], 55, 1e-3), "WMO floor probe height or liquid exclusion");
        Check(GroundProbe.WmoHeights(placed, data, new(120, 0, 200)).Count == 0, "WMO probe hit outside its floor");
        Check(GroundProbe.Covers(placed, new(105, 0, 205)) && !GroundProbe.Covers(placed, new(111, 0, 205)), "MODF bounds coverage");
        Console.WriteLine("PLACEMENT_CHECKS_PASSED heading-normalize/terrain flat-slope-hole/step-up choice/WMO floor+liquid");
    }
}
