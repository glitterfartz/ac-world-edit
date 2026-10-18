using System.Net.Http;
using System.Net.Http.Headers;

namespace ACWorldEdit.Assets;

/// <summary>
/// Where the community listfile lives and how it is downloaded. The file is user data (never bundled
/// or read from the game folders): it is fetched only when the user asks, and replaced only by a
/// complete, validated download so a failed update keeps the previous copy.
/// </summary>
public static class ListfileStore
{
    public const string FileName = "community-listfile.csv";
    public const string Variable = "ACWORLD_LISTFILE";
    public const string DefaultUrl = "https://github.com/wowdev/wow-listfile/releases/latest/download/community-listfile.csv";

    /// <summary>
    /// The listfile path: <see cref="Variable"/> when set, else <see cref="FileName"/> inside the
    /// user-data directory. The file may not exist yet.
    /// </summary>
    public static string PathIn(string userDirectory, string? overridePath = null) =>
        string.IsNullOrWhiteSpace(overridePath) ? Path.Combine(userDirectory, FileName) : overridePath;

    /// <summary>
    /// Cheap sanity check on a downloaded file: large enough, and its first lines are "id;path". The
    /// whole file is not parsed.
    /// </summary>
    public static bool LooksValid(string file)
    {
        if (!File.Exists(file) || new FileInfo(file).Length < 1_000_000)
        {
            return false;
        }

        int good = 0;
        foreach (string line in File.ReadLines(file).Take(200))
        {
            int split = line.IndexOf(';');
            if (split <= 0 || !uint.TryParse(line.AsSpan(0, split), out _) || split == line.Length - 1)
            {
                return false;
            }

            good++;
        }

        return good == 200;
    }

    /// <summary>
    /// Download to "<paramref name="destination"/>.part", validate, then move into place. Progress is
    /// (bytes read, total bytes or -1). Throws on network errors, cancellation or an invalid file; the
    /// partial file is removed and any existing listfile is left untouched.
    /// </summary>
    public static async Task DownloadAsync(string destination, IProgress<(long Read, long Total)>? progress = null, CancellationToken cancel = default, string url = DefaultUrl, HttpMessageHandler? handler = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        string partial = destination + ".part";
        try
        {
            using var http = handler == null ? new HttpClient() : new HttpClient(handler);
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ACWorldEdit", "1.0"));
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? -1;
            await using (var source = await response.Content.ReadAsStreamAsync(cancel))
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
            {
                var buffer = new byte[1 << 16];
                long read = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, count), cancel);
                    read += count;
                    progress?.Report((read, total));
                }
            }

            if (!LooksValid(partial))
            {
                throw new InvalidDataException("The downloaded file is not a listfile (expected \"id;path\" lines).");
            }

            File.Move(partial, destination, true);
        }
        finally
        {
            File.Delete(partial);
        }
    }
}
