using System.Diagnostics;
using System.Text;

namespace ACWorldEdit.Data;

/// <summary>
/// Everything behind File → Reset configs and delete cache…: deciding that a folder is safe to
/// delete, counting what the user would lose, and starting a detached helper that removes the
/// whole user data folder after the viewer has exited. Deleting after exit is the only way to
/// remove files the running viewer holds open (the log, draft lock files, the engine's pipeline
/// cache written at exit). Godot-free so the xUnit project compiles it.
/// </summary>
public static class UserDataReset
{
    /// <summary>
    /// Whether the folder is the app's own Godot user folder: absolute, not a drive root, named
    /// after the project and sitting in an <c>app_userdata</c> folder. Anything else (a custom
    /// user directory, an empty or relative path) is refused so the delete can never widen.
    /// </summary>
    public static bool IsSafeTarget(string? directory, string appName)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(appName) || !Path.IsPathFullyQualified(directory))
        {
            return false;
        }

        var folder = new DirectoryInfo(Path.GetFullPath(directory));
        return folder.Parent != null
            && string.Equals(folder.Name, appName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(folder.Parent.Name, "app_userdata", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Number of exported SQL scripts under <c>drafts/*/exports</c>, the loss most worth warning about.</summary>
    public static int CountExportedScripts(string directory)
    {
        string drafts = Path.Combine(directory, "drafts");
        if (!Directory.Exists(drafts))
        {
            return 0;
        }

        return Directory.GetDirectories(drafts)
            .Select(profile => Path.Combine(profile, "exports"))
            .Where(Directory.Exists)
            .Sum(exports => Directory.EnumerateFiles(exports, "*.sql", SearchOption.AllDirectories).Count());
    }

    /// <summary>
    /// File the cleaner looks for inside the folder before deleting. It is written when the launch
    /// failed or timed out, because the cleaner may exist anyway and would otherwise delete the data
    /// when the viewer is next closed normally.
    /// </summary>
    public const string CancelMarker = ".reset-cancel";

    /// <summary>Tell any cleaner already started for this folder not to delete it. Best effort.</summary>
    public static void Cancel(string directory)
    {
        try
        {
            File.WriteAllText(Path.Combine(directory, CancelMarker), "cancelled");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The folder is unwritable; the cleaner cannot be told, but nothing else can be done.
        }
    }

    /// <summary>
    /// The cleanup script: wait for the process to exit, then remove the folder, retrying for up to a
    /// minute because a file can stay locked for a moment after exit. Quotes in the path are doubled
    /// for the single-quoted PowerShell literals.
    /// </summary>
    public static string CleanerScript(string directory, int processId)
    {
        string quoted = directory.Replace("'", "''");
        string marker = Path.Combine(directory, CancelMarker).Replace("'", "''");
        return $"Wait-Process -Id {processId} -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1; " +
            $"if (Test-Path -LiteralPath '{marker}') {{ exit }}; " +
            $"for ($i = 0; $i -lt 120 -and (Test-Path -LiteralPath '{quoted}'); $i++) {{ " +
            $"Remove-Item -LiteralPath '{quoted}' -Recurse -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 500 }}";
    }

    static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    static string PowerShellPath => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    static string[] PowerShellArguments(string script) =>
        ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-EncodedCommand", Encode(script)];

    /// <summary>
    /// The launcher's command line. The cleaner is created through WMI (Win32_Process.Create), not as
    /// our own child: a child inherits the viewer's inheritable handles, including its open log file,
    /// and would then hold that file open itself while waiting for it to be deletable. The launcher
    /// inherits them instead and exits as soon as the cleaner exists; its exit code is the WMI result.
    /// </summary>
    public static (string File, string[] Arguments) LauncherCommand(string directory, int processId)
    {
        string cleaner = $"\"{PowerShellPath}\" {string.Join(' ', PowerShellArguments(CleanerScript(directory, processId)))}";
        string script =
            $"$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{{ CommandLine = '{cleaner.Replace("'", "''")}' }}; " +
            "exit ([int]$r.ReturnValue)";
        return (PowerShellPath, PowerShellArguments(script));
    }

    /// <summary>
    /// Start the detached cleaner for <paramref name="processId"/>. Returns false when the launcher
    /// could not start, failed, or took too long, so the caller keeps the viewer open; the folder is
    /// then marked so a cleaner that did get created stands down.
    /// </summary>
    public static async Task<bool> StartCleanerAsync(string directory, int processId, int timeoutMilliseconds = 20000)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            File.Delete(Path.Combine(directory, CancelMarker));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        bool started = await LaunchCleanerAsync(directory, processId, timeoutMilliseconds);
        if (!started)
        {
            Cancel(directory);
        }

        return started;
    }

    static async Task<bool> LaunchCleanerAsync(string directory, int processId, int timeoutMilliseconds)
    {
        var (file, arguments) = LauncherCommand(directory, processId);
        var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var launcher = Process.Start(info);
            if (launcher == null)
            {
                return false;
            }

            using var timeout = new CancellationTokenSource(timeoutMilliseconds);
            try
            {
                await launcher.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                launcher.Kill();
                return false;
            }

            return launcher.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Delete a folder and everything in it, skipping files that are locked. Returns whether the
    /// folder is gone; a missing folder counts as gone.
    /// </summary>
    public static bool TryDeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return true;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Held open by a running capture; the folder simply stays until the next clear.
            }
        }

        try
        {
            Directory.Delete(directory, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
