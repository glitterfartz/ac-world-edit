using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using Xunit;

namespace ACWorldEdit.Tests.Support;

/// <summary>Where the optional external inputs live; unset or missing means the dependent tests skip.</summary>
public static class Environments
{
    public const string DbVariable = "ACWORLD_DB_INFO";
    public const string DataVariable = "ACWORLD_DATA_DIR";
    public const string TrinityDbVariable = "ACWORLD_TC_DB_INFO";

    /// <summary>The TrinityCore connection from ACWORLD_TC_DB_INFO (host;port;user;password;database[;hotfixes]).</summary>
    public static WorldConnection? TrinityConnection()
    {
        string? info = Environment.GetEnvironmentVariable(TrinityDbVariable);
        try
        {
            return string.IsNullOrWhiteSpace(info) ? null : WorldConnection.Parse(info, WorldDialect.TrinityCore);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Null when ACWORLD_TC_DB_INFO is usable; otherwise the skip reason.</summary>
    public static string? TrinitySkip() => TrinityConnection() != null ? null
        : string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TrinityDbVariable))
            ? "ACWORLD_TC_DB_INFO not set; skipping TrinityCore database test"
            : "ACWORLD_TC_DB_INFO is invalid (host;port;user;password;database[;hotfixes]); skipping TrinityCore database test";

    /// <summary>Null when ACWORLD_DB_INFO is usable; otherwise the skip reason.</summary>
    public static string? DatabaseSkip()
    {
        string? info = Environment.GetEnvironmentVariable(DbVariable);
        if (string.IsNullOrWhiteSpace(info))
        {
            return "ACWORLD_DB_INFO not set; skipping read-only database test";
        }

        try
        {
            _ = DatabaseSettings.Parse(info);
            return null;
        }
        catch (InvalidDataException)
        {
            return "ACWORLD_DB_INFO is invalid (host;port;user;password;database); skipping read-only database test";
        }
    }

    /// <summary>The client data directory: ACWORLD_DATA_DIR, else the nearest wow-data folder above the test binaries.</summary>
    public static string? DataDirectory()
    {
        string? configured = Environment.GetEnvironmentVariable(DataVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Directory.Exists(configured) ? configured : null;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "wow-data");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string? DataSkip() => DataDirectory() == null ? "client data not found (set ACWORLD_DATA_DIR or place wow-data in the repo); skipping corpus test" : null;
}

/// <summary>Opened once and shared by all corpus tests.</summary>
public static class ClientData
{
    static readonly Lazy<AssetStore> store = new(() => new AssetStore(Environments.DataDirectory() ?? throw new InvalidOperationException("client data missing")));

    public static AssetStore Store => store.Value;
}

/// <summary>Fact that skips when the client data is unavailable.</summary>
public sealed class ClientDataFactAttribute : FactAttribute
{
    public ClientDataFactAttribute()
    {
        Skip = Environments.DataSkip();
    }
}

/// <summary>Fact that skips unless ACWORLD_DB_INFO is set and valid (and, optionally, client data exists).</summary>
public sealed class DbFactAttribute : FactAttribute
{
    public DbFactAttribute()
    {
        Skip = Environments.DatabaseSkip();
    }

    bool needsClientData;

    public bool NeedsClientData
    {
        get => needsClientData;
        set
        {
            needsClientData = value;
            Skip = Environments.DatabaseSkip() ?? (value ? Environments.DataSkip() : null);
        }
    }
}

/// <summary>
/// Fact for tests that must write (CREATE DATABASE / CREATE TABLE): skips unless ACWORLD_DB_INFO
/// is set and the server actually grants this user CREATE. The probe runs once and is cached, so
/// restricted users (like the shared read-only fixture) skip instead of failing.
/// </summary>
public sealed class DbWriteFactAttribute : FactAttribute
{
    static readonly Dictionary<string, string?> ProbeCache = [];
    static readonly object ProbeLock = new();

