using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
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
        ("database.wrath", "host") => "saved-host",
        ("database.wrath", "port") => "3310",
        ("database.wrath", "user") => "saved-user",
        ("database.wrath", "password") => "saved-pw",
        ("database.wrath", "name") => "saved_db",
        _ => fallback
    };

    static string Nothing(string section, string key, string fallback) => fallback;

    [Fact]
    public void ValidDatabaseEnvironmentOverridesSaved()
    {
        var settings = SettingsResolver.Connection(WorldDialect.AzerothCore, "db.example;3307;reader;pw;acore_world", Saved).World;
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
        var settings = SettingsResolver.Connection(WorldDialect.AzerothCore, env, Saved).World;
        Assert.Equal(new DatabaseSettings("saved-host", 3310, "saved-user", "saved-pw", "saved_db"), settings);
    }

    [Fact]
    public void MissingSavedValuesUseTheDefaults()
    {
        Assert.Equal(DatabaseSettings.Empty, SettingsResolver.Connection(WorldDialect.AzerothCore, null, Nothing).World);
    }

    [Fact]
    public void AnUnparseablePortKeepsTheDefaultPort()
    {
        var settings = SettingsResolver.Connection(WorldDialect.AzerothCore, null, (s, k, f) => k == "port" ? "abc" : Saved(s, k, f)).World;
        Assert.Equal(DatabaseSettings.Empty.Port, settings.Port);
        Assert.Equal("saved-host", settings.Host);
    }

    [Fact]
    public void InvalidEnvironmentNeverLeaksIntoTheErrorPath()
    {
        // The invalid override is swallowed, not rethrown with its (possibly secret) text.
        var ex = Record.Exception(() => SettingsResolver.Connection(WorldDialect.AzerothCore, "secret-password-only", Saved));
        Assert.Null(ex);
    }

    static string Retail(string section, string key, string fallback) => (section, key) switch
    {
        ("database.retail", "host") => "tc-host",
        ("database.retail", "port") => "3311",
        ("database.retail", "user") => "tc-user",
        ("database.retail", "password") => "tc-pw",
        ("database.retail", "name") => "tc_world",
        ("database.retail", "hotfixes") => "tc_hotfixes",
        _ => fallback
    };

    [Fact]
    public void ProfilesSaveToSeparateSections()
    {
        Assert.Equal("database.wrath", SettingsResolver.DatabaseSection(WorldDialect.AzerothCore));
        Assert.Equal("database.retail", SettingsResolver.DatabaseSection(WorldDialect.TrinityCore));
        Assert.Equal("ACWORLD_TC_DB_INFO", SettingsResolver.DatabaseVariableFor(WorldDialect.TrinityCore));
        Assert.Equal("ACWORLD_DB_INFO", SettingsResolver.DatabaseVariableFor(WorldDialect.AzerothCore));
    }

    [Fact]
    public void RetailReadsItsOwnSectionIncludingHotfixes()
    {
        var connection = SettingsResolver.Connection(WorldDialect.TrinityCore, null, Retail);
        Assert.Equal(new DatabaseSettings("tc-host", 3311, "tc-user", "tc-pw", "tc_world"), connection.World);
        Assert.Equal("tc_hotfixes", connection.Hotfixes);
        Assert.Equal(WorldDialect.TrinityCore, connection.Dialect);
        Assert.Equal("saved-host", SettingsResolver.Connection(WorldDialect.AzerothCore, null, Saved).World.Host);
    }

    [Fact]
    public void RetailDefaultsToTheStockWorldAndHotfixesDatabases()
    {
        var connection = SettingsResolver.Connection(WorldDialect.TrinityCore, null, Nothing);
        Assert.Equal("world", connection.World.Database);
        Assert.Equal("hotfixes", connection.Hotfixes);
    }

    [Fact]
    public void TheProfilelessSectionIsNoLongerRead()
    {
        Func<string, string, string, string> legacyOnly = (s, k, f) => s == "database" ? "old-value" : f;
        Assert.Equal(DatabaseSettings.DefaultFor(WorldDialect.AzerothCore), SettingsResolver.Connection(WorldDialect.AzerothCore, null, legacyOnly).World);
        Assert.Equal(DatabaseSettings.DefaultFor(WorldDialect.TrinityCore), SettingsResolver.Connection(WorldDialect.TrinityCore, null, legacyOnly).World);
    }

    [Fact]
    public void LegacySettingsMoveIntoTheWrathProfile()
    {
        var legacy = new Dictionary<string, string> { ["host"] = "old-host", ["port"] = "3307", ["password"] = "pw" };
        var moves = SettingsResolver.LegacyDatabaseMigration(k => legacy.GetValueOrDefault(k), _ => false);
        Assert.Equal([("host", "old-host"), ("port", "3307"), ("password", "pw")], moves);
    }

    [Fact]
    public void MigrationKeepsWhatTheProfileAlreadyHas()
    {
        var legacy = new Dictionary<string, string> { ["host"] = "old-host", ["user"] = "old-user" };
        var moves = SettingsResolver.LegacyDatabaseMigration(k => legacy.GetValueOrDefault(k), k => k == "host");
        Assert.Equal([("user", "old-user")], moves);
        Assert.Empty(SettingsResolver.LegacyDatabaseMigration(_ => null, _ => false));
    }

    [Fact]
    public void TrinityEnvironmentAcceptsAnOptionalHotfixesPart()
    {
        var five = SettingsResolver.Connection(WorldDialect.TrinityCore, "h;3306;u;p;world", Nothing);
        Assert.Equal("hotfixes", five.Hotfixes);
        var six = SettingsResolver.Connection(WorldDialect.TrinityCore, "h;3306;u;p;world;tc_hot", Nothing);
        Assert.Equal(new DatabaseSettings("h", 3306, "u", "p", "world"), six.World);
        Assert.Equal("tc_hot", six.Hotfixes);
        Assert.Equal("hotfixes", SettingsResolver.Connection(WorldDialect.TrinityCore, "h;3306;u;p;world; ", Nothing).Hotfixes);
    }

    [Fact]
    public void AzerothCoreEnvironmentStillRejectsASixthPart()
    {
        var connection = SettingsResolver.Connection(WorldDialect.AzerothCore, "h;3306;u;p;acore_world;extra", Saved);
        Assert.Equal("saved-host", connection.World.Host);
    }

    [Fact]
    public void HotfixesSettingsReuseTheServerAndCredentials()
    {
        var connection = new WorldConnection(new DatabaseSettings("h", 3306, "u", "p", "world"), "hotfixes", WorldDialect.TrinityCore);
        Assert.Equal(new DatabaseSettings("h", 3306, "u", "p", "hotfixes"), connection.HotfixesSettings);
        Assert.Null(WorldConnection.Empty.HotfixesSettings);
    }

    [Fact]
    public void VariableNamesMatchTheDocumentedOnes()
    {
        Assert.Equal("ACWORLD_DATA_DIR", SettingsResolver.DataVariable);
        Assert.Equal("ACWORLD_DB_INFO", SettingsResolver.DatabaseVariable);
        Assert.Equal("ACWORLD_TC_DB_INFO", SettingsResolver.TrinityDatabaseVariable);
        Assert.Equal("ACWORLD_RETAIL_DIR", SettingsResolver.RetailVariable);
        Assert.Equal("ACWORLD_CLIENT_PROFILE", SettingsResolver.ProfileVariable);
    }
    [Theory]
    [InlineData("retail", ClientKind.Retail)]
    [InlineData(" Retail ", ClientKind.Retail)]
    [InlineData("RETAIL", ClientKind.Retail)]
    [InlineData("wrath", ClientKind.Wrath335)]
    [InlineData("", ClientKind.Wrath335)]
    [InlineData("garbage", ClientKind.Wrath335)]
    [InlineData(null, ClientKind.Wrath335)]
    public void ProfileNamesParseAndDamagedValuesFallBackToWrath(string? value, ClientKind expected)
    {
        Assert.Equal(expected, SettingsResolver.ParseProfile(value));
    }

    [Fact]
    public void ProfileEnvironmentWinsOverSavedAndIsRoundTripped()
    {
        bool read = false;
        Assert.Equal(ClientKind.Retail, SettingsResolver.Profile("retail", () =>
        {
            read = true;
            return "wrath";
        }));
        Assert.False(read);
        Assert.Equal(ClientKind.Retail, SettingsResolver.Profile(null, () => "retail"));
        Assert.Equal(ClientKind.Wrath335, SettingsResolver.Profile("  ", () => ""));
        Assert.Equal("retail", SettingsResolver.ProfileName(ClientKind.Retail));
        Assert.Equal("wrath", SettingsResolver.ProfileName(ClientKind.Wrath335));
        Assert.Equal(ClientKind.Retail, SettingsResolver.ParseProfile(SettingsResolver.ProfileName(ClientKind.Retail)));
    }

    [Fact]
    public void RetailDirectoryFollowsTheSameEnvironmentRule()
    {
        Assert.Equal("env", SettingsResolver.RetailDirectory("env", () => "saved"));
        Assert.Equal("saved", SettingsResolver.RetailDirectory(" ", () => "saved"));
    }
}
