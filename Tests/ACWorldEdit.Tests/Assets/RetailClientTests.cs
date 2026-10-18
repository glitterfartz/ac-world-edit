using ACWorldEdit.Assets;
using Xunit;

/// <summary>
/// Retail install detection and file-search helpers over synthetic folders; no client, CascLib or
/// network needed.
/// </summary>
public class RetailClientTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "acwe-retail-" + Guid.NewGuid().ToString("N"));

    public RetailClientTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        Directory.Delete(root, true);
    }

    string Write(params string[] lines)
    {
        File.WriteAllLines(Path.Combine(root, ".build.info"), lines);
        return root;
    }

    const string Header = "Branch!STRING:0|Active!DEC:1|Product!STRING:0|Version!STRING:0|Tags!STRING:0";

    [Fact]
    public void DetectReadsTheActiveWowRow()
    {
        Write(Header, "us|0|wow|1.0.0.1|x", "us|1|wow|12.1.5.70448|y", "us|1|wowt|99.0.0.1|z");
        var install = RetailInstall.Detect(root);
        Assert.Equal("12.1.5.70448", install.Build);
        Assert.Equal(root, install.Root);
        Assert.Equal("enUS", install.Locale);
    }

    [Fact]
    public void DetectAcceptsTheProductFolder()
    {
        Write(Header, "us|1|wow|12.1.5.70448|y");
        Directory.CreateDirectory(Path.Combine(root, "_retail_"));
        var install = RetailInstall.Detect(Path.Combine(root, "_retail_"));
        Assert.Equal(root, install.Root);
    }

    [Fact]
    public void DetectRejectsAFolderWithoutBuildInfo()
    {
        Assert.Throws<FileNotFoundException>(() => RetailInstall.Detect(root));
    }

    [Fact]
    public void DetectRejectsMissingActiveWowRow()
    {
        Write(Header, "us|0|wow|12.1.5.70448|y", "us|1|wowt|12.1.5.1|z");
        Assert.Throws<InvalidDataException>(() => RetailInstall.Detect(root));
    }

    [Fact]
    public void DetectRejectsMissingColumns()
    {
        Write("Branch!STRING:0|Product!STRING:0", "us|wow");
        Assert.Throws<InvalidDataException>(() => RetailInstall.Detect(root));
    }

    [Fact]
    public void NativeCandidatesPreferTheOverrideThenTheBaseDirectory()
    {
        string[] found = CascNative.Candidates("X:/custom/CascLib.dll", Path.Combine(root, "bin")).ToArray();
        Assert.Equal("X:/custom/CascLib.dll", found[0]);
        Assert.Equal(Path.Combine(root, "bin", "CascLib.dll"), found[1]);
        Assert.Contains(Path.Combine(root, "bin", "native", "win-x64", "CascLib.dll"), found);
        Assert.Contains(Path.Combine(root, "native", "win-x64", "CascLib.dll"), found);
    }

    [Fact]
    public void NativeCandidatesSkipBlankInputs()
    {
        Assert.Empty(CascNative.Candidates(" ", null, ""));
    }

    [Fact]
    public void DefinitionsOverrideWinsWhenItExists()
    {
        string custom = Path.Combine(root, "custom");
        Directory.CreateDirectory(custom);
        Assert.Equal(custom, Db2Tables.FindDefinitions(custom, root));
        Assert.Null(Db2Tables.FindDefinitions(Path.Combine(root, "missing"), root));
    }

    [Fact]
    public void DefinitionsAreFoundBesideOrAboveTheStartDirectory()
    {
        string beside = Path.Combine(root, "bin", "definitions");
        Directory.CreateDirectory(beside);
        File.WriteAllText(Path.Combine(beside, "Map.dbd"), "COLUMNS");
        Assert.Equal(beside, Db2Tables.FindDefinitions(null, Path.Combine(root, "bin", "deeper")));

        string repo = Path.Combine(root, "repo", "Data", "definitions");
        Directory.CreateDirectory(repo);
        File.WriteAllText(Path.Combine(repo, "Map.dbd"), "COLUMNS");
        Assert.Equal(repo, Db2Tables.FindDefinitions(null, Path.Combine(root, "repo", "Tests", "bin")));
    }

    [Fact]
    public void DefinitionsRequireMapDbd()
    {
        Directory.CreateDirectory(Path.Combine(root, "definitions"));
        Assert.Null(Db2Tables.FindDefinitions(null, root));
    }

    [Fact]
    public void MpqMapsHaveNoWdtFileId()
    {
        Assert.Equal(0u, new MapInfo(0, "Azeroth", "Eastern Kingdoms").WdtFileId);
    }
}
