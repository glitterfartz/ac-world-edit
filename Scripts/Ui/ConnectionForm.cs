using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Host/port/user/password/database fields for the read-only world database, plus the hotfixes
/// database for TrinityCore. Apply tests a real read-only session first; only a working connection is
/// saved (per client profile) and announced through Applied.
/// </summary>
public sealed partial class ConnectionForm : VBoxContainer
{
    readonly LineEdit host = new() { PlaceholderText = "Host (e.g. 127.0.0.1)" };
    readonly SpinBox port = new() { MinValue = 1, MaxValue = 65535, Step = 1, Value = 3306 };
    readonly LineEdit user = new() { PlaceholderText = "User" };
    readonly LineEdit password = new() { PlaceholderText = "Password", Secret = true };
    readonly LineEdit database = new() { PlaceholderText = "Database (e.g. acore_world)" };
    readonly LineEdit hotfixes = new() { PlaceholderText = "Hotfixes database (optional, e.g. hotfixes)" };
    readonly List<Control> hotfixRows = [];
    readonly Label result = InspectorUi.Text("", 12);
    bool testing;
    WorldDialect dialect;

    /// <summary>Raised on the main thread after the settings connected successfully and were saved.</summary>
    public event Action? Applied;

    public ConnectionForm(WorldDialect dialect = WorldDialect.AzerothCore)
    {
        AddField("Host", host);
        AddField("Port", port);
        AddField("User", user);
        AddField("Password", password);
        AddField("Database", database);
        hotfixRows.Add(AddField("Hotfixes database", hotfixes));
        result.AutowrapMode = TextServer.AutowrapMode.WordSmart; AddChild(result);
        Dialect = dialect;
    }

    /// <summary>
    /// The server flavour being edited. Changing it loads that profile's saved settings and shows the
    /// hotfixes field for TrinityCore only.
    /// </summary>
    public WorldDialect Dialect
    {
        get => dialect;
        set
        {
            dialect = value;
            foreach (var row in hotfixRows)
            {
                row.Visible = value == WorldDialect.TrinityCore;
            }

            Connection = UserSettings.LoadDatabase(value);
        }
    }

    public WorldConnection Connection
    {
        get => new(Settings, dialect == WorldDialect.TrinityCore ? hotfixes.Text.Trim() : null, dialect);
        set
        {
            Settings = value.World;
            hotfixes.Text = value.Hotfixes ?? WorldConnection.DefaultHotfixes;
        }
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

        var connection = Connection;
        SetTesting(true);
        string? error = await Task.Run(() => DatabaseDiagnostics.TestAsync(connection));
        SetTesting(false);
        if (error != null)
        {
            Show(error, false);
            return false;
        }

        UserSettings.SaveDatabase(connection);
        Show($"Connected to {connection.World}.", true);
        Applied?.Invoke();
        return true;
    }

    /// <summary>Take settings that another form already tested and saved, then announce them.</summary>
    public void Adopt(WorldConnection tested)
    {
        Connection = tested;
        Show($"Connected to {tested.World}.", true);
        Applied?.Invoke();
    }

    void SetTesting(bool value)
    {
        testing = value;
        host.Editable = user.Editable = password.Editable = database.Editable = hotfixes.Editable = port.Editable = !value;
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

    /// <summary>Add a labelled field; the returned container holds both so a row can be hidden as one.</summary>
    Control AddField(string label, Control field)
    {
        var row = new VBoxContainer();
        row.AddChild(InspectorUi.Text(label, 12, true));
        field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(field);
        AddChild(row);
        return row;
    }
}

/// <summary>
/// Modal prompt shown at startup (and from File → Database connection…) until a connection tests OK.
/// It edits its own form; the viewer copies the working settings into the shared one.
/// </summary>
public sealed partial class ConnectionDialog : AcceptDialog
{
    readonly ConnectionForm form;

    public event Action<WorldConnection>? Connected;

    public ConnectionDialog(WorldConnection initial)
    {
        Title = "Connect to the world database";
        OkButtonText = "Connect";
        DialogHideOnOk = false;
        form = new(initial.Dialect) { Connection = initial };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        // A wrapping label measured before it has a width asks for one line per word, which makes the
        // dialog as tall as the window; a minimum width keeps its height to the real line count.
        var intro = InspectorUi.Text($"Enter your {(initial.Dialect == WorldDialect.TrinityCore ? "TrinityCore" : "AzerothCore")} world database. The connection is read-only; nothing is written.", 12, true);
        intro.CustomMinimumSize = new Vector2(380, 0);
        box.AddChild(intro);
        box.AddChild(form);
        AddChild(box);
        Confirmed += async () =>
        {
            if (await form.ApplyAsync())
            {
                Connected?.Invoke(form.Connection);
                Hide();
            }
        };
    }
}


/// <summary>
/// Choose the client to open: a profile (Wrath 3.3.5 MPQs or Retail CASC) and its folder. Each
/// profile remembers its own folder, shown when the profile is selected. Load stays disabled until
/// the folder looks like the selected client (see ClientFolder), so a wrong pick is explained here
/// rather than as an archive error. Choosing never opens or writes anything itself.
/// </summary>
public sealed partial class ClientPickerDialog : ConfirmationDialog
{
    readonly OptionButton profile = new() { Name = "Profile" };
    readonly LineEdit path = new() { Name = "Folder", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, PlaceholderText = "Client folder" };
    readonly Label problem = InspectorUi.Text("", 12, true);
    readonly Func<ClientKind, string> remembered;

    /// <summary>Raised once, on Load, with the profile and the folder as entered.</summary>
    public event Action<ClientKind, string>? Chosen;

    public ClientPickerDialog(ClientKind initial, Func<ClientKind, string> remembered)
    {
        this.remembered = remembered;
        Title = "Load client";
        OkButtonText = "Load";
        Exclusive = true;
        profile.AddItem("Wrath 3.3.5 (MPQ Data folder)", (int)ClientKind.Wrath335);
        profile.AddItem("Retail (World of Warcraft install folder)", (int)ClientKind.Retail);
        profile.Select(initial == ClientKind.Retail ? 1 : 0);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(new Label { Text = "Client" });
        box.AddChild(profile);
        box.AddChild(new Label { Text = "Folder" });
        var row = new HBoxContainer();
        row.AddChild(path);
        var browse = new Button { Text = "Browse…" };
        row.AddChild(browse);
        box.AddChild(row);
        problem.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(problem);
        AddChild(box);

        profile.ItemSelected += _ => ShowRemembered();
        path.TextChanged += _ => Validate();
        browse.Pressed += Browse;
        Confirmed += () => Chosen?.Invoke(Selected, path.Text.Trim());
        ShowRemembered();
    }

    ClientKind Selected => profile.GetSelectedId() == (int)ClientKind.Retail ? ClientKind.Retail : ClientKind.Wrath335;

    void ShowRemembered()
    {
        path.Text = remembered(Selected);
        Validate();
    }

    void Validate()
    {
        string? error = ClientFolder.Validate(Selected, path.Text.Trim());
        problem.Text = error ?? "";
        GetOkButton().Disabled = error != null;
    }

    void Browse()
    {
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = Selected == ClientKind.Retail ? "Choose the World of Warcraft install folder" : "Choose the client Data folder (MPQs)",
            UseNativeDialog = true
        };
        string current = path.Text.Trim();
        dialog.CurrentDir = current.Length > 0 && System.IO.Directory.Exists(current) ? current : System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        dialog.DirSelected += directory =>
        {
            path.Text = directory;
            Validate();
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(760, 520));
    }
}
