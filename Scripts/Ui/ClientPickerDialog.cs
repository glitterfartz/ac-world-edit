using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

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
