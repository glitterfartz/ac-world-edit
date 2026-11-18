using System.Text;

namespace ACWorldEdit.Foundation.Scripting;

/// <summary>Indentation and newlines for generated Lua; always LF, deterministically.</summary>
public sealed class LuaWriter
{
    readonly StringBuilder builder = new();
    int indent;

    public void Line(string text = "")
    {
        if (text.Length > 0)
        {
            builder.Append(' ', indent * 4);
            builder.Append(text);
        }

        builder.Append('\n');
    }

    public void Indent() => indent++;

    public void Outdent() => indent = Math.Max(0, indent - 1);

    public override string ToString() => builder.ToString();
}
