using ACWorldEdit;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// Headless coverage for the change-history fields on the connection form: the always-visible
/// database name, the different-server override, and round-tripping both. Never presses Apply,
/// so no connection is made and no settings are saved.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HistoryConnectionFormTests
{
    static void WithDatabaseInfo(string? info, Action body)
    {
        string? previous = System.Environment.GetEnvironmentVariable("ACWORLD_DB_INFO");
        string? previousHistory = System.Environment.GetEnvironmentVariable("ACWORLD_ACWORLD_INFO");
        string? previousTcHistory = System.Environment.GetEnvironmentVariable("ACWORLD_TC_ACWORLD_INFO");
        System.Environment.SetEnvironmentVariable("ACWORLD_DB_INFO", info);
        System.Environment.SetEnvironmentVariable("ACWORLD_ACWORLD_INFO", null);
        System.Environment.SetEnvironmentVariable("ACWORLD_TC_ACWORLD_INFO", null);
        try
        {
            body();
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("ACWORLD_DB_INFO", previous);
            System.Environment.SetEnvironmentVariable("ACWORLD_ACWORLD_INFO", previousHistory);
            System.Environment.SetEnvironmentVariable("ACWORLD_TC_ACWORLD_INFO", previousTcHistory);
        }
    }

    static DatabaseSettings World() => new("db.example", 3307, "reader", "pw", "acore_world");

    [TestCase]
    public void DefaultFormHasNoOverrideAndAcworldName() =>
        WithDatabaseInfo("db.example;3307;reader;pw;acore_world", () =>
        {
            var form = AutoFree(new ConnectionForm())!;
            AssertThat(form.Connection.AcworldOverride).IsNull();
            AssertString(form.Connection.Acworld.Database).IsEqual("acworld");
            AssertString(form.Connection.Acworld.Host).IsEqual("db.example");
        });

    [TestCase]
    public void CustomNameOnlyRoundTripsWithoutCustomServer()
    {
        var form = AutoFree(new ConnectionForm(WorldDialect.AzerothCore))!;
        form.Connection = new WorldConnection(World(), null, WorldDialect.AzerothCore, World() with { Database = "my_hist" });
        var roundTripped = form.Connection.AcworldOverride;
        AssertThat(roundTripped).IsNotNull();
        AssertString(roundTripped!.Database).IsEqual("my_hist");
        AssertString(roundTripped.Host).IsEqual(World().Host);
        AssertThat(roundTripped.Port).IsEqual(World().Port);
        AssertString(form.Connection.Acworld.Database).IsEqual("my_hist");
    }

    [TestCase]
    public void FullServerOverrideRoundTrips()
    {
        var custom = new DatabaseSettings("hist.example", 3310, "huser", "hpw", "hist_db");
        var form = AutoFree(new ConnectionForm(WorldDialect.AzerothCore))!;
        form.Connection = new WorldConnection(World(), null, WorldDialect.AzerothCore, custom);
        AssertThat(form.Connection.AcworldOverride).IsEqual(custom);
    }

    [TestCase]
    public void NullOverrideRestoresDerivedDefaults()
    {
        var form = AutoFree(new ConnectionForm(WorldDialect.AzerothCore))!;
        form.Connection = new WorldConnection(World(), null, WorldDialect.AzerothCore, new DatabaseSettings("h", 3306, "u", "p", "d"));
        form.Connection = new WorldConnection(World(), null, WorldDialect.AzerothCore, null);
        AssertThat(form.Connection.AcworldOverride).IsNull();
        AssertString(form.Connection.Acworld.Database).IsEqual("acworld");
    }
}
