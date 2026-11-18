using System.Globalization;
using System.Text;

namespace ACWorldEdit.Foundation.Scripting;

/// <summary>Invariant Lua literals and identifiers: numbers, escaped strings, reserved words.</summary>
public static class LuaLiteral
{
    static readonly HashSet<string> Keywords =
    [
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if",
        "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while"
    ];

    public static string Nil => "nil";

    public static string FromValue(object? value) => value switch
    {
        null => Nil,
        bool flag => flag ? "true" : "false",
        sbyte or byte or short or ushort or int or uint or long or ulong => Integer(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        float or double or decimal => Number(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
        string text => String(text),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported literal value.")
    };

    public static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Number(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Lua has no NaN or infinity literal.");
        }

        string text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('e') || text.Contains('E') ? text : text + ".0";
    }

    public static string String(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (char character in value)
        {
            switch (character)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (character < ' ')
                    {
                        builder.Append('\\').Append(((int)character).ToString("D3", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>A valid Lua identifier that is not a reserved word; collisions are the caller's job.</summary>
    public static string Identifier(string name)
    {
        var builder = new StringBuilder(name.Length + 1);
        foreach (char character in name)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_');
        }

        if (builder.Length == 0)
        {
            builder.Append("value");
        }

        if (char.IsAsciiDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        string text = builder.ToString();
        return Keywords.Contains(text) ? "_" + text : text;
    }
}
