using ACWorldEdit.Data.Scripting;
using ACWorldEdit.Foundation.Scripting;
using Xunit;

/// <summary>Workspace scan/load/save/delete, atomic writes, stamps, duplicate ids and path safety.</summary>
public class ScriptWorkspaceTests
{
    static string TempRoot() => Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"));

    static ScriptWorkspace NewWorkspace(out string root)
    {
        root = TempRoot();
        return new ScriptWorkspace(root);
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var workspace = NewWorkspace(out _);
        var document = ScriptDocument.Create("Creature On Death");

        string path = workspace.Save(document);
        var loaded = workspace.Load(path);

        Assert.Equal(Path.Combine(workspace.Directory, "creature-on-death.acwscript"), path);
        Assert.Equal(document.Id, loaded.Id);
        Assert.Equal(document.Name, loaded.Name);
        Assert.Equal(ScriptDocumentJson.CanonicalHash(document), ScriptDocumentJson.CanonicalHash(loaded));
    }

    [Fact]
    public void SavingTheSameDocumentTwiceReusesItsFile()
    {
        var workspace = NewWorkspace(out _);
        var document = ScriptDocument.Create("Creature On Death");

        string first = workspace.Save(document);
        document.Description = "edited";
        string second = workspace.Save(document);

        Assert.Equal(first, second);
        Assert.Single(workspace.Scan().Files);
    }

    [Fact]
    public void ScanListsEveryDocumentWithItsIdAndHash()
    {
        var workspace = NewWorkspace(out _);
        var first = ScriptDocument.Create("First");
        var second = ScriptDocument.Create("Second");
        workspace.Save(first);
        workspace.Save(second);

        var scan = workspace.Scan();

        Assert.Empty(scan.Issues);
        Assert.Equal(2, scan.Files.Count);
        Assert.Contains(scan.Files, file => file.DocumentId == first.Id);
        Assert.Contains(scan.Files, file => file.DocumentId == second.Id);
        Assert.All(scan.Files, file => Assert.Equal(ScriptDocumentJson.CanonicalHash(workspace.Load(file.Path)), file.Hash));
    }

    [Fact]
    public void ScanDoesNotFailWhenTheDirectoryDoesNotExist()
    {
        var workspace = NewWorkspace(out _);

        var scan = workspace.Scan();

        Assert.Empty(scan.Files);
        Assert.Empty(scan.Issues);
    }

    [Fact]
    public void ScanReportsDuplicateDocumentIds()
    {
        var workspace = NewWorkspace(out string root);
        Directory.CreateDirectory(root);
        var document = ScriptDocument.Create("Duplicated");
        File.WriteAllText(Path.Combine(root, "one.acwscript"), ScriptDocumentJson.Save(document));
        File.WriteAllText(Path.Combine(root, "two.acwscript"), ScriptDocumentJson.Save(document));

        var scan = workspace.Scan();

        Assert.Equal(2, scan.Files.Count);
        Assert.Contains(scan.Issues, issue => issue.Code == ScriptWorkspace.DuplicateIdCode);
    }

    [Fact]
    public void SlugCollisionsGetDistinctFileNames()
    {
        var workspace = NewWorkspace(out _);
        var first = ScriptDocument.Create("Same Name");
        var second = ScriptDocument.Create("Same Name");

        string firstPath = workspace.Save(first);
        string secondPath = workspace.Save(second);

        Assert.NotEqual(firstPath, secondPath);
        Assert.Equal(first.Id, workspace.Load(firstPath).Id);
        Assert.Equal(second.Id, workspace.Load(secondPath).Id);
        Assert.Equal(2, workspace.Scan().Files.Count);
    }

    [Fact]
    public void SaveRefusesToOverwriteAnExternallyModifiedFile()
    {
        var workspace = NewWorkspace(out _);
        var document = ScriptDocument.Create("Guarded");
        string path = workspace.Save(document);
        var stamp = workspace.Stamp(path);
        File.AppendAllText(path, "\n// changed outside the editor");

        Assert.NotEqual(stamp.Hash, workspace.Stamp(path).Hash);
        var error = Assert.Throws<ScriptWorkspaceException>(() => workspace.Save(document, stamp));
        Assert.Equal(ScriptWorkspace.StaleStampCode, error.Code);
    }

    [Fact]
    public void SaveWithAMatchingStampSucceeds()
    {
        var workspace = NewWorkspace(out _);
        var document = ScriptDocument.Create("Guarded");
        string path = workspace.Save(document);
        var stamp = workspace.Stamp(path);

        document.Description = "edited";
        Assert.Equal(path, workspace.Save(document, stamp));
        Assert.Equal("edited", workspace.Load(path).Description);
    }

    [Fact]
    public void DeleteRemovesTheFile()
    {
        var workspace = NewWorkspace(out _);
        string path = workspace.Save(ScriptDocument.Create("Temporary"));

        workspace.Delete(path);

        Assert.False(File.Exists(path));
        Assert.Empty(workspace.Scan().Files);
    }

    [Fact]
    public void DeleteRefusesAStaleStamp()
    {
        var workspace = NewWorkspace(out _);
        var document = ScriptDocument.Create("Guarded");
        string path = workspace.Save(document);
        var stamp = workspace.Stamp(path);
        File.AppendAllText(path, "x");

        var error = Assert.Throws<ScriptWorkspaceException>(() => workspace.Delete(path, stamp));
        Assert.Equal(ScriptWorkspace.StaleStampCode, error.Code);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void PathsOutsideTheWorkspaceAreRejected()
    {
        var workspace = NewWorkspace(out _);
        string outside = Path.Combine(Path.GetTempPath(), "outside.acwscript");

        Assert.Throws<ScriptWorkspaceException>(() => workspace.Load("../outside.acwscript"));
        Assert.Throws<ScriptWorkspaceException>(() => workspace.Load(outside));
        Assert.Throws<ScriptWorkspaceException>(() => workspace.Delete("../outside.acwscript"));
    }

    [Fact]
    public void SaveLeavesNoTemporaryFileBehind()
    {
        var workspace = NewWorkspace(out _);
        workspace.Save(ScriptDocument.Create("Atomic"));

        Assert.Empty(Directory.GetFiles(workspace.Directory, "*.tmp"));
    }

    [Fact]
    public void LoadRejectsMalformedDocuments()
    {
        var workspace = NewWorkspace(out string root);
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "broken.acwscript");
        File.WriteAllText(path, "{ not json");

        var error = Assert.Throws<ScriptWorkspaceException>(() => workspace.Load(path));
        Assert.Equal(ScriptWorkspace.InvalidDocumentCode, error.Code);
        Assert.Contains(workspace.Scan().Issues, issue => issue.Code == ScriptWorkspace.UnreadableCode);
    }
}
