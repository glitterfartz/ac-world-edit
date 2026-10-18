using ACWorldEdit;
using ACWorldEdit.Assets;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>
/// The real client picker dialog, headless, over synthetic folders: per-profile remembered paths,
/// folder validation gating the Load button, and the chosen profile/folder being reported.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ClientPickerDialogTests
{
    static string MakeFolder(string name, string file, out string root)
    {
        root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "acwe-picker-" + Guid.NewGuid().ToString("N"));
        string folder = System.IO.Path.Combine(root, name);
        System.IO.Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, file), "x");
        return folder;
    }

    static OptionButton Profile(ClientPickerDialog d) => (OptionButton)d.FindChild("Profile", true, false)!;

    static LineEdit Folder(ClientPickerDialog d) => (LineEdit)d.FindChild("Folder", true, false)!;

    [TestCase]
    public void ShowsTheRememberedFolderAndValidatesIt()
    {
        string wrath = MakeFolder("Data", "common.MPQ", out string rootA);
        string retail = MakeFolder("wow", ".build.info", out string rootB);
        try
        {
            var dialog = new ClientPickerDialog(ClientKind.Wrath335, kind => kind == ClientKind.Retail ? retail : wrath);
            AutoFree(dialog);
            AssertString(Folder(dialog).Text).IsEqual(wrath);
            AssertBool(dialog.GetOkButton().Disabled).IsFalse();

            Profile(dialog).Select(1);
            Profile(dialog).EmitSignal(OptionButton.SignalName.ItemSelected, 1);
            AssertString(Folder(dialog).Text).IsEqual(retail);
            AssertBool(dialog.GetOkButton().Disabled).IsFalse();
        }
        finally
        {
            System.IO.Directory.Delete(rootA, true);
            System.IO.Directory.Delete(rootB, true);
        }
    }

    [TestCase]
    public void LoadIsDisabledForTheWrongKindOfFolder()
    {
        string wrath = MakeFolder("Data", "common.MPQ", out string root);
        try
        {
            // A Wrath Data folder is not a retail install: switching profile with that path must block Load.
            var dialog = new ClientPickerDialog(ClientKind.Retail, _ => wrath);
            AutoFree(dialog);
            AssertBool(dialog.GetOkButton().Disabled).IsTrue();
            Folder(dialog).Text = "";
            Folder(dialog).EmitSignal(LineEdit.SignalName.TextChanged, "");
            AssertBool(dialog.GetOkButton().Disabled).IsTrue();
        }
        finally
        {
            System.IO.Directory.Delete(root, true);
        }
    }

    [TestCase]
    public void ConfirmingReportsTheProfileAndFolder()
    {
        string retail = MakeFolder("wow", ".build.info", out string root);
        try
        {
            var dialog = new ClientPickerDialog(ClientKind.Retail, _ => retail);
            AutoFree(dialog);
            (ClientKind Kind, string Path)? chosen = null;
            dialog.Chosen += (kind, path) => chosen = (kind, path);
            dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
            AssertThat(chosen).IsEqual((ClientKind.Retail, retail));
        }
        finally
        {
            System.IO.Directory.Delete(root, true);
        }
    }
}
