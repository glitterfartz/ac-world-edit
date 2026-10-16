using ACWorldEdit.Data;
using Xunit;

/// <summary>
/// Environment-versus-saved precedence for the per-user settings. Values are fake; nothing reads or
/// writes a real profile or environment variable.
/// </summary>
public class SettingsResolverTests
{
    [Fact]
    public void DataDirectoryEnvironmentValueWinsAndSavedIsNotRead()
    {
        bool read = false;
        string result = SettingsResolver.DataDirectory("env-dir", () =>
        {
            read = true;
            return "saved";
        });
        Assert.Equal("env-dir", result);
        Assert.False(read);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankEnvironmentFallsBackToTheSavedDirectory(string? env)
    {
        Assert.Equal("saved", SettingsResolver.DataDirectory(env, () => "saved"));
    }

    static string Saved(string section, string key, string fallback) => (section, key) switch
    {
        ("database", "host") => "saved-host",
        ("database", "port") => "3310",
        ("database", "user") => "saved-user",
        ("database", "password") => "saved-pw",
        ("database", "name") => "saved_db",
        _ => fallback
    };

    static string Nothing(string section, string key, string fallback) => fallback;

    [Fact]
    public void ValidDatabaseEnvironmentOverridesSaved()
    {
        var settings = SettingsResolver.Database("db.example;3307;reader;pw;acore_world", Saved);
        Assert.Equal(new DatabaseSettings("db.example", 3307, "reader", "pw", "acore_world"), settings);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not;enough")]
    [InlineData("h;notaport;u;p;d")]
    public void BlankOrInvalidDatabaseEnvironmentFallsBackToSaved(string? env)
    {
        var settings = SettingsResolver.Database(env, Saved);
        Assert.Equal(new DatabaseSettings("saved-host", 3310, "saved-user", "saved-pw", "saved_db"), settings);
    }

    [Fact]
    public void MissingSavedValuesUseTheDefaults()
    {
        Assert.Equal(DatabaseSettings.Empty, SettingsResolver.Database(null, Nothing));
    }

    [Fact]
    public void AnUnparseablePortKeepsTheDefaultPort()
    {
        var settings = SettingsResolver.Database(null, (s, k, f) => k == "port" ? "abc" : Saved(s, k, f));
        Assert.Equal(DatabaseSettings.Empty.Port, settings.Port);
        Assert.Equal("saved-host", settings.Host);
    }

    [Fact]
    public void InvalidEnvironmentNeverLeaksIntoTheErrorPath()
    {
        // The invalid override is swallowed, not rethrown with its (possibly secret) text.
        var ex = Record.Exception(() => SettingsResolver.Database("secret-password-only", Saved));
        Assert.Null(ex);
    }

    [Fact]
    public void VariableNamesMatchTheDocumentedOnes()
    {
        Assert.Equal("ACWORLD_DATA_DIR", SettingsResolver.DataVariable);
        Assert.Equal("ACWORLD_DB_INFO", SettingsResolver.DatabaseVariable);
    }
}