    public DbWriteFactAttribute()
    {
        Skip = Environments.DatabaseSkip() ?? ProbeWriteAccess();
    }

    static string? ProbeWriteAccess()
    {
        string? info = Environment.GetEnvironmentVariable(Environments.DbVariable);
        if (string.IsNullOrWhiteSpace(info))
        {
            return "ACWORLD_DB_INFO not set; skipping database write test";
        }

        lock (ProbeLock)
        {
            if (ProbeCache.TryGetValue(info, out string? cached))
            {
                return cached;
            }

            string? result = Probe(info);
            ProbeCache[info] = result;
            return result;
        }
    }

    static string? Probe(string info)
    {
        DatabaseSettings config;
        try
        {
            config = DatabaseSettings.Parse(info);
        }
        catch (InvalidDataException)
        {
            return "ACWORLD_DB_INFO is invalid; skipping database write test";
        }

        string probe = "acwe_probe_" + Guid.NewGuid().ToString("N")[..12];
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            ProbeAsync(config, probe, timeout.Token).GetAwaiter().GetResult();
            return null;
        }
        catch (Exception error)
        {
            return $"Database user '{config.User}' cannot create tables here ({ShortError(error)}); skipping database write test";
        }
    }

    static async Task ProbeAsync(DatabaseSettings config, string probe, CancellationToken token)
    {
        await using var connection = new MySqlConnector.MySqlConnection(config.BuildConnectionString());
        await connection.OpenAsync(token);
        await using (var create = new MySqlConnector.MySqlCommand($"CREATE TABLE `{probe}` (`id` INT NOT NULL PRIMARY KEY) ENGINE=InnoDB", connection))
        {
            await create.ExecuteNonQueryAsync(token);
        }

        await using (var drop = new MySqlConnector.MySqlCommand($"DROP TABLE `{probe}`", connection))
        {
            await drop.ExecuteNonQueryAsync(token);
        }
    }

    static string ShortError(Exception error) => error is MySqlConnector.MySqlException mysql
        ? $"MySQL error {mysql.Number}"
        : error.GetType().Name;
}

/// <summary>Fact that skips unless ACWORLD_TC_DB_INFO names a TrinityCore world database.</summary>
public sealed class TcDbFactAttribute : FactAttribute
{
    public TcDbFactAttribute()
    {
        Skip = Environments.TrinitySkip();
    }
}

/// <summary>
/// A test that documents a bug found by testing and not yet fixed. Skipped by default; set
/// ACWE_RUN_KNOWN_BUGS=1 to run them and watch them fail. Carries Category=KnownBug.
/// </summary>
[Xunit.Sdk.TraitDiscoverer("ACWorldEdit.Tests.Support.KnownBugDiscoverer", "ACWorldEdit.Tests")]
public sealed class KnownBugAttribute : FactAttribute, Xunit.Sdk.ITraitAttribute
{
    public KnownBugAttribute(string id, string finding)
    {
        Id = id;
        Finding = finding;
        if (Environment.GetEnvironmentVariable("ACWE_RUN_KNOWN_BUGS") != "1")
        {
            Skip = $"KnownBug {id}: {finding}";
        }
    }

    public string Id { get; }

    public string Finding { get; }
}

public sealed class KnownBugDiscoverer : Xunit.Sdk.ITraitDiscoverer
{
    public IEnumerable<KeyValuePair<string, string>> GetTraits(Xunit.Abstractions.IAttributeInfo traitAttribute)
    {
        yield return new("Category", "KnownBug");
        yield return new("KnownBug", traitAttribute.GetConstructorArguments().First()?.ToString() ?? "");
    }
}

/// <summary>Theory that skips when the client data is unavailable.</summary>
public sealed class ClientDataTheoryAttribute : TheoryAttribute
{
    public ClientDataTheoryAttribute()
    {
        Skip = Environments.DataSkip();
    }
}
