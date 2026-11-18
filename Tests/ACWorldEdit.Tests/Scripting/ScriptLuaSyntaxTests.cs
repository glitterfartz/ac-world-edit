using ACWorldEdit.Foundation.Scripting;
using Xunit;

/// <summary>The generated text stays inside Lua 5.1 and the scanner ignores strings and comments.</summary>
public class ScriptLuaSyntaxTests
{
    static readonly ScriptCompiler Compiler = new(AcScriptHosts.Profile);

    [Theory]
    [InlineData("blank")]
    [InlineData("command")]
    [InlineData("creature-death")]
    [InlineData("dungeon-encounter")]
    [InlineData("quest-giver")]
    [InlineData("spell-cast")]
    [InlineData("world-event")]
    public void TemplatesEmitOnlyLua51Syntax(string template)
    {
        var result = Compiler.Compile(ScriptTemplateLibrary.Load(template), template + ".acwscript");

        Assert.True(result.Success);
        Assert.Empty(ScriptLuaSyntax.FindForbidden(result.LuaText!));
    }

    [Fact]
    public void TheScannerFindsNewerSyntax()
    {
        var found = ScriptLuaSyntax.FindForbidden("local x = a // b\ngoto done\nlocal y = a & b\n");

        Assert.Contains(found, item => item.StartsWith("//", StringComparison.Ordinal));
        Assert.Contains(found, item => item.StartsWith("goto", StringComparison.Ordinal));
        Assert.Contains(found, item => item.StartsWith("&", StringComparison.Ordinal));
    }

    [Fact]
    public void StringsAndCommentsAreNotFlagged()
    {
        var found = ScriptLuaSyntax.FindForbidden(
            "-- goto here\nprint(\"http://example.com/a&b\")\nprint('// not code')\n");

        Assert.Empty(found);
    }
}
