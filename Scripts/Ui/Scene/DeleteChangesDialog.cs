using Godot;

namespace ACWorldEdit;

/// <summary>
/// Asks how much of the history to delete: applied, reverted and failed changes older than an age, or
/// every finished change. Staged drafts are never offered, and only the open world is counted. The
/// dialog only asks; the viewer runs the delete and reloads the list.
/// </summary>
public sealed partial class DeleteChangesDialog : ConfirmationDialog
{
    /// <summary>Choices in the order the picker shows them; 0 days means every finished change.</summary>
    static readonly (int Days, string Text)[] Ages =
    [
        (7, "Older than 7 days"),
        (30, "Older than 30 days"),
        (90, "Older than 90 days"),
        (0, "All finished changes"),
    ];

    readonly OptionButton age = new() { Name = "Age" };
    readonly Label matching;
    readonly Func<int, int> countFor;

    /// <summary>Raised on Delete with the chosen age in days; 0 means every finished change.</summary>
    public event Action<int>? DeleteRequested;

    public DeleteChangesDialog(Func<int, int> countFor)
    {
        this.countFor = countFor;
        Title = "Delete changes";
        OkButtonText = "Delete";
        CancelButtonText = "Keep";
        Exclusive = true;
        // The built-in label wraps and sizes itself; a wrapped Label added as custom content
        // reports a stale full-height minimum before it is laid out and stretches the dialog.
        DialogText = "Delete applied, reverted and failed changes for the open world. Staged edits are kept.";
        foreach (var (_, text) in Ages)
        {
            age.AddItem(text);
        }

        age.Select(1);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(age);
        matching = new Label();
        box.AddChild(matching);
        AddChild(box);

        age.ItemSelected += _ => UpdateCount();
        Confirmed += () =>
        {
            int days = Days;
            QueueFree();
            DeleteRequested?.Invoke(days);
        };
        Canceled += QueueFree;
        UpdateCount();
    }

    /// <summary>The selected age in days; 0 means every finished change.</summary>
    public int Days => Ages[Mathf.Clamp(age.Selected, 0, Ages.Length - 1)].Days;

    void UpdateCount()
    {
        int count = countFor(Days);
        matching.Text = count == 1 ? "1 change matches." : $"{count} changes match.";
        GetOkButton().Disabled = count == 0;
    }
}
