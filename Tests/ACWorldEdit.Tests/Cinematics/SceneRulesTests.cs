using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Cinematics;

/// <summary>TcSceneRules: a complete new scene row and the package checks, with and without a client.</summary>
public class SceneRulesTests
{
    [Fact]
    public void NewSceneFillsEveryColumn()
    {
        var row = TcSceneRules.NewScene(35);
        row.Validate(TcTables.SceneTemplate);

        Assert.Equal(35u, row.U("SceneId"));
        Assert.Equal(0u, row.U("ScriptPackageID"));
        Assert.Equal("", row.T("ScriptName"));
        Assert.Equal(TcTables.SceneTemplate.Columns.Length, row.Values.Count);
    }

    [Fact]
    public void CheckWarnsWithoutAScriptPackage()
    {
        var rows = new RowSet();
        rows.Set(TcTables.SceneTemplate, TcSceneRules.NewScene(35));

        var issue = Assert.Single(TcSceneRules.Check(35, rows, _ => true));
        Assert.False(issue.Error);
        Assert.Contains("No script package", issue.Message);
    }

    [Fact]
    public void CheckRejectsAnUnknownScriptPackage()
    {
        var rows = new RowSet();
        rows.Set(TcTables.SceneTemplate, TcSceneRules.NewScene(35).With("ScriptPackageID", 999u));

        Assert.Contains(TcSceneRules.Check(35, rows, _ => false), issue => issue.Error && issue.Message.Contains("package 999"));
    }

    [Fact]
    public void CheckAcceptsAKnownPackage()
    {
        var rows = new RowSet();
        rows.Set(TcTables.SceneTemplate, TcSceneRules.NewScene(35).With("ScriptPackageID", 186u));

        Assert.Empty(TcSceneRules.Check(35, rows, id => id == 186));
    }
}
