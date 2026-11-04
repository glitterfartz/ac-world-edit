using Godot;

namespace ACWorldEdit;

/// <summary>
/// The two dialog shapes the viewer repeats: an acknowledgement and a confirm/cancel question. Both
/// are configured before they enter the tree and free themselves on either answer, so a caller only
/// supplies the text and the confirmed action.
/// </summary>
public static class Dialogs
{
    /// <summary>An acknowledgement with one button.</summary>
    public static AcceptDialog Info(Node owner, string title, string text, string ok = "OK", Vector2I? size = null)
    {
        var dialog = new AcceptDialog
        {
            Title = title,
            DialogText = text,
            OkButtonText = ok
        };
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        owner.AddChild(dialog);
        if (size is { } rect)
        {
            dialog.PopupCentered(rect);
        }
        else
        {
            dialog.PopupCentered();
        }

        return dialog;
    }

    /// <summary>
    /// A confirm/cancel question. <paramref name="confirmed"/> runs only on the affirmative answer;
    /// the dialog frees itself either way.
    /// </summary>
    public static ConfirmationDialog Confirm(Node owner, string title, string text, Action confirmed,
        string ok = "OK", string cancel = "Cancel", Vector2I? size = null)
    {
        var dialog = new ConfirmationDialog
        {
            Title = title,
            DialogText = text,
            OkButtonText = ok,
            CancelButtonText = cancel
        };
        dialog.Confirmed += () =>
        {
            confirmed();
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        owner.AddChild(dialog);
        if (size is { } rect)
        {
            dialog.PopupCentered(rect);
        }
        else
        {
            dialog.PopupCentered();
        }

        return dialog;
    }
}
