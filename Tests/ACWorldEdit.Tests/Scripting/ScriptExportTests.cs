using System.Security.Cryptography;
using System.Text;
using ACWorldEdit.Data.Scripting;
using ACWorldEdit.Foundation.Scripting;
using Xunit;

/// <summary>Export writes only what the manifest owns, records hashes, and aborts as a whole.</summary>
public class ScriptExportTests
{
    static readonly ScriptCompiler Compiler = new(AcScriptHosts.Profile);

    static string TempRoot() => Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"));

    static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    [Fact]
    public void ExportWritesTheLuaAndRecordsTheManifest()
    {
        string exportDirectory = TempRoot();
        var document = ScriptTemplateLibrary.Create("creature-death");
        var service = new ScriptExportService(Compiler);

        var result = service.Export(document, exportDirectory);

        Assert.True(result.Success, result.Error);
        var compilation = Compiler.Compile(document);
        string path = Path.Combine(exportDirectory, compilation.OutputName);
        Assert.Equal(compilation.LuaText, File.ReadAllText(path));
        var file = Assert.Single(result.Files);
        Assert.Equal(document.Id, file.DocumentId);
        Assert.Equal(Hash(compilation.LuaText!), file.Hash);

        var entry = Assert.Single(ScriptManifest.Load(exportDirectory).Entries);
        Assert.Equal(document.Id, entry.DocumentId);
        Assert.Equal(compilation.OutputName, entry.OutputName);
        Assert.Equal(file.Hash, entry.Hash);
        Assert.Equal(document.Host, entry.HostId);
        Assert.Equal(compilation.SourceHash, entry.SourceHash);
        Assert.Equal(compilation.CatalogHash, entry.CatalogHash);
    }

    [Fact]
    public void ExportAllCompilesEachDocumentWithItsOwnHost()
    {
        string exportDirectory = TempRoot();
        var aleDocument = ScriptTemplateLibrary.Create("creature-death");
        var elunaDocument = ScriptTemplateLibrary.Create("creature-death");
        elunaDocument.Name = "Eluna kill";
        elunaDocument.Host = TcScriptHosts.Id;
        var service = new ScriptExportService(Compiler);

        var result = service.ExportAll([aleDocument, elunaDocument], exportDirectory,
            document => ScriptHostRegistry.Profile(document.Host) ?? AcScriptHosts.Profile);

        Assert.True(result.Success, result.Error);
        var elunaCompilation = new ScriptCompiler(TcScriptHosts.Profile).Compile(elunaDocument);
        var entry = ScriptManifest.Load(exportDirectory).Entries.Single(item => item.DocumentId == elunaDocument.Id);
        Assert.Equal(TcScriptHosts.Id, entry.HostId);
        Assert.Equal(elunaCompilation.CatalogHash, entry.CatalogHash);
    }

    [Fact]
    public void RenamingADocumentReportsTheOldOutputAsOrphaned()
    {
        string exportDirectory = TempRoot();
        var document = ScriptTemplateLibrary.Create("creature-death");
        var service = new ScriptExportService(Compiler);
        var first = service.Export(document, exportDirectory);
        Assert.True(first.Success, first.Error);
        string oldPath = Assert.Single(first.Files).Path;

        document.Name = "Renamed kill";
        var second = service.Export(document, exportDirectory);

        Assert.True(second.Success, second.Error);
        Assert.Contains(second.Diagnostics, item =>
            item.Code == ScriptDiagnosticCodes.ExportOrphaned
            && item.Message.Contains(Path.GetFileName(oldPath), StringComparison.Ordinal));
        Assert.True(File.Exists(oldPath));
    }

