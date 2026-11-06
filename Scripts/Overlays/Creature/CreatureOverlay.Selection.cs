using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

// Selection and list publication. Selection is the stable GUID the list, picking and the gizmo
// share; the marker and model nodes belong to their own collaborators.
public partial class CreatureOverlay
{
    /// <summary>
    /// Filter loaded records and preserve GUID metadata so duplicate names/templates cannot confuse
    /// selection.
    /// </summary>
    void PopulateList()
    {
        list.Clear();
        string term = filter.Text.Trim();
        foreach (var spawn in records.Where(s => $"{s.Name} {s.Guid} {s.TemplateId}".Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            int index = list.AddItem($"{spawn.Name}   ·   {spawn.Guid}");
            list.SetItemTooltip(index, $"GUID {spawn.Guid} · Template {spawn.TemplateId}\n{spawn.Visibility?.Description}");
            list.SetItemMetadata(index, spawn.Guid.ToString());
            if (selectedGuid == spawn.Guid)
            {
                list.Select(index);
            }
        }
    }

    public void Deselect()
    {
        // Cancel while the selected identity still exists so an unfinished move restores its pose.
        gizmo.Finish(false);
        if (selectedGuid.HasValue && markers.ContainsKey(selectedGuid.Value))
        {
            markers.Highlight(selectedGuid, null);
        }

        selectedGuid = null;
        group.Clear();
        list?.DeselectAll();
        animation.Disabled = true;
        selectionName.Text = "Select a creature";
        selectionMeta.Text = "Click a model or choose one above";
        details.Text = "Selection details appear here.";
        ShowPose(null);
        RefreshSpawnFields();
    }

    /// <summary>
    /// Synchronize viewport/list/details and reset the clip menu on selection changes. Highlight
    /// fallback geometry when needed.
    /// </summary>
    public void Select(ulong guid)
    {
        if (!markers.TryGetValue(guid, out var marker))
        {
            return;
        }

        if (selectedGuid != guid)
        {
            group.Clear();
        }

        if (selectedGuid.HasValue && markers.ContainsKey(selectedGuid.Value))
        {
            markers.Highlight(selectedGuid, guid);
        }

        if (selectedGuid != guid)
        {
            // Keep playing the marker's own stance/seat animation instead of resetting to Stand;
            // the clip menu re-selects it below once the batches report their clips.
            previewAnimation = modelView.RiderAnimFor(guid);
            userPreviewAnim = false;
            animationListGuid = null;
            modelView.ResetTimer();
            animation.Disabled = true;
        }

        selectedGuid = guid;
        SelectionChanged?.Invoke();
        LoadRow(guid);
        var spawn = marker.Spawn;
        var pose = PoseOf(spawn);
        selectionName.Text = spawn.Name;
        selectionMeta.Text = $"GUID {spawn.Guid}   /   Template {spawn.TemplateId}" + (group.Members.Count > 0 ? $"   (+{group.Members.Count} selected)" : "");
        details.Text = FormattableString.Invariant($"Map {pose.Map} · Display {spawn.DisplayId}\nX {pose.X:F2}    Y {pose.Y:F2}    Z {pose.Z:F2}\nHeading {PoseText.HeadingText(pose.Heading)}\n{SpawnPhasing.Describe(spawn.Phasing, spawn.PhaseMask, spawn.SpawnMask)}\n{spawn.Visibility?.Description}\n{(poses?.Conflict(sourceKey, guid, spawn.Pose) == true ? "Source position changed; draft requires review." : "")}");
        ShowPose(pose);
        list.DeselectAll();
        InspectorUi.SelectRow(list, guid);
        RefreshSpawnFields();
    }
}
