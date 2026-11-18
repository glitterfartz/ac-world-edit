using ACWorldEdit.Foundation.Scripting;
using Xunit;

/// <summary>Template graphs versus committed expected Lua, copied to the test output directory.</summary>
public class ScriptCompilerGoldenTests
{
    static readonly ScriptCompiler Compiler = new(AcScriptHosts.Profile);

    [Theory]
    [InlineData("creature-death")]
    [InlineData("command")]
    [InlineData("dungeon-encounter")]
    [InlineData("quest-giver")]
    [InlineData("spell-cast")]
    [InlineData("world-event")]
    public void TemplateMatchesGoldenLua(string template)
    {
        var document = ScriptTemplateLibrary.Load(template);
        var result = Compiler.Compile(document, template + ".acwscript");

        Assert.True(result.Success);
        string expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripting", "Golden", template + ".lua"));
        Assert.Equal(expected, result.LuaText);
    }
}
