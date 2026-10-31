using ACWorldEdit.Assets;
using Xunit;

namespace ACWorldEdit.Tests.Support;

/// <summary>
/// Optional retail client for the RetailClient tests: ACWORLD_RETAIL_DIR names the install root (the
/// folder holding .build.info). The install is only read; nothing is written to it. Tests skip with a
/// reason when the variable, CascLib.dll or the definitions are missing.
/// </summary>
public static class RetailEnvironment
{
    public const string Variable = "ACWORLD_RETAIL_DIR";

    public static string? Directory()
    {
        string? configured = Environment.GetEnvironmentVariable(Variable);
        return !string.IsNullOrWhiteSpace(configured) && System.IO.Directory.Exists(configured) ? configured : null;
    }

    public static string? DefinitionsDirectory() => Db2Tables.FindDefinitions(Environment.GetEnvironmentVariable("ACWORLD_DBD_DIR"), AppContext.BaseDirectory);

    public static bool CascLibPresent() => CascNative.Candidates(Environment.GetEnvironmentVariable("ACWORLD_CASCLIB"), AppContext.BaseDirectory).Any(File.Exists);

    public static string? Skip()
    {
        if (Directory() == null)
        {
            return "ACWORLD_RETAIL_DIR not set (or missing); skipping retail client test";
        }

        if (!CascLibPresent())
        {
            return "CascLib.dll not found (run Tools/setup.py); skipping retail client test";
        }

        return DefinitionsDirectory() == null ? "Data/WoWDBDefs not found (git submodule update --init); skipping retail client test" : null;
    }
}

/// <summary>
/// The downloaded community listfile: ACWORLD_LISTFILE, else the viewer's user-data copy (File →
/// Update listfile…). Null when neither exists.
/// </summary>
public static class ListfileEnvironment
{
    public static string? Path()
    {
        string appData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godot", "app_userdata", "ACWorldEdit");
        string path = ListfileStore.PathIn(appData, Environment.GetEnvironmentVariable(ListfileStore.Variable));
        return File.Exists(path) ? path : null;
    }

    public static string? Skip() => RetailEnvironment.Skip() ?? (Path() == null ? "community listfile not downloaded (File → Update listfile… or ACWORLD_LISTFILE); skipping" : null);
}

/// <summary>Fact that also needs the downloaded community listfile.</summary>
public sealed class RetailListfileFactAttribute : FactAttribute
{
    public RetailListfileFactAttribute()
    {
        Skip = ListfileEnvironment.Skip();
    }
}

/// <summary>
/// Opened once and shared by the retail tests. Db2Tables is not thread-safe, so every test class that
/// reads it joins the <see cref="Collection"/> collection, whose classes run one at a time.
/// </summary>
public static class RetailData
{
    public const string Collection = "RetailClient";

    static readonly Lazy<CascFiles> files = new(() => new CascFiles(RetailEnvironment.Directory()!));
    static readonly Lazy<Db2Tables> tables = new(() => new Db2Tables(Files, RetailEnvironment.DefinitionsDirectory()!));

    public static CascFiles Files => files.Value;

    public static Db2Tables Tables => tables.Value;
}

[CollectionDefinition(RetailData.Collection)]
public sealed class RetailClientCollection;

/// <summary>Fact that skips unless a retail client, CascLib.dll and the definitions are available.</summary>
public sealed class RetailClientFactAttribute : FactAttribute
{
    public RetailClientFactAttribute()
    {
        Skip = RetailEnvironment.Skip();
    }
}

/// <summary>Fact that skips unless a TrinityCore database and a retail client are both available.</summary>
public sealed class TcRetailFactAttribute : FactAttribute
{
    public TcRetailFactAttribute()
    {
        Skip = Environments.TrinitySkip() ?? RetailEnvironment.Skip();
    }
}
