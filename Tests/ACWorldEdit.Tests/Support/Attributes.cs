using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using Xunit;

namespace ACWorldEdit.Tests.Support;

/// <summary>Where the optional external inputs live; unset or missing means the dependent tests skip.</summary>
public static class Environments
{
    public const string DbVariable = "ACWORLD_DB_INFO";
    public const string DataVariable = "ACWORLD_DATA_DIR";

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
