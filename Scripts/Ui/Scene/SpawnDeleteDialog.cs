using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// The question a spawn delete asks when other tables still name the spawn: it lists them, because the
/// spawn and its addon row are all a plain delete removes. With <c>removeToo</c> it also offers to remove
/// those rows in the same undoable command.
/// </summary>
public static class SpawnDeleteDialog
{
    /// <summary>
    /// Open the dialog on <paramref name="host"/>. <paramref name="confirmed"/> runs on "Delete only this";
    /// <paramref name="removeToo"/> runs on "Remove these too" and is offered only when it is not null.
    /// </summary>
    public static ConfirmationDialog Ask(Node host, string subject, IReadOnlyList<SpawnImpactItem> items, Action confirmed, IReadOnlyList<SpawnPathUse>? paths = null, Action? removeToo = null) =>
        Dialogs.Confirm(host, "Delete " + subject, SpawnImpact.Prompt(subject, items, paths, removeToo != null), confirmed,
            ok: removeToo == null ? "Delete anyway" : "Delete only this",
            cancel: "Keep",
            extra: removeToo == null ? null : ("Remove these too", removeToo));
}
