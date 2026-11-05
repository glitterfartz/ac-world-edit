using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

// The creature layer's frame steps. The query, markers, models and pose editing are collaborators;
// these methods only order their work for one frame, so they stay beside the overlay state.
public partial class CreatureOverlay
{
    /// <summary>Move the query center, debounce the search and start the one current query.</summary>
    void UpdateQuery(Camera3D camera)
    {
        if (LayerEnabled && region != null)
        {
            var next = ViewSpace.QueryPointAt(camera.Position, region.Map);
            if (point != next)
            {
                point = next;
                Restart(false);
            }
        }

        if (query.TakeSettledSearch())
        {
            Restart(true);
        }

        query.Poll(Publish, error => queryStatus = error);
        group.Sync(guid => markers.TryGetValue(guid, out var member) ? member.Node.Position : null);
        if (region != null && LayerEnabled && query.WantsStart)
        {
            var next = region;
            var path = Connection();
            var preview = Preview;
            var center = point;
            string term = filter.Text;
            query.Start(token => DBSource.Open(path).ReadCreaturesAsync(next, preview, center, term, token));
        }
    }

    /// <summary>Create pending markers at a bounded rate, then restore the selection once they are drawn.</summary>
    void PrepareMarkers()
    {
        if (pendingMarkers.Count == 0)
        {
            return;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        int created = 0;
        while (pendingMarkers.Count > 0 && created < 64 && (created == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds < 2))
        {
            var spawn = pendingMarkers[^1];
            pendingMarkers.RemoveAt(pendingMarkers.Count - 1);
            AddMarker(spawn);
            created++;
        }

        if (pendingMarkers.Count == 0)
        {
            RestoreSelectionWhenDrawn();
            queryStatus = CreatureStatusText();
        }
        else
        {
            queryStatus = $"Preparing markers {records.Count - pendingMarkers.Count}/{records.Count}.";
        }
    }

    /// <summary>A placed spawn (and its group) waits for its marker, then is selected as one action.</summary>
    void RestoreSelectionWhenDrawn()
    {
        if (selectWhenDrawn is ulong placed && markers.ContainsKey(placed))
        {
            selectWhenDrawn = null;
            Select(placed);
            if (groupWhenDrawn != null)
            {
                foreach (ulong other in groupWhenDrawn.Where(markers.ContainsKey))
                {
                    group.Add(other);
                    LoadRow(other);
                }

                groupWhenDrawn = null;
                Select(placed);
            }
        }

        if (selectedGuid is not ulong selected)
        {
            return;
        }

        if (markers.ContainsKey(selected))
        {
            Select(selected);
        }
        else
        {
            Deselect();
            details.Text = "Selected creature is outside the current region.";
        }
    }

    void UpdateStatus()
    {
        if (status != null)
        {
            status.Text = LayerEnabled ? queryStatus : "Creature layer hidden";
        }
    }

    /// <summary>Reconcile the native model batches and their labels/clip menu.</summary>
    void UpdateModelBatches(Camera3D camera)
    {
        modelView.Update(camera, markers.Values, Preview, LayerEnabled, selectedGuid, previewAnimation, Models);
        foreach (var batch in modelView.Batches.Values)
        {
            if (!batch.Node.ModelReady)
            {
                continue;
            }

            foreach (ulong guid in batch.Guids)
            {
                if (markers.TryGetValue(guid, out var marker))
                {
                    marker.Body.Visible = false;
                    marker.Label.Position = new(0, modelView.LabelHeight(marker, batch.Node.Bounds), 0);
                }
            }

            if (selectedGuid.HasValue && batch.Guids.Contains(selectedGuid.Value) && animationListGuid != selectedGuid)
            {
                animation.Clear();
                int selectedIndex = 0;
                foreach (ushort id in batch.Node.AvailableAnimations)
                {
                    animation.AddItem($"{Models?.Catalog?.AnimationNames.GetValueOrDefault(id) ?? "Animation"} ({id})", id);
                    if (id == previewAnimation)
                    {
                        selectedIndex = animation.ItemCount - 1;
                    }
                }

                if (animation.ItemCount == 0)
                {
                    animation.AddItem("Static model", 0);
                }

                animation.Select(selectedIndex);
                animation.Disabled = batch.Node.AvailableAnimations.Length <= 1;
                animationListGuid = selectedGuid;
            }
        }
    }

    /// <summary>Show only admitted markers and a bounded number of nearby names.</summary>
    void UpdateVisibility(Camera3D camera)
    {
        int visibleNames = 0;
        foreach (var marker in markers.Values)
        {
            bool nearby = LayerEnabled && modelView.IsAdmitted(marker.Spawn.Guid);
            if (marker.Node.Visible != nearby)
            {
                marker.Node.Visible = nearby;
            }

            bool showName = nearby && Preview.ShowNames && visibleNames < 64 && camera.GlobalPosition.DistanceSquaredTo(marker.Node.GlobalPosition) < 80 * 80;
            bool labelVisible = selectedGuid == marker.Spawn.Guid || showName;
            if (marker.Label.Visible != labelVisible)
            {
                marker.Label.Visible = labelVisible;
            }

            if (showName)
            {
                visibleNames++;
            }
        }
    }

    /// <summary>Refresh the inspector buttons and put the gizmo on the editable marker.</summary>
    void UpdateGizmo(Camera3D camera)
    {
        UpdateInspectorActions();
        var editing = EditableMarker();
        gizmo.Update(camera, editing?.Node.GlobalPosition, editing?.Node.Rotation.Y ?? 0);
    }
}
