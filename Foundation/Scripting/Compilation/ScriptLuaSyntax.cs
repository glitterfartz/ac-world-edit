namespace ACWorldEdit.Foundation.Scripting;

/// <summary>
/// A small Lua 5.1 compatibility scan over generated text. It walks the source outside strings and
/// comments so a user's message containing "//" is not a false positive, and reports the syntax the
/// emitter must never produce: goto, integer division, bitwise operators, labels and 5.2 library moves.
/// </summary>
public static class ScriptLuaSyntax
{
    static readonly string[] ForbiddenWords = ["goto", "rawlen", "table.unpack", "math.type"];
    static readonly string[] ForbiddenOperators = ["//", "<<", ">>", "::"];

    public static IReadOnlyList<string> FindForbidden(string lua)
    {
        var found = new List<string>();
        int line = 1;
        for (int index = 0; index < lua.Length; index++)
        {
            char character = lua[index];
            if (character == '\n')
            {
                line++;
                continue;
            }

            if (character == '-' && index + 1 < lua.Length && lua[index + 1] == '-')
            {
                while (index < lua.Length && lua[index] != '\n')
                {
                    index++;
                }

                line++;
                continue;
            }

            if (character is '"' or '\'')
            {
                char quote = character;
                index++;
                while (index < lua.Length && lua[index] != quote)
                {
                    if (lua[index] == '\\')
                    {
                        index++;
                    }
                    else if (lua[index] == '\n')
                    {
                        line++;
                    }

                    index++;
                }

                continue;
            }

            if (character == '[' && index + 1 < lua.Length && lua[index + 1] == '[')
            {
                index += 2;
                while (index + 1 < lua.Length && !(lua[index] == ']' && lua[index + 1] == ']'))
                {
                    if (lua[index] == '\n')
                    {
                        line++;
                    }

                    index++;
                }

                index++;
                continue;
            }

            foreach (string token in ForbiddenOperators)
            {
                if (string.CompareOrdinal(lua, index, token, 0, token.Length) == 0)
                {
                    found.Add($"{token} at line {line}");
                }
            }

            if (character is '&' or '|')
            {
                found.Add($"{character} at line {line}");
            }

            foreach (string word in ForbiddenWords)
            {
                if (!lua.AsSpan(index).StartsWith(word, StringComparison.Ordinal))
                {
                    continue;
                }

                bool startsWord = index == 0 || !IsIdentifier(lua[index - 1]);
                bool endsWord = index + word.Length >= lua.Length || !IsIdentifier(lua[index + word.Length]);
                if (startsWord && endsWord)
                {
                    found.Add($"{word} at line {line}");
                }
            }
        }

        return found;
    }

    static bool IsIdentifier(char character) => char.IsAsciiLetterOrDigit(character) || character is '_' or '.';
}
