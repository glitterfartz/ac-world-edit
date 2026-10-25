using Godot;
using ACWorldEdit.Data;

namespace ACWorldEdit;

/// <summary>
/// Host/port/user/password/database fields for the read-only world database. Apply tests a real
/// read-only session first; only a working connection is saved and announced through Applied.
/// </summary>
public sealed partial class ConnectionForm : VBoxContainer
{
    readonly LineEdit host = new() { PlaceholderText = "Host (e.g. 127.0.0.1)" };
    readonly SpinBox port = new() { MinValue = 1, MaxValue = 65535, Step = 1, Value = 3306 };
    readonly LineEdit user = new() { PlaceholderText = "User" };
    readonly LineEdit password = new() { PlaceholderText = "Password", Secret = true };
    readonly LineEdit database = new() { PlaceholderText = "Database (e.g. acore_world)" };
    readonly Label result = InspectorUi.Text("", 12);
    bool testing;

    /// <summary>Raised on the main thread after the settings connected successfully and were saved.</summary>
    public event Action? Applied;

    public ConnectionForm()
    {
        AddField("Host", host);
        AddField("Port", port);
        AddField("User", user);
        AddField("Password", password);
        AddField("Database", database);
        result.AutowrapMode = TextServer.AutowrapMode.WordSmart; AddChild(result);
        Settings = UserSettings.LoadDatabase();
    }

    public DatabaseSettings Settings
    {
        get => new(host.Text.Trim(), (uint)port.Value, user.Text.Trim(), password.Text, database.Text.Trim());
        set
        {
            host.Text = value.Host;
            port.Value = value.Port;
            user.Text = value.User;
            password.Text = value.Password;
            database.Text = value.Database;
        }
    }

    /// <summary>Test the entered settings; on success save them and raise Applied, otherwise show why not.</summary>
    public async Task<bool> ApplyAsync()
    {
        if (testing)
        {
            return false;
        }

        var settings = Settings;
        SetTesting(true);
        string? error = await Task.Run(() => DatabaseDiagnostics.TestAsync(settings));
        SetTesting(false);
        if (error != null)
        {
            Show(error, false);
            return false;
        }

        UserSettings.SaveDatabase(settings);
        Show($"Connected to {settings}.", true);
        Applied?.Invoke();
        return true;
    }

    /// <summary>Take settings that another form already tested and saved, then announce them.</summary>
    public void Adopt(DatabaseSettings settings)
    {
        Settings = settings;
        Show($"Connected to {settings}.", true);
        Applied?.Invoke();
    }

    void SetTesting(bool value)
    {
        testing = value;
        host.Editable = user.Editable = password.Editable = database.Editable = port.Editable = !value;
        if (value)
        {
            Show("Connecting…", true);
        }
    }

    void Show(string text, bool ok)
    {
        result.Text = text;
        result.AddThemeColorOverride("font_color", ok ? InspectorUi.Accent : new Color("ff8a80"));
    }

    void AddField(string label, Control field)
    {
        AddChild(InspectorUi.Text(label, 12, true));
        field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(field);
    }
}

/// <summary>
/// Modal prompt shown at startup (and from File → Database connection…) until a connection tests OK.
/// It edits its own form; the viewer copies the working settings into the shared one.
/// </summary>
public sealed partial class ConnectionDialog : AcceptDialog
{
    readonly ConnectionForm form = new();

    public event Action<DatabaseSettings>? Connected;

    public ConnectionDialog(DatabaseSettings initial)
    {
        Title = "Connect to the world database";
        OkButtonText = "Connect";
        DialogHideOnOk = false;
        form.Settings = initial;
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        // A wrapping label measured before it has a width asks for one line per word, which makes the
        // dialog as tall as the window; a minimum width keeps its height to the real line count.
        var intro = InspectorUi.Text("Enter your AzerothCore world database. The connection is read-only; nothing is written.", 12, true);
        intro.CustomMinimumSize = new Vector2(380, 0);
        box.AddChild(intro);
        box.AddChild(form);
        AddChild(box);
        Confirmed += async () =>
        {
            if (await form.ApplyAsync())
            {
                Connected?.Invoke(form.Settings);
                Hide();
            }
        };
    }
}
