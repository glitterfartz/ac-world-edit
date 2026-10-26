using ACWorldEdit.Foundation;
using ACWorldEdit.Data;
using Xunit;

namespace ACWorldEdit.Tests.Data;

public class DatabaseSettingsTests
{
    [Fact]
    public void ParsesFiveFields()
    {
        var s = DatabaseSettings.Parse("db.example;3307;reader;s3cret;acore_world");
        Assert.Equal(new DatabaseSettings("db.example", 3307, "reader", "s3cret", "acore_world"), s);
        Assert.True(s.IsComplete);
    }

    [Theory]
    [InlineData("")]
    [InlineData("host;3306;user;pw")]
    [InlineData("host;3306;user;pw;db;extra")]
    [InlineData("host;notaport;user;pw;db")]
    [InlineData("host;-1;user;pw;db")]
    public void RejectsMalformed(string info) => Assert.Throws<InvalidDataException>(() => DatabaseSettings.Parse(info));

    [Fact]
    public void ToStringAndConnectionDescriptionNeverContainPassword()
    {
        var s = new DatabaseSettings("h", 3306, "u", "hunter2-secret", "d");
        Assert.DoesNotContain("hunter2-secret", s.ToString());
        Assert.DoesNotContain("hunter2-secret", DatabaseDiagnostics.Describe(new InvalidOperationException("hunter2-secret"), s));
        Assert.DoesNotContain("hunter2-secret", DatabaseDiagnostics.Describe(new TimeoutException(), s));
    }

    [Fact]
    public void ValidateNamesMissingFields()
    {
        Assert.Null(DatabaseDiagnostics.Validate(new("h", 3306, "u", "", "d")));
        Assert.Equal("Enter the host, user, database.", DatabaseDiagnostics.Validate(new("", 3306, "", "", "")));
        Assert.Equal("Enter the user.", DatabaseDiagnostics.Validate(new("h", 3306, "", "", "d")));
    }

