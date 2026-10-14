using System.Globalization;
using Godot;

namespace ACWorldEdit;

/// <summary>Read-only accessors and button presses for --areatrigger-smoke.</summary>
public partial class AreaTriggerOverlay
{
    public bool SmokeQueryIdle => pending == null && requested == generation;
    public int SmokeVisibleVolumes => markers.Values.Count(m => m.Node.Visible && !m.Node.IsQueuedForDeletion());
    public bool SmokeHasOutline => outline != null && !outline.IsQueuedForDeletion();
    public string SmokeDetails => details.Text;

    public uint? SmokeListSelection
    {
        get
        {
            var picked = list.GetSelectedItems();
            return picked.Length == 0 ? null : uint.Parse(list.GetItemMetadata(picked[0]).AsString(), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The volume node as placed (unit mesh scaled and rotated), for transform checks.</summary>
    public Transform3D? SmokeVolumeTransform(uint entry) => markers.TryGetValue(entry, out var marker) ? marker.Volume.GlobalTransform : null;

    /// <summary>Press the inspector's Edit behavior button.</summary>
    public void SmokePressBehavior() => behavior.EmitSignal(BaseButton.SignalName.Pressed);
}
