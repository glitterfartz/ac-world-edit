using System.Diagnostics;
using System.Text;
using ACWorldEdit.Data;
using Xunit;

namespace ACWorldEdit.Tests;

public class UserDataResetTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "acw-reset-" + Guid.NewGuid().ToString("N"));

    public UserDataResetTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, true);

    string UserFolder(string appName = "ACWorldEdit", string parent = "app_userdata")
    {
        string folder = Path.Combine(root, "Godot", parent, appName);
        Directory.CreateDirectory(folder);
        return folder;
    }

    [Fact]
    public void AcceptsOnlyTheAppsOwnUserFolder()
    {
        Assert.True(UserDataReset.IsSafeTarget(UserFolder(), "ACWorldEdit"));
        Assert.False(UserDataReset.IsSafeTarget(UserFolder("Other"), "ACWorldEdit"));
        Assert.False(UserDataReset.IsSafeTarget(UserFolder("ACWorldEdit", "elsewhere"), "ACWorldEdit"));
        Assert.False(UserDataReset.IsSafeTarget(Path.GetPathRoot(root), "ACWorldEdit"));
        Assert.False(UserDataReset.IsSafeTarget(Path.Combine("Godot", "app_userdata", "ACWorldEdit"), "ACWorldEdit"));
        Assert.False(UserDataReset.IsSafeTarget("", "ACWorldEdit"));
        Assert.False(UserDataReset.IsSafeTarget(null, "ACWorldEdit"));
    }

    [Fact]
    public void CountsExportedScriptsAcrossProfiles()
    {
        string folder = UserFolder();
        Directory.CreateDirectory(Path.Combine(folder, "drafts", "wrath", "exports", "20260101-000000"));
        Directory.CreateDirectory(Path.Combine(folder, "drafts", "retail", "exports", "20260102-000000"));
        File.WriteAllText(Path.Combine(folder, "drafts", "wrath", "exports", "20260101-000000", "a-apply.sql"), "");
        File.WriteAllText(Path.Combine(folder, "drafts", "wrath", "exports", "20260101-000000", "a-revert.sql"), "");
        File.WriteAllText(Path.Combine(folder, "drafts", "retail", "exports", "20260102-000000", "b-apply.sql"), "");
        File.WriteAllText(Path.Combine(folder, "drafts", "wrath", "interactions.json"), "{}");

        Assert.Equal(3, UserDataReset.CountExportedScripts(folder));
        Assert.Equal(0, UserDataReset.CountExportedScripts(Path.Combine(root, "missing")));
    }

    [Fact]
    public void CleanerScriptCarriesTheProcessIdAndEscapesQuotes()
    {
        string script = UserDataReset.CleanerScript(@"C:\Users\O'Brien\AppData\Roaming\Godot\app_userdata\ACWorldEdit", 4242);

        Assert.Contains("Wait-Process -Id 4242", script);
        Assert.Contains(@"'C:\Users\O''Brien\AppData\Roaming\Godot\app_userdata\ACWorldEdit'", script);
    }

    [Fact]
    public void LauncherCreatesTheCleanerThroughWmiSoItInheritsNoHandles()
    {
        var (file, arguments) = UserDataReset.LauncherCommand(@"C:\Users\O'Brien\x\app_userdata\ACWorldEdit", 4242);

        Assert.EndsWith("powershell.exe", file);
        Assert.Equal("-EncodedCommand", arguments[^2]);
        string script = Encoding.Unicode.GetString(Convert.FromBase64String(arguments[^1]));
        Assert.Contains("Win32_Process", script);
        Assert.Contains("Create", script);
        Assert.Contains("-EncodedCommand", script);
    }

    [Fact]
    public void TryDeleteDirectoryRemovesNestedFilesAndTreatsMissingAsGone()
    {
        string folder = Path.Combine(root, "thumbs");
        Directory.CreateDirectory(Path.Combine(folder, "wrath"));
        File.WriteAllText(Path.Combine(folder, "wrath", "a.png"), "x");

        Assert.True(UserDataReset.TryDeleteDirectory(folder));
        Assert.False(Directory.Exists(folder));
        Assert.True(UserDataReset.TryDeleteDirectory(folder));
    }

    [Fact]
    public async Task CleanerWaitsForTheWatchedProcessAndIsNotHeldUpByAHandleItWouldInherit()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = UserFolder();
        Directory.CreateDirectory(Path.Combine(folder, "logs"));
        File.WriteAllText(Path.Combine(folder, "settings.cfg"), "x");
        File.WriteAllText(Path.Combine(folder, UserDataReset.CancelMarker), "stale marker from an earlier failed reset");
        // The viewer's log is an inheritable handle. A cleaner started as our own child would inherit
        // it and block its own deletion, which is the failure this guards against.
        var log = new FileStream(Path.Combine(folder, "logs", "godot.log"), FileMode.Create, FileAccess.ReadWrite, FileShare.Read | FileShare.Inheritable);
        using var watched = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 4 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;

        bool started = await UserDataReset.StartCleanerAsync(folder, watched.Id);

        Assert.True(started);
        Assert.True(Directory.Exists(folder), "the cleaner must wait for the watched process");
        log.Dispose();
        watched.WaitForExit();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Directory.Exists(folder) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
        }

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task CleanerLeavesTheFolderAloneOnceCancelled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string folder = UserFolder();
        File.WriteAllText(Path.Combine(folder, "settings.cfg"), "x");
        using var watched = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;

        Assert.True(await UserDataReset.StartCleanerAsync(folder, watched.Id));
        UserDataReset.Cancel(folder);
        watched.WaitForExit();
        await Task.Delay(6000);

        Assert.True(File.Exists(Path.Combine(folder, "settings.cfg")));
    }

    [Fact]
    public async Task StartCleanerReportsFailureOffWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.False(await UserDataReset.StartCleanerAsync(UserFolder(), Environment.ProcessId));
    }
}
