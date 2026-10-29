using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>Shared helpers of the spawn-authoring smoke.</summary>
internal static class SpawnSmoke
{
    /// <summary>
    /// A spawn among <paramref name="guids"/> that other tables still name, with what names it. Halves the
    /// list while the count says some member is referenced, so it needs few database round trips.
    /// </summary>
    public static async Task<(ulong Guid, IReadOnlyList<SpawnImpactItem> Items)?> FindReferenced(IDBSource source, SpawnKind kind, IReadOnlyList<ulong> guids)
    {
        if (guids.Count == 0)
        {
            return null;
        }

        var items = await source.CountSpawnReferencesAsync(kind, guids, default);
        if (items.Count == 0)
        {
            return null;
        }

        if (guids.Count == 1)
        {
            return (guids[0], items);
        }

        int half = guids.Count / 2;
        return await FindReferenced(source, kind, guids.Take(half).ToArray()) ?? await FindReferenced(source, kind, guids.Skip(half).ToArray());
    }

    /// <summary>Answer an open delete-impact dialog: "Delete anyway" or "Keep". False when none is open.</summary>
    public static bool Answer(Godot.ConfirmationDialog? dialog, bool delete)
    {
        if (dialog == null || !Godot.GodotObject.IsInstanceValid(dialog) || dialog.IsQueuedForDeletion())
        {
            return false;
        }

        (delete ? dialog.GetOkButton() : dialog.GetCancelButton()).EmitSignal(Godot.BaseButton.SignalName.Pressed);
        return true;
    }

    public static string? Text(Godot.ConfirmationDialog? dialog) =>
        dialog != null && Godot.GodotObject.IsInstanceValid(dialog) && !dialog.IsQueuedForDeletion() ? dialog.DialogText : null;
}
