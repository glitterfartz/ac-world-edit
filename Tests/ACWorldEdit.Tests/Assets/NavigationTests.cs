using System.Numerics;
using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

public sealed class NavigationTests
{
    static byte[] Tile(bool ac)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream);
        int header = ac ? 56 : 20;
        w.Write(DetourReader.Magic); w.Write(7); w.Write(ac ? 20 : 16); w.Write(0); w.Write(1);
        if(ac) { w.Write(55f); w.Write(new byte[]{2,6,3,0}); w.Write(2000); w.Write(80); w.Write(25); w.Write(.25f); w.Write(.25f); w.Write(.25f); w.Write(1.3f); }
        w.Write(0x444e4156); w.Write(7); w.Write(5); w.Write(7); w.Write(0); w.Write(42);
        foreach(int n in new[]{2,5,2,1,1,1,1,1,1}) w.Write(n);
        foreach(float f in new[]{1.5f,.5f,.75f,0,0,0,10,10,10,1}) w.Write(f);
        foreach(var v in new[]{new Vector3(0,0,0),new Vector3(10,0,0),new Vector3(0,0,10),new Vector3(1,1,1),new Vector3(2,2,2)}) Write(w,v);
        Poly(w,new ushort[]{0,1,2},(byte)(ac?1:11),0); Poly(w,new ushort[]{3,4},(byte)(ac?8:9),1);
        w.Write(new byte[32]);
        w.Write(0); w.Write(0); w.Write((byte)1); w.Write((byte)1); w.Write((ushort)0);
        Write(w,new(3,4,5)); w.Write(new byte[]{0,1,3,5});
        for(int i=0;i<3;i++) w.Write((ushort)0); for(int i=0;i<3;i++) w.Write((ushort)10); w.Write(0);
        Write(w,new(1,1,1)); Write(w,new(2,2,2)); w.Write(.5f); w.Write((ushort)1); w.Write((byte)1); w.Write((byte)255); w.Write(123u);
        w.Flush(); byte[] bytes=stream.ToArray(); BitConverter.GetBytes(bytes.Length-header).CopyTo(bytes,12); return bytes;
    }
    static void Write(BinaryWriter w,Vector3 v) { w.Write(v.X);w.Write(v.Y);w.Write(v.Z); }
    static void Poly(BinaryWriter w,ushort[] indices,byte area,byte type)
    {
        w.Write(uint.MaxValue); for(int i=0;i<6;i++) w.Write(i<indices.Length?indices[i]:(ushort)0);
        for(int i=0;i<6;i++) w.Write((ushort)(i==0?0x8001:0));
        w.Write((ushort)0x1234);w.Write((byte)indices.Length);w.Write((byte)(area|(type<<6)));
    }
    static INavigationReader Reader(bool ac) => ac ? new AcNavigationReader() : new TcNavigationReader();
    [Theory] [InlineData(true)] [InlineData(false)]
    public void ReadsWrapperDetailAndConnections(bool ac)
    {
        var tile=Reader(ac).ReadTile(Tile(ac),42,1,"sample",32,33);
        Assert.Equal(42,tile.RequestedMap); Assert.Equal(1,tile.SourceMap); Assert.Equal(32,tile.FileX); Assert.Equal(5,tile.GridX);
        var tri=Assert.Single(tile.Triangles); Assert.Equal(new Vector3(3,4,5),tri.C); Assert.Equal((byte)5,tri.EdgeFlags);
        Assert.Equal((ushort)0x8001,tile.Polygons[0].Neighbors[0]); Assert.Equal((ushort)0x1234,tile.Polygons[0].Flags);
        Assert.Equal(123u,Assert.Single(tile.Connections).UserId); Assert.Equal(ac,tile.Settings != null);
        Assert.Equal(new Vector3(-3,4,-5),NavigationTile.ToView(tri.C));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void RejectsMalformedData(bool ac)
    {
        int h=ac?56:20;
        foreach(var mutation in new Action<byte[]>[]{
            b=>BitConverter.GetBytes(99).CopyTo(b,8), b=>BitConverter.GetBytes(1).CopyTo(b,12),
            b=>BitConverter.GetBytes(-1).CopyTo(b,h+24), b=>BitConverter.GetBytes(float.NaN).CopyTo(b,h+100),
            b=>b[h+100+60+30]=7, b=>b[h+100+60+64+32+12+12+2]=99, b=>BitConverter.GetBytes(2).CopyTo(b,h+296)})
        {
            var bytes=Tile(ac);mutation(bytes);Assert.Throws<InvalidDataException>(()=>Reader(ac).ReadTile(bytes,0,0,"bad",0,0));
        }
        Assert.Throws<InvalidDataException>(()=>Reader(ac).ReadTile(Tile(ac)[..^1],0,0,"bad",0,0));
    }
    [Fact]
    public void TerrainMeaningsAreServerSpecific()
    {
        Assert.Equal("Water",new AcNavigationReader().AreaName(8)); Assert.Equal("Magma / slime",new TcNavigationReader().AreaName(8));
        Assert.Equal("Ground",new TcNavigationReader().AreaName(11)); Assert.Contains("37",new AcNavigationReader().AreaName(37));
    }
    [Fact]
    public void TrinityChildPrecedenceAndImmediateParentOnly()
    {
        string directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var reader=new TcNavigationReader();var source=new NavigationSource(directory,reader,new Dictionary<int,int>{{2,1},{1,0}});
            File.WriteAllBytes(Path.Combine(directory,reader.TileName(0,32,33)),Tile(false));
            Assert.Throws<FileNotFoundException>(()=>source.Read(2,32,33));
            File.WriteAllBytes(Path.Combine(directory,reader.TileName(1,32,33)),Tile(false));
            Assert.Equal(1,source.Read(2,32,33).SourceMap);
            File.WriteAllBytes(Path.Combine(directory,reader.TileName(2,32,33)),Tile(false));
            Assert.Equal(2,source.Read(2,32,33).SourceMap);
            File.WriteAllBytes(Path.Combine(directory,reader.TileName(2,32,33)),new byte[20]);
            Assert.Throws<InvalidDataException>(()=>source.Read(2,32,33));
        }
        finally { Directory.Delete(directory,true); }
    }
    [Fact]
    public void TrinityMapLevelConnections()
    {
        using var stream=new MemoryStream();using var w=new BinaryWriter(stream);
        w.Write(DetourReader.Magic);w.Write(16);Write(w,new(1,2,3));w.Write(533.33f);w.Write(533.33f);w.Write(1024);w.Write(0x80000000u);w.Write(1);
        w.Write(42u);w.Write(32u);w.Write(33u);Write(w,new(4,5,6));Write(w,new(7,8,9));w.Write(.5f);w.Write((byte)1);w.Write((byte)11);w.Write((ushort)1);
        var bytes=stream.ToArray();var parameters=new TcNavigationReader().ReadParameters(bytes);var c=Assert.Single(parameters.Connections);
        Assert.Equal(0x80000000u,parameters.MaxPolygons);Assert.Equal(42u,c.Map);Assert.Equal(new Vector3(4,5,6),c.From);Assert.Equal((ushort)1,c.TerrainFlags);
        Assert.Throws<InvalidDataException>(()=>new TcNavigationReader().ReadParameters(bytes[..^1]));
        BitConverter.GetBytes(float.PositiveInfinity).CopyTo(bytes,8);Assert.Throws<InvalidDataException>(()=>new TcNavigationReader().ReadParameters(bytes));
    }
    [Theory] [InlineData(true)] [InlineData(false)] [Trait("Category","Corpus")]
    public void SuppliedCorpus(bool ac)
    {
        string? directory=Environment.GetEnvironmentVariable(ac?"ACWORLD_AC_MMAP_DIR":"ACWORLD_TC_MMAP_DIR");
        if(string.IsNullOrEmpty(directory)) return;
        var reader=Reader(ac);
        foreach(string file in Directory.EnumerateFiles(directory,"*.mmtile"))
        {
            var tile=reader.ReadTile(File.ReadAllBytes(file),0,0,file,0,0); Assert.NotEmpty(tile.Polygons);
        }
        foreach(string file in Directory.EnumerateFiles(directory,"*.mmap")) reader.ReadParameters(File.ReadAllBytes(file));
    }
}
