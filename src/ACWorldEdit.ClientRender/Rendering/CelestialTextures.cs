using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Loads the client's sun and moon textures for the sky shader. A client that does not have one (or, for retail,
/// a missing listfile) simply has no such body.
/// </summary>
public static class CelestialTextures
{
    public static Texture2D? Load(IClientFiles files, string path)
    {
        try
        {
            if (!files.Exists(path))
            {
                return null;
            }

            var blp = BlpTexture.Decode(files.Read(path), 512);
            using var image = Image.CreateFromData(blp.Width, blp.Height, false, Image.Format.Rgba8, blp.Rgba);
            image.GenerateMipmaps();
            return ImageTexture.CreateFromImage(image);
        }
        catch (Exception e) when (e is InvalidDataException or FileNotFoundException or IOException)
        {
            return null;
        }
    }
}
