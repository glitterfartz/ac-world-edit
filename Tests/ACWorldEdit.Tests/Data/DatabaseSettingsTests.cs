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
    public void OpeningAnUnsupportedDialectFailsWithAnInvalidData() =>
        Assert.Throws<InvalidDataException>(() => DBSource.Open(new WorldConnection(new DatabaseSettings("h", 3306, "u", "p", "world"), null, WorldDialect.TrinityCore)));

    [Fact]
    public void OpeningWithIncompleteSettingsFailsWithAnInvalidData() =>
        Assert.Throws<InvalidDataException>(() => DBSource.Open(WorldConnection.Empty));

    [Fact]
    public void WorldDatabaseRefusesIncompleteSettings() => Assert.Throws<InvalidDataException>(() => new WorldDatabase(DatabaseSettings.Empty));
}
