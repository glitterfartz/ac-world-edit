using System.Text;

namespace ACWorldEdit.Foundation;

/// <summary>
/// Which dialect each database source key belongs to, so drafts made against a database that is not
/// the open one (an earlier connection, a previous session) still export with their own statements.
/// A source key is a one-way hash, so the dialect has to be remembered while the source is open. An
/// unknown source is AzerothCore, which is what every draft was before TrinityCore support.
/// </summary>
public sealed class SourceDialects
{
    readonly string path;
    readonly Dictionary<string, WorldDialect> known = [];

    public SourceDialects(string path)
    {
        this.path = path;
        try
        {
            if (File.Exists(path))
            {
                foreach (var (key, dialect) in System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, WorldDialect>>(File.ReadAllText(path)) ?? [])
                {
                    known[key] = dialect;
                }
            }
        }
        catch (Exception)
        {
            // An unreadable record only means older non-AzerothCore drafts need their database opened again.
            known.Clear();
        }
    }

    public WorldDialect DialectOf(string source) => known.GetValueOrDefault(source, WorldDialect.AzerothCore);

    public IWorldSchema SchemaFor(string source) => WorldSchemas.For(DialectOf(source));

    /// <summary>Record the open source's dialect; saved only when it is new or changed.</summary>
    public void Remember(string source, WorldDialect dialect)
    {
        if (known.TryGetValue(source, out var current) ? current == dialect : dialect == WorldDialect.AzerothCore)
        {
            return;
        }

        known[source] = dialect;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new SortedDictionary<string, WorldDialect>(known, StringComparer.Ordinal)), new UTF8Encoding(false));
        }
        catch (Exception)
        {
            // Not persisting only matters for drafts left from this source in a later session.
        }
    }
}
