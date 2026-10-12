using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;
using ACWorldEdit.Assets;

namespace ACWorldEdit;
/// <summary>Read-only, transient projection of nearby database spawns.</summary>
public partial class CreatureOverlay
{
    public bool ChooseAnimationForSmoke(ushort id)
    {
        for (int i = 0; i < animation.ItemCount; i++)
            if (animation.GetItemId(i) == id)
            {
                animation.Select(i);
                animation.EmitSignal(OptionButton.SignalName.ItemSelected, i);
                return true;
            }

        return false;
    }

    public bool SmokeGizmo(Camera3D camera)
    {
        if (!selectedGuid.HasValue || draft == null)
            return false;
        uint guid = selectedGuid.Value;
        var original = markers[guid].Node.Position;
        if (!Pick(camera, camera.UnprojectPosition(original + Vector3.Up)) || selectedGuid != guid)
            return false;
        gizmo.Update(camera, original);
        var handle = original + Vector3.Right * gizmo.Scale.X * 0.7f;
        if (!BeginDrag(camera, camera.UnprojectPosition(handle)))
            return false;
        Drag(camera, camera.UnprojectPosition(handle + Vector3.Right * 2));
        EndDrag(true);
        var moved = markers[guid].Node.Position;
        if (moved.DistanceTo(original + Vector3.Right * 2) > 0.05f)
            return false;
        foreach (var batch in modelBatches.Values)
        {
            int i = Array.IndexOf(batch.Guids, guid);
            if (i >= 0 && batch.Node.Transforms![i].Origin.DistanceTo(moved) > 0.01f)
                return false;
        }

        draft.Dispose();
        draft = new CreatureDraft(draftPath);
        if (ViewPosition(PoseOf(markers[guid].Spawn)).DistanceTo(moved) > 0.01f)
            return false;
        History(false);
        if (markers[guid].Node.Position.DistanceTo(original) > 0.01f)
            return false;
        History(true);
        if (markers[guid].Node.Position.DistanceTo(moved) > 0.01f)
            return false;
        History(false);
        return markers[guid].Node.Position.DistanceTo(original) < 0.01f;
    }
}
