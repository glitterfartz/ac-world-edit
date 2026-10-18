using ACWorldEdit.Assets;
using Xunit;

/// <summary>
/// The picker's folder checks over synthetic folders: they decide whether a choice looks like the
/// selected client kind, and never open an archive.
/// </summary>
public class ClientFolderTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "acwe-folder-" + Guid.NewGuid().ToString("N"));

    public ClientFolderTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        Directory.Delete(root, true);
    }

    string Make(string relative, string? file = null)
    {
        string dir = Path.Combine(root, relative);
        Directory.CreateDirectory(dir);
        if (file != null)
        {
            File.WriteAllText(Path.Combine(dir, file), "x");
        }

        return dir;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankChoicesAreRejected(string? path)
    {
        Assert.NotNull(ClientFolder.Validate(ClientKind.Wrath335, path));
        Assert.NotNull(ClientFolder.Validate(ClientKind.Retail, path));
    }

    [Fact]
    public void MissingFoldersAreRejected()
    {
        Assert.Contains("does not exist", ClientFolder.Validate(ClientKind.Retail, Path.Combine(root, "nope")));
    }

    [Fact]
    public void RetailNeedsBuildInfoAtTheRootOrAboveTheProductFolder()
    {
        string install = Make("wow", ".build.info");
        Assert.Null(ClientFolder.Validate(ClientKind.Retail, install));
        string product = Make(Path.Combine("wow", "_retail_"));
        Assert.Null(ClientFolder.Validate(ClientKind.Retail, product));
        Assert.NotNull(ClientFolder.Validate(ClientKind.Retail, Make("other")));
    }

    [Fact]
    public void WrathAcceptsTheDataFolderOrTheRootAboveIt()
    {
        string data = Make(Path.Combine("client", "Data"), "common.MPQ");
        string client = Path.GetDirectoryName(data)!;
        Assert.Null(ClientFolder.Validate(ClientKind.Wrath335, data));
        Assert.Null(ClientFolder.Validate(ClientKind.Wrath335, client));
        Assert.Equal(data, ClientFolder.Resolve(ClientKind.Wrath335, client));
        Assert.Equal(data, ClientFolder.Resolve(ClientKind.Wrath335, data));
        Assert.NotNull(ClientFolder.Validate(ClientKind.Wrath335, Make("empty")));
    }

    [Fact]
    public void RetailPathsAreNeverRewritten()
    {
        string install = Make("wow2", ".build.info");
        Assert.Equal(install, ClientFolder.Resolve(ClientKind.Retail, install));
    }
}
