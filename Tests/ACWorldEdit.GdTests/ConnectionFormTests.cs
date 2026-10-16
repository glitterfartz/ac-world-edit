using ACWorldEdit;
using ACWorldEdit.Data;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// Headless instantiation of the real ConnectionForm control. These tests only read and set fields;
/// they never press Apply, so no connection is made and no settings are saved.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ConnectionFormTests
{
    static void WithDatabaseInfo(string? info, Action body)
    {
        string? previous = System.Environment.GetEnvironmentVariable("ACWORLD_DB_INFO");
        System.Environment.SetEnvironmentVariable("ACWORLD_DB_INFO", info);
        try
        {
            body();
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("ACWORLD_DB_INFO", previous);
        }
    }

    [TestCase]
    public void FormLoadsEnvironmentSettingsAndRoundTripsEdits() =>
        WithDatabaseInfo("db.example;3307;reader;pw;acore_world", () =>
        {
            var form = AutoFree(new ConnectionForm())!;
            AssertThat(form.Settings).IsEqual(new DatabaseSettings("db.example", 3307, "reader", "pw", "acore_world"));
            form.Settings = new DatabaseSettings("other", 3310, "u", "p", "d");
            AssertThat(form.Settings).IsEqual(new DatabaseSettings("other", 3310, "u", "p", "d"));
        });

    [TestCase]
    public void PasswordFieldIsMasked() =>
        WithDatabaseInfo("h;3306;u;secret;d", () =>
        {
            var form = AutoFree(new ConnectionForm())!;
            var masked = new List<LineEdit>();
            void Walk(Node node)
            {
                foreach (var child in node.GetChildren())
                {
                    if (child is LineEdit edit && edit.Secret)
                    {
                        masked.Add(edit);
                    }

                    Walk(child);
                }
            }

            Walk(form);
            AssertThat(masked.Count).IsEqual(1);
            AssertString(masked[0].Text).IsEqual("secret");
        });
}
