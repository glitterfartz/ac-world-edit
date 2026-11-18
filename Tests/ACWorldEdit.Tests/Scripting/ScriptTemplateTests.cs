using ACWorldEdit.Foundation.Scripting;
using Xunit;

/// <summary>Every embedded template loads, validates, compiles, and clones with fresh ids.</summary>
public class ScriptTemplateTests
{
    static readonly ScriptCompiler Compiler = new(AcScriptHosts.Profile);

    [Theory]
    [InlineData("blank")]
    [InlineData("creature-death")]
    [InlineData("command")]
    [InlineData("dungeon-encounter")]
    [InlineData("quest-giver")]
    [InlineData("world-event")]
    [InlineData("spell-cast")]
    public void EveryTemplateCompiles(string template)
    {
        var document = ScriptTemplateLibrary.Load(template);
        var result = Compiler.Compile(document, template + ".acwscript");

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));
    }

    [Fact]
    public void CreateRemapsEveryId()
    {
        var template = ScriptTemplateLibrary.Load("creature-death");
        var created = ScriptTemplateLibrary.Create("creature-death");

        Assert.NotEqual(template.Id, created.Id);
        Assert.DoesNotContain(created.Graph.Nodes, node => template.Graph.Nodes.Any(old => old.Id == node.Id));
        var nodeIds = created.Graph.Nodes.Select(node => node.Id).ToHashSet();
        Assert.All(created.Graph.Edges, edge =>
        {
            Assert.Contains(edge.From.Node, nodeIds);
            Assert.Contains(edge.To.Node, nodeIds);
        });
        Assert.All(created.Graph.Comments, comment => Assert.DoesNotContain(comment.Id, template.Graph.Comments.Select(old => old.Id)));
    }

    [Fact]
    public void CreateDoesNotMutateTheLoadedTemplate()
    {
        ScriptTemplateLibrary.Create("creature-death");
        var loaded = ScriptTemplateLibrary.Load("creature-death");

        Assert.Equal("11111111111111111111111111111111", loaded.Id);
        Assert.Contains(loaded.Graph.Nodes, node => node.Id == "n1");
    }

    [Fact]
    public void TemplateNamesAreStable()
    {
        Assert.Equal(["blank", "command", "creature-death", "dungeon-encounter", "quest-giver", "spell-cast", "world-event"], ScriptTemplateLibrary.Names);
    }
}
