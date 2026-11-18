using System.Security.Cryptography;
using System.Text;

namespace ACWorldEdit.Data.Scripting;

/// <summary>Hashing and crash-safe writes shared by the workspace, manifest and exporter.</summary>
static class ScriptFileIo
{
    static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static string HashBytes(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string HashText(string text) => HashBytes(Utf8.GetBytes(text));

    public static string HashFile(string path) => HashBytes(File.ReadAllBytes(path));

    /// <summary>Write a sibling temporary file, flush it to disk, then replace the target.</summary>
    public static void WriteAtomic(string path, string text)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(Utf8.GetBytes(text));
            stream.Flush(true);
        }

        File.Move(temp, path, true);
    }
}
