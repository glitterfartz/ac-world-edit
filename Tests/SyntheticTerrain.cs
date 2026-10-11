using System.Text;
namespace ACWorldEdit.Assets;

public static class SyntheticTerrain
{
    public static byte[] Tile(bool duplicateChunk = false)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        void Chunk(string tag, byte[] bytes)
        { writer.Write(Encoding.ASCII.GetBytes(new string(tag.Reverse().ToArray()))); writer.Write(bytes.Length); writer.Write(bytes); }
        Chunk("MVER", BitConverter.GetBytes(18)); Chunk("MTEX", Encoding.UTF8.GetBytes("fixture.blp\0"));
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
        {
            using var chunk = new MemoryStream(); using var fields = new BinaryWriter(chunk);
            byte[] header = new byte[128];
            BitConverter.GetBytes(duplicateChunk ? 0 : x).CopyTo(header, 4);
            BitConverter.GetBytes(duplicateChunk ? 0 : y).CopyTo(header, 8);
            BitConverter.GetBytes(136).CopyTo(header, 20); // MCVT relative to MCNK header start.
            BitConverter.GetBytes(724).CopyTo(header, 28); // MCLY follows height header/data.
            BitConverter.GetBytes(10f).CopyTo(header, 112); fields.Write(header);
            fields.Write(Encoding.ASCII.GetBytes("TVCM")); fields.Write(580); fields.Write(new byte[580]);
            fields.Write(Encoding.ASCII.GetBytes("YLCM")); fields.Write(16); fields.Write(new byte[16]);
            Chunk("MCNK", chunk.ToArray());
        }
        return stream.ToArray();
    }
}