    [Fact]
    public void ExportRefusesAFileItDoesNotOwn()
    {
        string exportDirectory = TempRoot();
        Directory.CreateDirectory(exportDirectory);
        var document = ScriptTemplateLibrary.Create("creature-death");
        string path = Path.Combine(exportDirectory, ScriptCompiler.OutputName(document));
        File.WriteAllText(path, "-- hand written, not ours");
        var service = new ScriptExportService(Compiler);

        var result = service.Export(document, exportDirectory);

        Assert.False(result.Success);
        Assert.Equal(ScriptDiagnosticCodes.ExportForeignFile, result.ErrorCode);
        Assert.Contains("own", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("-- hand written, not ours", File.ReadAllText(path));
        Assert.Empty(ScriptManifest.Load(exportDirectory).Entries);
    }

    [Fact]
    public void ExportRefusesWhenTheGeneratedFileWasEdited()
    {
        string exportDirectory = TempRoot();
        var document = ScriptTemplateLibrary.Create("creature-death");
        var service = new ScriptExportService(Compiler);
        Assert.True(service.Export(document, exportDirectory).Success);
        string path = Path.Combine(exportDirectory, ScriptCompiler.OutputName(document));
        File.AppendAllText(path, "\n-- hand edit");

        var result = service.Export(document, exportDirectory);

        Assert.False(result.Success);
        Assert.Equal(ScriptDiagnosticCodes.ExportForeignFile, result.ErrorCode);
        Assert.Contains("changed", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("-- hand edit", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void ExportingTwiceReplacesItsOwnFileAndUpdatesTheHash()
    {
        string exportDirectory = TempRoot();
        var document = ScriptTemplateLibrary.Create("creature-death");
        var service = new ScriptExportService(Compiler);
        Assert.True(service.Export(document, exportDirectory).Success);
        document.Description = "updated";

        var second = service.Export(document, exportDirectory);

        Assert.True(second.Success, second.Error);
        var compilation = Compiler.Compile(document);
        Assert.Equal(compilation.LuaText,
            File.ReadAllText(Path.Combine(exportDirectory, compilation.OutputName)));
        Assert.Equal(second.Files[0].Hash, Assert.Single(ScriptManifest.Load(exportDirectory).Entries).Hash);
    }

    [Fact]
    public void ExportAllWritesNothingWhenOneDocumentFails()
    {
        string exportDirectory = TempRoot();
        Directory.CreateDirectory(exportDirectory);
        var valid = ScriptTemplateLibrary.Create("creature-death");
        var invalid = new ScriptDocument
        {
            Id = "ffffffffffffffffffffffffffffffff",
            Name = "Broken",
            Graph = { Nodes = { new ScriptNode { Id = "n1", Kind = "future.node" } } }
        };
        var service = new ScriptExportService(Compiler);

        var result = service.ExportAll([valid, invalid], exportDirectory);

        Assert.False(result.Success);
        Assert.Equal(ScriptDiagnosticCodes.ExportAllBlocked, result.ErrorCode);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == ScriptDiagnosticCodes.UnknownNode);
        Assert.Empty(Directory.GetFiles(exportDirectory, "*.lua"));
        Assert.Empty(ScriptManifest.Load(exportDirectory).Entries);
    }

    [Fact]
    public void ExportAllRefusesCollidingOutputNames()
    {
        string exportDirectory = TempRoot();
        Directory.CreateDirectory(exportDirectory);
        var first = ScriptDocument.Create("Collide");
        first.Id = "abcdef01aaaaaaaaaaaaaaaaaaaaaaaa";
        var second = ScriptDocument.Create("Collide");
        second.Id = "abcdef01bbbbbbbbbbbbbbbbbbbbbbbb";
        var service = new ScriptExportService(Compiler);
        Assert.Equal(ScriptCompiler.OutputName(first), ScriptCompiler.OutputName(second));

        var result = service.ExportAll([first, second], exportDirectory);

        Assert.False(result.Success);
        Assert.Equal(ScriptDiagnosticCodes.ExportCollision, result.ErrorCode);
        Assert.Contains("collide", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(exportDirectory, "*.lua"));
        Assert.Empty(ScriptManifest.Load(exportDirectory).Entries);
    }

    [Fact]
    public void ManifestReportsOrphansWithoutDeletingThem()
    {
        string exportDirectory = TempRoot();
        var first = ScriptTemplateLibrary.Create("creature-death");
        var second = ScriptTemplateLibrary.Create("command");
        var service = new ScriptExportService(Compiler);
        Assert.True(service.ExportAll([first, second], exportDirectory).Success);
        string missing = Path.Combine(exportDirectory, ScriptCompiler.OutputName(first));
        File.Delete(missing);

        var manifest = ScriptManifest.Load(exportDirectory);

        var orphan = Assert.Single(manifest.Orphans(exportDirectory));
        Assert.Equal(first.Id, orphan.DocumentId);
        Assert.Equal(2, manifest.Entries.Count);
        Assert.True(File.Exists(Path.Combine(exportDirectory, ScriptCompiler.OutputName(second))));
    }
}
