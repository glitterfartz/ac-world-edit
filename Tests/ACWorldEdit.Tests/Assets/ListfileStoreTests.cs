using System.Net;
using System.Text;
using ACWorldEdit.Assets;
using Xunit;

public class ListfileStoreTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "listfile-" + Guid.NewGuid().ToString("N"));

    public ListfileStoreTests()
    {
        Directory.CreateDirectory(directory);
    }

    public void Dispose() => Directory.Delete(directory, true);

    sealed class Fake(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond());
    }

    static string Listfile(int lines) => string.Join('\n', Enumerable.Range(1, lines).Select(i => $"{i};world/wmo/test/file_{i:D8}.wmo"));

    static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };

    [Fact]
    public void PathUsesTheOverrideWhenSet()
    {
        Assert.Equal(Path.Combine("u", ListfileStore.FileName), ListfileStore.PathIn("u"));
        Assert.Equal(Path.Combine("u", ListfileStore.FileName), ListfileStore.PathIn("u", " "));
        Assert.Equal("x.csv", ListfileStore.PathIn("u", "x.csv"));
    }

    [Fact]
    public async Task DownloadWritesAValidListfileAndReportsProgress()
    {
        string target = Path.Combine(directory, "sub", ListfileStore.FileName);
        string body = Listfile(60_000);
        long last = 0;

        await ListfileStore.DownloadAsync(target, new Progress<(long, long)>(p => last = Math.Max(last, p.Item1)), default, "http://test/list.csv", new Fake(() => Ok(body)));

        Assert.True(File.Exists(target));
        Assert.False(File.Exists(target + ".part"));
        Assert.Equal(60_000, PathListfile.Load(target).Count);
        Assert.Equal(7u, PathListfile.Load(target).Resolve("World\\WMO\\Test\\file_00000007.wmo"));
    }

    [Fact]
    public async Task InvalidDownloadKeepsThePreviousListfile()
    {
        string target = Path.Combine(directory, ListfileStore.FileName);
        File.WriteAllText(target, "old");

        await Assert.ThrowsAsync<InvalidDataException>(() => ListfileStore.DownloadAsync(target, null, default, "http://test/x", new Fake(() => Ok(new string('x', 2_000_000)))));
        await Assert.ThrowsAsync<HttpRequestException>(() => ListfileStore.DownloadAsync(target, null, default, "http://test/x", new Fake(() => new HttpResponseMessage(HttpStatusCode.NotFound))));

        Assert.Equal("old", File.ReadAllText(target));
        Assert.False(File.Exists(target + ".part"));
    }

    [Fact]
    public void LooksValidRejectsSmallAndMalformedFiles()
    {
        string small = Path.Combine(directory, "small.csv");
        File.WriteAllText(small, Listfile(10));
        string good = Path.Combine(directory, "good.csv");
        File.WriteAllText(good, Listfile(60_000));

        Assert.False(ListfileStore.LooksValid(small));
        Assert.False(ListfileStore.LooksValid(Path.Combine(directory, "missing.csv")));
        Assert.True(ListfileStore.LooksValid(good));
    }
}
