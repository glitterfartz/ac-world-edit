using ACWorldEdit.Assets;
using ACWorldEdit.Tests.Support;
using Xunit;

/// <summary>
/// FileDataID reference strings share the asset-path fields with MPQ paths, so the two forms must
/// never be confused and malformed references must not parse.
/// </summary>
public class AssetRefTests
{
    [Fact]
    public void RoundTripsAFileId()
    {
        string reference = AssetRef.FromFileId(219901);
        Assert.Equal("fdid:219901", reference);
        Assert.True(AssetRef.TryParse(reference, out uint id));
        Assert.Equal(219901u, id);
        Assert.True(AssetRef.IsFileReference(reference));
    }

    [Fact]
    public void ZeroIsNotAFile()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AssetRef.FromFileId(0));
        Assert.False(AssetRef.TryParse("fdid:0", out _));
    }

    [Theory]
    [InlineData("fdid:")]
    [InlineData("fdid:-5")]
    [InlineData("fdid:12x")]
    [InlineData("fdid: 12")]
    [InlineData("fdid:4294967296")]
    [InlineData("FDID:12")]
    public void MalformedReferencesDoNotParse(string reference)
    {
        Assert.False(AssetRef.TryParse(reference, out uint id));
        Assert.Equal(0u, id);
    }

    [Theory]
    [InlineData(@"World\Maps\Azeroth\Azeroth.wdt")]
    [InlineData("Textures/Minimap/md5translate.trs")]
    [InlineData("")]
    public void PathsAreNotReferences(string path)
    {
        Assert.False(AssetRef.IsFileReference(path));
        Assert.False(AssetRef.TryParse(path, out _));
    }

    [Fact]
    public void MalformedReferencesAreStillReferences()
    {
        // A path-only source must refuse these rather than report an ordinary missing path.
        Assert.True(AssetRef.IsFileReference("fdid:12x"));
    }

    [ClientDataFact]
    [Trait("Category", "Corpus")]
    public void MpqStoreRefusesFileIds()
    {
        var store = ClientData.Store;
        Assert.Equal(ClientKind.Wrath335, store.Kind);
        Assert.Equal("3.3.5.12340", store.Build);
        Assert.Throws<NotSupportedException>(() => store.Read(AssetRef.FromFileId(775971)));
        Assert.False(store.Exists(AssetRef.FromFileId(775971)));
        Assert.Equal("missing", store.Source(AssetRef.FromFileId(775971)));
    }
}