    [Fact]
    public void ConnectionStringDisablesPoolingAndLocalInfile()
    {
        string cs = new DatabaseSettings("h", 3306, "u", "p", "d").BuildConnectionString();
        cs = cs.Replace(" ", "");
        Assert.Contains("Pooling=False", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AllowLoadLocalInfile=False", cs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AzerothCoreSourceKeyIsStableSoSavedDraftsStayValid()
    {
        var settings = new DatabaseSettings("db.example", 3307, "u", "p", "acore_world");
        Assert.Equal("EC41E454880D7A30DF5A345C3BF180E16C5690917781B8469061AAA42BD10168", new WorldDatabase(settings).SourceKey);
        Assert.Equal(new WorldDatabase(settings).SourceKey, new WorldDatabase(new WorldConnection(settings, null, WorldDialect.AzerothCore)).SourceKey);
    }

    [Fact]
    public void TrinityCoreSourceKeyNeverCollidesWithAzerothCore()
    {
        var settings = new DatabaseSettings("db.example", 3307, "u", "p", "acore_world");
        Assert.Equal("0E99AAEBC8EE481351BAB3B0ECD222AFEDB3D1A1AEABA9E8072F57CB4DD5F345", new WorldDatabase(new WorldConnection(settings, null, WorldDialect.TrinityCore)).SourceKey);
    }

    [Fact]
    public void OpeningATrinityCoreConnectionKeepsItsOwnSourceKey()
    {
        var connection = new WorldConnection(new DatabaseSettings("h", 3306, "u", "p", "world"), "hotfixes", WorldDialect.TrinityCore);
        Assert.Equal(new WorldDatabase(connection).SourceKey, DBSource.Open(connection).SourceKey);
    }

    [Fact]
    public async Task TheTrinityCoreEditorsThatAreNotBuiltYetSayWhatIsMissing()
    {
        var source = DBSource.Open(new WorldConnection(new DatabaseSettings("h", 3306, "u", "p", "world"), "hotfixes", WorldDialect.TrinityCore));
        var error = await Assert.ThrowsAsync<DatabaseFeatureException>(() => source.SearchItemDetailsAsync("x", CancellationToken.None));
        Assert.Contains("not available for the TrinityCore", error.Message);
    }

    [Fact]
    public void OpeningWithIncompleteSettingsSaysWhatToEnter() =>
        Assert.Throws<DatabaseFeatureException>(() => DBSource.Open(WorldConnection.Empty));

    [Fact]
    public async Task StatusTextKeepsFeatureMessagesAndHidesParserInternals()
    {
        var source = DBSource.Open(new WorldConnection(new DatabaseSettings("h", 3306, "u", "p", "world"), "hotfixes", WorldDialect.TrinityCore));
        var unsupported = await Assert.ThrowsAsync<DatabaseFeatureException>(() => source.ReadWaypointsAsync(new CreatureRegion(0, 31, 33, 48, 50), null, CancellationToken.None));
        Assert.Equal(unsupported.Message, DbErrors.Safe(unsupported));
        Assert.Equal("Enter the world database host, user and database name.", DbErrors.Safe(Record.Exception(() => DBSource.Open(WorldConnection.Empty))!));
        Assert.DoesNotContain("Missing column", DbErrors.Safe(new InvalidDataException("Missing column guid.")));
    }

    [Fact]
    public async Task TrinityCoreAreaTriggersWithoutClientDataSayWhy()
    {
        var source = DBSource.Open(new WorldConnection(new DatabaseSettings("h", 3306, "u", "p", "world"), "hotfixes", WorldDialect.TrinityCore));
        var error = await Assert.ThrowsAsync<DatabaseFeatureException>(() => source.ReadAreaTriggersAsync(new CreatureRegion(0, 31, 33, 48, 50), null, "", CancellationToken.None));
        Assert.Contains("retail client", DbErrors.Safe(error));
    }

    [Fact]
    public void DescribeNamesTheServerFlavourWhenTheSchemaDoesNotMatch()
    {
        var s = new DatabaseSettings("h", 3306, "u", "p", "world");
        var error = new SpawnSchemaException("Required database metadata unavailable: creature.guid.");
        Assert.Contains("not a TrinityCore world database", DatabaseDiagnostics.Describe(error, s, WorldDialect.TrinityCore));
        Assert.Contains("not an AzerothCore world database", DatabaseDiagnostics.Describe(error, s));
    }

    [Fact]
    public async Task TestAsyncRejectsIncompleteSettingsWithoutConnecting()
    {
        var empty = new WorldConnection(new DatabaseSettings("", 3306, "", "", ""), null, WorldDialect.TrinityCore);
        Assert.Equal("Enter the host, user, database.", await DatabaseDiagnostics.TestAsync(empty));
    }

    [Fact]
    public void ABlankHotfixesNameMeansNoHotfixesDatabase()
    {
        var blank = new WorldConnection(new DatabaseSettings("h", 3306, "u", "p", "world"), "  ", WorldDialect.TrinityCore);
        Assert.Null(blank.HotfixesSettings);
    }

    [Fact]
    public void SchemaTablesDifferPerDialect()
    {
        var ac = WorldReadSession.SchemaTables(WorldDialect.AzerothCore);
        var tc = WorldReadSession.SchemaTables(WorldDialect.TrinityCore);
        Assert.Contains("game_graveyard", ac);
        Assert.DoesNotContain("world_safe_locs", ac);
        Assert.Contains("world_safe_locs", tc);
        Assert.Contains("pool_members", tc);
        Assert.DoesNotContain("game_graveyard", tc);
        Assert.DoesNotContain("waypoint_data", tc);
        Assert.Equal(ac.Length, ac.Distinct().Count());
        Assert.Equal(tc.Length, tc.Distinct().Count());
    }

    [Fact]
    public void ParseAcceptsTheOptionalHotfixesPartForTrinityCoreOnly()
    {
        var tc = WorldConnection.Parse("h;3306;u;p;world;hot", WorldDialect.TrinityCore);
        Assert.Equal("hot", tc.Hotfixes);
        Assert.Equal("hotfixes", WorldConnection.Parse("h;3306;u;p;world", WorldDialect.TrinityCore).Hotfixes);
        Assert.Null(WorldConnection.Parse("h;3306;u;p;acore_world", WorldDialect.AzerothCore).Hotfixes);
        Assert.Throws<InvalidDataException>(() => WorldConnection.Parse("h;3306;u;p;acore_world;hot", WorldDialect.AzerothCore));
        Assert.Throws<InvalidDataException>(() => WorldConnection.Parse("h;3306;u;p;world;hot;extra", WorldDialect.TrinityCore));
    }

    [Fact]
    public void WorldDatabaseRefusesIncompleteSettings() => Assert.Throws<DatabaseFeatureException>(() => new WorldDatabase(DatabaseSettings.Empty));
}
