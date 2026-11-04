using Godot;
using ACWorldEdit.Assets;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Client profile prompts and the dialogs that change the session: choosing a client folder,
/// switching the draft profile, the startup checks, the connection prompt and the info box.
/// </summary>
public partial class ViewerShell
{
    Label clientLabel = null!;

    /// <summary>
    /// Pick the client profile and folder; loading reuses the normal client lifecycle and
    /// cancellation. Each profile remembers its own folder, and the chosen profile is remembered too.
    /// </summary>
    void ChooseClient()
    {
        var dialog = new ClientPickerDialog(profile.Kind, kind => kind == ClientKind.Retail ? retailDirectory : wrathDirectory);
        dialog.Chosen += async (kind, directory) =>
        {
            if (kind != profile.Kind)
            {
                SwitchDraftProfile(kind);
            }

            profile = ClientProfile.For(kind);
            dataDirectory = directory;
            UserSettings.SaveProfile(kind);
            connection.Dialect = profile.Dialect;
            if (kind == ClientKind.Retail)
            {
                UserSettings.SaveRetailDirectory(directory);
            }
            else
            {
                UserSettings.SaveDataDirectory(directory);
            }

            WarnMissingDependencies(kind);
            await LoadClient();
        };
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>
    /// Say in the status bar what a profile needs that this install lacks (CascLib.dll, retail table
    /// definitions), before the client load fails on it.
    /// </summary>
    void WarnMissingDependencies(ClientKind kind)
    {
        foreach (string problem in DependencyCheck.Missing(kind, System.Environment.GetEnvironmentVariable("ACWORLD_CASCLIB"), System.Environment.GetEnvironmentVariable("ACWORLD_DBD_DIR"), AppContext.BaseDirectory))
        {
            statusLog.Post($"Retail unavailable: {problem}");
        }
    }

    /// <summary>
    /// Each client profile keeps its own draft files, so switching profile closes this profile's
    /// drafts (they stay on disk) and opens the other's.
    /// </summary>
    void SwitchDraftProfile(ClientKind kind)
    {
        ContentDraftFiles.UseProfile(kind);
        ContentDraftFiles.Rebind(drafts);
        graveyards.ReopenDraft();
        waypoints.ReopenDraft();
        pendingStore = null!;
        RefreshPending();
    }

    /// <summary>
    /// Reflect the opened client in the UI: the title line, whether database layers and editors are
    /// available, and the teleport list (which reads the database).
    /// </summary>
    void ApplyClientProfile(string description)
    {
        clientLabel.Text = "WORLD VIEWER  /  " + description.Split(" · ")[0];
        sidebar.SetDatabaseAvailable(databaseEnabled);
        sidebar.SetDialect(profile.Dialect);
        _ = LoadTeleports();
    }

    void ShowInfo(string title, string text) => Dialogs.Info(this, title, text);

    /// <summary>
    /// Interactive launches only: verify the saved database settings with a real read-only session and
    /// ask for working ones if they fail, then ask for the client Data folder if none is remembered.
    /// Smoke runs are non-interactive and skip both prompts.
    /// </summary>
    async Task StartupPrompts()
    {
        if (databaseEnabled && !anySmoke)
        {
            var saved = connection.Connection;
            string? error = await Task.Run(() => ACWorldEdit.Data.DatabaseDiagnostics.TestAsync(saved));
            if (error != null)
            {
                await PromptConnection();
            }
        }

        if (dataDirectory.Length == 0 && !IsQueuedForDeletion())
        {
            ChooseClient();
        }
    }

    /// <summary>
    /// Show the connection dialog and return when it closes. Settings are adopted only after they
    /// connect, so cancelling never replaces a working connection.
    /// </summary>
    Task PromptConnection()
    {
        var closed = new TaskCompletionSource();
        var dialog = new ConnectionDialog(connection.Connection);
        dialog.Connected += connection.Adopt;
        dialog.VisibilityChanged += () =>
        {
            if (!dialog.Visible)
            {
                closed.TrySetResult();
                dialog.QueueFree();
            }
        };
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(420, 0));
        // The first layout pass measures wrapping labels at zero width, and a window never shrinks
        // by itself, so refit it once the labels have their real width.
        Callable.From(() =>
        {
            dialog.ResetSize();
            dialog.MoveToCenter();
        }).CallDeferred();
        return closed.Task;
    }
}
