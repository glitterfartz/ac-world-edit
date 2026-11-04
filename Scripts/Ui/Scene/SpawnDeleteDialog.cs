using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The question a spawn delete asks when other tables still name the spawn: it lists them, because the
/// exported delete removes only the spawn and its addon row and would leave those rows dangling.
/// </summary>
public static class SpawnDeleteDialog
{
    /// <summary>Open the dialog on <paramref name="host"/>; <paramref name="confirmed"/> runs only on "Delete anyway".</summary>
    public static ConfirmationDialog Ask(Node host, string subject, IReadOnlyList<SpawnImpactItem> items, Action confirmed, IReadOnlyList<SpawnPathUse>? paths = null) =>
        Dialogs.Confirm(host, "Delete " + subject, SpawnImpact.Prompt(subject, items, paths), confirmed,
            ok: "Delete anyway", cancel: "Keep");
}
