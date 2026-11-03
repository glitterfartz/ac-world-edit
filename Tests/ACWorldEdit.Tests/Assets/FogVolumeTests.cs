using System.Numerics;
using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>The retail fog volume reader (a map's _fogs.wdt) on synthetic chunks laid out as the wiki documents VFOG and VFEX.</summary>
public class FogVolumeTests
{
    static byte[] Chunk(string tag, byte[] body)
    {
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        w.Write(tag.Reverse().Select(c => (byte)c).ToArray());
        w.Write((uint)body.Length);
        w.Write(body);
        return m.ToArray();
    }

    /// <summary>A VFOG entry shaped like the Kul Tiras volumes: a tan 1300 by 700 yard box at (2724, 4933, -20).</summary>
    static byte[] Vfog(uint id, uint model = 0x1a5f64)
    {
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        foreach (float f in new[] { 0.5451f, 0.4392f, 0.1922f, 0.007f, 0.8f, 450f, 1f, 2724f, 4933f, -20.14f, 0f, 0f, 0f, 0.02932f, 0.9996f, 1300f, 700f, 2f })
        {
            w.Write(f);
        }

        foreach (int period in new[] { 10000, 20000, 50000, 60000 })
        {
            w.Write(period);
        }

        w.Write(1u);
        w.Write(model);
        w.Write(1u);
        w.Write(id);
        return m.ToArray();
    }

    static byte[] Vfex(uint id)
    {
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        w.Write(1u);
        foreach (float f in new[] { 471.9f, 509.2f, 171.4f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f })
        {
            w.Write(f);
        }

        w.Write(id);
        w.Write(new byte[24]);
        return m.ToArray();
    }

    [Fact]
    public void EntryFieldsLandWhereTheWikiPutsThem()
    {
        var volume = Assert.Single(FogVolumes.Read(Chunk("MVER", [2, 0, 0, 0]).Concat(Chunk("VFOG", Vfog(0x259))).ToArray()));

        Assert.Equal(new Vector3(0.5451f, 0.4392f, 0.1922f), volume.Color);
        Assert.Equal([0.007f, 0.8f, 450f], volume.Intensity);
        Assert.Equal(1f, volume.Unknown18);
        Assert.Equal(new Vector3(2724f, 4933f, -20.14f), volume.Position);
        Assert.Equal(0.9996f, volume.Rotation.W);
        Assert.Equal([1300f, 700f, 2f], volume.Radius);
        Assert.Equal([10000, 20000, 50000, 60000], volume.AnimationPeriods);
        Assert.Equal((1u, 0x1a5f64u, 1u, 0x259u), (volume.Flags, volume.ModelFileDataId, volume.FogLevel, volume.Id));
        Assert.Null(volume.Extra);
    }

    [Fact]
    public void ExtraDataJoinsTheEntryWithTheSameId()
    {
        var data = Chunk("VFOG", Vfog(0x259).Concat(Vfog(0x25a)).ToArray()).Concat(Chunk("VFEX", Vfex(0x25a))).ToArray();

        var volumes = FogVolumes.Read(data);

        Assert.Equal(2, volumes.Count);
        Assert.Null(volumes[0].Extra);
        Assert.Equal([471.9f, 509.2f, 171.4f], volumes[1].Extra);
    }

    [Fact]
    public void DerivedBoxFollowsTheFieldMeaning()
    {
        var volume = Assert.Single(FogVolumes.Read(Chunk("VFOG", Vfog(1))));

        // The box is radius x and y wide and the intensity's third value tall; thickness is scaled to extinction per yard.
        Assert.Equal(new Vector3(1300f, 700f, 450f), volume.Size);
        Assert.Equal(0.007f * MapFogVolume.ThicknessScale, volume.ThicknessPerYard, 6);
        Assert.Equal(2f, volume.EdgePower);
    }

    [Fact]
    public void ATruncatedChunkAndATrailingPartialEntryAreIgnored()
    {
        var whole = Chunk("VFOG", Vfog(1));
        var partial = Chunk("VFOG", Vfog(1).Take(50).ToArray());

        Assert.Single(FogVolumes.Read(whole));
        Assert.Empty(FogVolumes.Read(partial));
        Assert.Empty(FogVolumes.Read(whole.Take(whole.Length - 10).ToArray()));
        Assert.Empty(FogVolumes.Read([]));
    }

    [Fact]
    public void PathNamesTheFogsFileInTheMapDirectory()
    {
        Assert.Equal("World/Maps/azeroth/azeroth_fogs.wdt", FogVolumes.PathFor("azeroth"));
    }
}
