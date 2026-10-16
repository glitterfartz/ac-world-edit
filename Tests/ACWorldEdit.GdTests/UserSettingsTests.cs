using ACWorldEdit;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>Environment overrides of the per-user settings (read-only: the saved settings file is never written).</summary>
[TestSuite]
public class UserSettingsTests
{
    static void WithEnvironment(string name, string? value, Action body)
    {
        string? previous = System.Environment.GetEnvironmentVariable(name);
        System.Environment.SetEnvironmentVariable(name, value);
        try
        {
            body();
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(name, previous);
        }
    }

    [TestCase]
    public void DataDirectoryEnvironmentOverrideWins() =>
        WithEnvironment("ACWORLD_DATA_DIR", "client-data-dir", () => AssertString(UserSettings.LoadDataDirectory()).IsEqual("client-data-dir"));

    [TestCase]
    public void DatabaseEnvironmentOverrideIsParsed() =>
        WithEnvironment("ACWORLD_DB_INFO", "db.example;3307;reader;pw;acore_world", () =>
        {
            var settings = UserSettings.LoadDatabase();
            AssertString(settings.Host).IsEqual("db.example");
            AssertThat(settings.Port).IsEqual(3307u);
            AssertString(settings.User).IsEqual("reader");
            AssertString(settings.Database).IsEqual("acore_world");
        });
}
