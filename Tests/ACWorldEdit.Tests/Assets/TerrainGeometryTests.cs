using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>Shared renderer/picker geometry: topology, hole interior, both hole-boundary sides and fully holed chunks.</summary>
public class TerrainGeometryTests
{
    static double? Pick(TerrainGeometry geometry, Point3 origin)
    {
        Point3 P(int index)
        {
            var v = geometry.Vertices[index];
            return new(v.X, v.Y, v.Z);
        }

        double? nearest = null;
        for (int i = 0; i < geometry.Indices.Length; i += 3)
        {
            var hit = WorldCoordinates.Hit(origin, new(0, -1, 0), P(geometry.Indices[i]), P(geometry.Indices[i + 1]), P(geometry.Indices[i + 2]));
            if (hit.HasValue && (!nearest.HasValue || hit < nearest))
            {
                nearest = hit;
            }
        }

        return nearest;
    }

    [Fact]
    public void FlatChunkTopologyAndPick()
    {
        var complete = TerrainGeometry.Build(new TerrainChunk { Height = 10 });
        Assert.True(complete.Indices.Length == 768 && complete.Vertices.Length == 145, "Shared geometry topology");
        Assert.True(Pick(complete, new(1, 20, 1)) == 10, "Flat terrain elevation hit");
    }

    [Fact]
    public void HoleMaskRemovesTrianglesAndPicks()
    {
        var flat = new TerrainChunk { Height = 10, Holes = 1 };
        var holed = TerrainGeometry.Build(flat);
        Assert.True(holed.Indices.Length == 720, "Hole mask topology");
        Assert.True(Pick(holed, new(1, 20, 1)) == null, "Picked terrain through hole");
        double holeEdge = TerrainTile.ChunkSize / 4;
        Assert.True(Pick(holed, new(holeEdge - 0.001, 20, 1)) == null && Pick(holed, new(holeEdge + 0.001, 20, 1)) == 10, "Hole boundary picking");
    }

    [Fact]
    public void FullyHoledChunkEmitsNothing()
    {
        var flat = new TerrainChunk { Height = 10, Holes = 0xFFFF };
        Assert.True(TerrainGeometry.Build(flat).Indices.Length == 0, "Fully holed terrain emitted triangles");
    }
}
