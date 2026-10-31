using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests;

public class DependencyCheckTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "acw-deps-" + Guid.NewGuid().ToString("N"));

    public DependencyCheckTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void WrathNeedsNothingBeyondTheClient()
    {
        Assert.Empty(DependencyCheck.Missing(ClientKind.Wrath335, Path.Combine(root, "none.dll"), Path.Combine(root, "none"), root));
    }

    [Fact]
    public void RetailReportsBothMissingDependencies()
    {
        var problems = DependencyCheck.Missing(ClientKind.Retail, Path.Combine(root, "none.dll"), Path.Combine(root, "none"), root);

        Assert.Equal([DependencyCheck.CascLibMissing, DependencyCheck.DefinitionsMissing], problems);
        Assert.All(problems, problem => Assert.Contains("Tools/setup.py", problem));
    }

    [Fact]
    public void RetailAcceptsOverridesThatExist()
    {
        string dll = Path.Combine(root, "CascLib.dll");
        string definitions = Path.Combine(root, "definitions");
        File.WriteAllBytes(dll, []);
        Directory.CreateDirectory(definitions);

        Assert.Empty(DependencyCheck.Missing(ClientKind.Retail, dll, definitions, root));
    }
}
