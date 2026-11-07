using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Assets;

/// <summary>
/// Retail WMO dynamic lights (MNLD, Shadowlands 9.1+): 184-byte entries with type, color,
/// position, attenuation and spot cone data. Synthetic roots keep the layout honest without
/// a client install; real-file counts ride on the corpus tests.
/// </summary>
public class WmoLightTests
{
    static byte[] Chunk(string tag, byte[] payload)
    {
        // On-disk tags are reversed: "MNLD" sits as DNLM.
        var header = new byte[8];
        header[0] = (byte)tag[3]; header[1] = (byte)tag[2]; header[2] = (byte)tag[1]; header[3] = (byte)tag[0];
        BitConverter.GetBytes(payload.Length).CopyTo(header, 4);
        return [.. header, .. payload];
    }

    static byte[] Root(params byte[][] chunks) => chunks.SelectMany(c => c).ToArray();

    static byte[] Entry(int type, float x, float y, float z, byte r, byte g, byte b, float attenStart, float attenEnd, float intensity, int set = 0, float rotX = 0, float rotY = 0, float rotZ = 0, float inner = 0, float outer = 0, float falloff = 0)
    {
        var e = new byte[184];
        BitConverter.GetBytes(type).CopyTo(e, 0);
        BitConverter.GetBytes(set).CopyTo(e, 12);
        e[16] = b; e[17] = g; e[18] = r; e[19] = 255;
        BitConverter.GetBytes(x).CopyTo(e, 20);
        BitConverter.GetBytes(y).CopyTo(e, 24);
        BitConverter.GetBytes(z).CopyTo(e, 28);
        BitConverter.GetBytes(rotX).CopyTo(e, 32);
        BitConverter.GetBytes(rotY).CopyTo(e, 36);
        BitConverter.GetBytes(rotZ).CopyTo(e, 40);
        BitConverter.GetBytes(attenStart).CopyTo(e, 44);
        BitConverter.GetBytes(attenEnd).CopyTo(e, 48);
        BitConverter.GetBytes(intensity).CopyTo(e, 52);
        BitConverter.GetBytes(inner).CopyTo(e, 128);
        BitConverter.GetBytes(outer).CopyTo(e, 132);
        BitConverter.GetBytes(falloff).CopyTo(e, 124);
        return e;
    }

    static byte[] RootWith(params byte[][] entries)
    {
        var mver = BitConverter.GetBytes(17);
        var mohd = new byte[64];
        var mnld = entries.SelectMany(e => e).ToArray();
        return Root(Chunk("MVER", mver), Chunk("MOHD", mohd), Chunk("MOMT", []), Chunk("MNLD", mnld));
    }

    sealed class FakeFiles(Dictionary<string, byte[]> contents) : IClientFiles
    {
        public ClientKind Kind => ClientKind.Retail;
        public string Build => "0.0.0.0";
        public byte[] Read(string path) => contents.TryGetValue(path, out var bytes) ? bytes : throw new FileNotFoundException(path);
        public bool Exists(string path) => contents.ContainsKey(path);
        public string Source(string path) => Exists(path) ? "fake" : "missing";
        public void Dispose() { }
    }

    [Fact]
    public void OmniAndSpotEntriesParseWithViewerSpacePositions()
    {
        var files = new FakeFiles(new() { ["hall.wmo"] = RootWith(
            Entry(0, 1, 2, 3, 255, 255, 0, 2, 10, 1.5f),
            Entry(1, 0, 0, 0, 0, 0, 255, 0, 5, 1, rotX: 1.2f, inner: 0.5f, outer: 1, falloff: 4)) });
        var model = WmoModel.Read(files, "hall.wmo");
        Assert.Equal(2, model.Lights!.Length);
        var omni = model.Lights[0];
        Assert.Equal(0, omni.Type);
        // BinaryAsset.View maps (x, y, z) to (-y, z, -x).
        Assert.Equal(new System.Numerics.Vector3(-2, 3, -1), omni.Position);
        Assert.Equal(new System.Numerics.Vector3(1, 1, 0), omni.Color);
        Assert.Equal(2f, omni.AttenStart);
        Assert.Equal(10f, omni.AttenEnd);
        Assert.Equal(1.5f, omni.Intensity);
        var spot = model.Lights[1];
        Assert.Equal(1, spot.Type);
        Assert.True(Math.Abs(spot.Direction.Length() - 1) < 0.001, $"spot direction not unit: {spot.Direction}");
        Assert.Equal(0.5f, spot.InnerAngle);
        Assert.Equal(1f, spot.OuterAngle);
        Assert.Equal(4f, spot.Falloff);
    }

    [Fact]
    public void MissingChunkYieldsNoLights()
    {
        var mver = BitConverter.GetBytes(17);
        var files = new FakeFiles(new() { ["bare.wmo"] = Root(Chunk("MVER", mver), Chunk("MOHD", new byte[64]), Chunk("MOMT", [])) });
        var model = WmoModel.Read(files, "bare.wmo");
        Assert.NotNull(model.Lights);
        Assert.Empty(model.Lights);
        Assert.DoesNotContain(model.Warnings, w => w.Contains("MNLD"));
    }

    [Fact]
    public void TruncatedChunkWarnsAndYieldsNothing()
    {
        var files = new FakeFiles(new() { ["cut.wmo"] = RootWith(new byte[100]) });
        var model = WmoModel.Read(files, "cut.wmo");
        Assert.Empty(model.Lights!);
        Assert.Contains(model.Warnings, w => w.Contains("MNLD"));
    }

    [Fact]
    public void UnknownLightTypeIsSkipped()
    {
        var files = new FakeFiles(new() { ["odd.wmo"] = RootWith(Entry(7, 1, 2, 3, 1, 2, 3, 0, 9, 1)) });
        Assert.Empty(WmoModel.Read(files, "odd.wmo").Lights!);
    }

    [Fact]
    public void PointLightsFeedTerrainAndModelShaders()
    {
        static string Shader(string name)
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "Shaders", name)))
            {
                dir = Path.GetDirectoryName(dir)!;
            }

            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir, "Shaders", name));
        }

        string inc = Shader("shared/client_lighting.gdshaderinc");
        // Four texels per light (see ClientShaderGlobals.PointLightTexels): position/end,
        // color*intensity/start, spot direction/type, cone cosines.
        Assert.Contains("texelFetch(client_point_lights, ivec2(at, 0), 0)", inc);
        Assert.Contains("texelFetch(client_point_lights, ivec2(at + 3, 0), 0)", inc);
        // The term lives in the shared include (definition + terrain path) and at the
        // model call site (world position is only in scope there, like fog's inv_view);
        // the wrath path (no MNLD data) and the fog volumes do not.
        Assert.Equal(2, Count(inc, "client_point_light("));
        Assert.Contains("client_point_light(client_world_pos(VERTEX, INV_VIEW_MATRIX)", Shader("model/model.gdshader"));
        Assert.DoesNotContain("client_point_light", Shader("terrain/terrainlow.gdshader").Replace("client_surface_light", ""));
    }

    static int Count(string text, string value)
    {
        int count = 0;
        for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
