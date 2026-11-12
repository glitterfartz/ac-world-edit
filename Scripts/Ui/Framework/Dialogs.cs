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
    /// A confirm/cancel question with an optional third answer. <paramref name="extra"/> is a button label
    /// and the action it runs; the dialog frees itself on any answer.
    /// </summary>
    public static ConfirmationDialog Confirm(Node owner, string title, string text, Action confirmed,
        string ok = "OK", string cancel = "Cancel", Vector2I? size = null, (string Label, Action Acted)? extra = null)
    {
        var dialog = new ConfirmationDialog
        {
            Title = title,
            DialogText = text,
            OkButtonText = ok,
            CancelButtonText = cancel
        };
        if (extra is { } third)
        {
            dialog.AddButton(third.Label, false, "extra");
            dialog.CustomAction += action =>
            {
                if (action.ToString() != "extra")
                {
                    return;
                }

                third.Acted();
                dialog.QueueFree();
            };
        }

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
