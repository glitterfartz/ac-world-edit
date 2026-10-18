using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Listfile download UI and logic. The community listfile maps FileDataIDs to paths, which the
/// retail client needs for files named by path (garrison interiors, WMO and liquid textures).
/// </summary>
public partial class TerrainViewer
{
    CancellationTokenSource? listfileDownload;

    /// <summary>
    /// First retail load without a listfile: offer the download once. Declining is remembered; File →
    /// Update listfile… stays available. Non-interactive smoke runs never prompt.
    /// </summary>
    void OfferListfile()
    {
        if (File.Exists(UserSettings.ListfilePath()) || UserSettings.ListfilePrompted() || OS.GetCmdlineUserArgs().Any(arg => arg.EndsWith("-smoke", StringComparison.Ordinal)))
        {
            return;
        }

        UserSettings.SaveListfilePrompted();
        var dialog = new ConfirmationDialog
        {
            Title = "Download listfile",
            OkButtonText = "Download",
            CancelButtonText = "Not now",
            DialogText = "Retail clients name many files (garrison interiors, WMO textures, liquid textures) by path, which only the community listfile can map to files.\n\nDownload it now from github.com/wowdev/wow-listfile? It is saved in your user data folder and only updated when you choose File → Update listfile…"
        };
        dialog.Confirmed += () => _ = UpdateListfile();
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(520, 0));
    }

    /// <summary>
    /// Download the listfile (replacing any existing copy only when complete), then reload the
    /// retail client so the new paths take effect.
    /// </summary>
    async Task UpdateListfile()
    {
        if (listfileDownload != null)
        {
            return;
        }

        var cancel = listfileDownload = new CancellationTokenSource();
        var bar = new ProgressBar { CustomMinimumSize = new Vector2(420, 24), MaxValue = 1, ShowPercentage = true };
        var label = new Label { Text = "Connecting…" };
        var box = new VBoxContainer();
        box.AddChild(label);
        box.AddChild(bar);
        var dialog = new AcceptDialog { Title = "Updating listfile", OkButtonText = "Cancel", Exclusive = true };
        dialog.AddChild(box);
        dialog.Confirmed += cancel.Cancel;
        dialog.CloseRequested += cancel.Cancel;
        AddChild(dialog);
        dialog.PopupCentered();
        // Progress callbacks arrive on pool threads; touch the UI only through deferred calls.
        var progress = new Progress<(long Read, long Total)>(p => Callable.From(() =>
        {
            if (IsInstanceValid(bar))
            {
                bar.Value = p.Total > 0 ? (double)p.Read / p.Total : 0;
                label.Text = p.Total > 0 ? $"{p.Read / 1048576} / {p.Total / 1048576} MB" : $"{p.Read / 1048576} MB";
            }
        }).CallDeferred());
        string destination = UserSettings.ListfilePath();
        string? error = null;
        try
        {
            await Task.Run(() => ListfileStore.DownloadAsync(destination, progress, cancel.Token));
        }
        catch (OperationCanceledException)
        {
            error = "Listfile update cancelled.";
        }
        catch (Exception e)
        {
            error = "Listfile update failed: " + e.Message;
        }

        listfileDownload = null;
        dialog.QueueFree();
        if (error != null)
        {
            message = error;
            return;
        }

        message = "Listfile updated.";
        if (profile == ClientKind.Retail)
        {
            await LoadClient();
        }
    }
}
