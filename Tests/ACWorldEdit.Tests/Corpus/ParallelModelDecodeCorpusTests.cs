using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ACWorldEdit.Assets;
using ACWorldEdit.Tests.Support;
using Xunit;

/// <summary>
/// The model decode path (parse, BLP decode, palette baking, batch merging) run on several threads at
/// once, as the viewer's decode workers do, produces the same bytes as running it on one.
/// </summary>
[Trait("Category", "Corpus")]
[Collection(RetailData.Collection)]
public class ParallelModelDecodeCorpusTests
{
    /// <summary>The decode steps of <c>ModelLibrary.DecodeModel</c>, reduced to a hash of everything produced.</summary>
    static string Decode(CreatureAppearance appearance)
    {
        var files = RetailData.Files;
        var data = M2Model.Read(files, appearance.Path, appearance.Textures);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string path in data.Surfaces.Select(s => s.Texture).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            try
            {
                hash.AppendData(BlpTexture.Decode(files.Read(path), 0).Rgba);
            }
            catch (Exception e)
            {
                // The viewer warns and draws the fallback texture; the failure itself must repeat.
                hash.AppendData(System.Text.Encoding.UTF8.GetBytes(path + e.GetType().Name));
            }
        }

        var materials = MaterialPalette.Bake(data, CancellationToken.None);
        var animation = AnimationPalette.Bake(data, CancellationToken.None);
        hash.AppendData(animation.Pixels);
        hash.AppendData(materials.Pixels);
        var (surfaces, _) = SurfaceMerge.Merge(data.Surfaces, materials.Surfaces);
        foreach (var surface in surfaces)
        {
            hash.AppendData(MemoryMarshal.AsBytes(surface.Positions.AsSpan()));
            hash.AppendData(MemoryMarshal.AsBytes(surface.Indices.AsSpan()));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    [RetailClientFact]
    public void CreatureModelsDecodeToTheSameBytesOnFourThreadsAsOnOne()
    {
        var catalog = new RetailCreatureCatalog(RetailData.Tables);
        List<CreatureAppearance> appearances = [];
        foreach (var display in RetailData.Tables.Table("CreatureDisplayInfo").Values.Take(400))
        {
            try
            {
                var appearance = catalog.Resolve((uint)display.ID);
                if (appearances.All(a => a.Path != appearance.Path))
                {
                    appearances.Add(appearance);
                }
            }
            catch (InvalidDataException)
            {
            }
        }

        appearances = appearances.Take(60).ToList();
        Assert.True(appearances.Count >= 30, $"only {appearances.Count} distinct models resolved");

        var serial = new Dictionary<string, string>();
        foreach (var appearance in appearances)
        {
            try
            {
                serial[appearance.Path] = Decode(appearance);
            }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException or FileNotFoundException or CascReadException)
            {
            }
        }

        Assert.True(serial.Count >= 20, $"only {serial.Count} models decoded");

        ConcurrentDictionary<string, string> parallel = new();
        Parallel.ForEach(
            appearances.Where(a => serial.ContainsKey(a.Path)),
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            appearance => parallel[appearance.Path] = Decode(appearance));

        Assert.Equal(serial.Count, parallel.Count);
        foreach (var (path, expected) in serial)
        {
            Assert.True(expected == parallel[path], $"{path} decoded differently on several threads");
        }
    }
}
