using System.Text;

namespace ACWorldEdit.Foundation.Scripting;

/// <summary>Filename-safe derivations shared by the compiler and the workspace.</summary>
public static class ScriptSlug
{
    public static string FromName(string name)
    {
        var builder = new StringBuilder();
        bool dash = false;
        foreach (char character in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
                dash = false;
            }
            else if (!dash && builder.Length > 0)
            {
                builder.Append('-');
                dash = true;
            }
        }

        string text = builder.ToString().Trim('-');
        return text.Length == 0 ? "script" : text[..Math.Min(48, text.Length)].Trim('-');
    }

    public static string ShortId(string id)
    {
        string text = new(id.Where(char.IsAsciiLetterOrDigit).ToArray());
        text = text.ToLowerInvariant();
        return text.Length == 0 ? "doc" : text[..Math.Min(8, text.Length)];
    }
}
