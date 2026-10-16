using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Graveyard placement and property edits. Every command validates against the server's loading
/// rules and records one local GraveyardDraft edit; nothing is written to the database.
/// </summary>
public partial class GraveyardOverlay : IPlacementEditor
{
    GraveyardDraft? draft;
    string draftPath = "";
    string draftMessage = "Local draft only; source database is unchanged.";
    TransformGizmo gizmo = null!;

    /// <summary>
    /// Viewer-supplied ground probe (terrain + WMO floors), shared with creature placement.
    /// </summary>
    public Func<Vector3, GroundHit>? GroundAt { get; set; }
    public bool SnapOnDrop { get; set; }
    public GroundHit? LastGround { get; private set; }
    public bool HasSelection => selected.HasValue;
    public bool CanEdit => EditableMarker() != null;
    public bool CanUndo => draft?.CanUndo == true;
    public IReadOnlyList<GraveyardEdit> NetEdits() => draft?.NetEdits() ?? [];
    public bool CanRedo => draft?.CanRedo == true;
    public bool IsDragging => gizmo.Dragging;
    public bool SupportsRotation => false;

    /// <summary>
    /// Graveyards have no orientation, so only Select and Move apply; Rotate hides the handles.
    /// </summary>
    public GizmoMode Mode
    {
        set => gizmo.Mode = value == GizmoMode.Move ? GizmoMode.Move : GizmoMode.Select;
    }

    /// <summary>
    /// Open the durable draft (isolated under artifacts/ for smoke runs) and attach the gizmo.
    /// Recovery failures keep the file and disable editing.
    /// </summary>
    void OpenDraft()
    {
        draftPath = ContentDraftFiles.LocalPath("graveyards.json");
        try
        {
            draft = new GraveyardDraft(draftPath);
        }
        catch (Exception)
        {
            draftMessage = "Cannot open graveyard draft. Editing disabled; existing file preserved.";
        }

        gizmo = new TransformGizmo();
        AddChild(gizmo);
        gizmo.Changed += MoveSelection;
    }

    (GraveyardSite Site, Node3D Node)? EditableMarker() =>
        draft != null && selected.HasValue && lookup.TryGetValue(selected.Value, out var site) && markers.TryGetValue(selected.Value, out var marker) ? (site, marker.Node) : null;

    GraveyardRecord RecordOf(GraveyardSite site) => draft?.Effective(sourceKey, site.Record) ?? site.Record;

    Func<uint, bool>? ZoneValidator() => Areas() is { } areas ? areas.Contains : null;

    /// <summary>
    /// Validate and record one edit, then re-sync the marker and inspector. Invalid edits leave the
    /// draft unchanged and show the server rule they break.
    /// </summary>
    void Commit(Func<GraveyardRecord, GraveyardRecord> change, string success)
    {
        if (EditableMarker() is not { } editing)
        {
            return;
        }

        gizmo.Finish(false);
        var before = RecordOf(editing.Site);
        var after = change(before).Normalized();
        if (after == before)
        {
            ApplyRecord(editing.Site.Record.Id);
            ShowSelection();
            return;
        }

        try
        {
            draft!.Edit(sourceKey, editing.Site.Record, after, ZoneValidator());
            draftMessage = success;
        }
        catch (InvalidDataException error)
        {
            draftMessage = "Not saved: " + error.Message;
        }
        catch (Exception)
        {
            draftMessage = "Could not save graveyard draft. Previous draft preserved.";
        }

        ApplyRecord(editing.Site.Record.Id);
        ShowSelection();
    }

    /// <summary>
    /// Gizmo previews move the marker only; a completed drag commits X/Y/Z, keeping exact source
    /// values for untouched axes and optionally snapping to the ground.
    /// </summary>
    void MoveSelection(GizmoEdit edit)
    {
        if (EditableMarker() is not { } editing)
        {
            return;
        }

        if (!edit.Commit)
        {
            editing.Node.Position = edit.Position;
            return;
        }

        var position = edit.Position;
        string success = "Moved graveyard; saved local draft.";
        if (SnapOnDrop)
        {
            var ground = GroundAt?.Invoke(position) ?? new GroundHit(null, "Ground probe unavailable.");
            if (ground.Height is float height)
            {
                position.Y = height;
                success = "Moved graveyard and snapped to ground; saved local draft.";
            }
            else
            {
                success = "Moved graveyard; not snapped: " + ground.Message;
            }
        }

        var before = RecordOf(editing.Site);
        var start = ViewPosition(before);
        var server = WorldCoordinates.ToServer(new(position.X, position.Y, position.Z));
        // View X = -server Y, view Y = server Z, view Z = -server X.
        Commit(r => r with
        {
            X = position.Z == start.Z ? r.X : server.X,
            Y = position.X == start.X ? r.Y : server.Y,
            Z = position.Y == start.Y ? r.Z : server.Z
        }, success);
    }

    public bool BeginDrag(Camera3D camera, Vector2 screen) => CanEdit && gizmo.Begin(camera, screen);
    public void Drag(Camera3D camera, Vector2 screen) => gizmo.Drag(camera, screen);
    public void EndDrag(bool commit) => gizmo.Finish(commit);

    public void DropToGround()
    {
        if (EditableMarker() is not { } editing)
        {
            return;
        }

        var record = RecordOf(editing.Site);
        var ground = GroundAt?.Invoke(ViewPosition(record)) ?? new GroundHit(null, "Ground probe unavailable.");
        LastGround = ground;
        if (ground.Height is not float height)
        {
            draftMessage = "Not dropped: " + ground.Message;
            ShowSelection();
            return;
        }

        Commit(r => r with { Z = height }, "Dropped graveyard to ground; saved local draft.");
    }

    public void ResetToSource()
    {
        if (EditableMarker() is { } editing)
        {
            Commit(_ => editing.Site.Record, "Reset to source graveyard; saved local draft.");
        }
    }

    public void SetPosition(double x, double y, double z) =>
        Commit(r => r with
        {
            // Fields show rounded text; matching values keep exact stored doubles.
            X = Math.Abs(x - r.X) < 0.5e-4 ? r.X : x,
            Y = Math.Abs(y - r.Y) < 0.5e-4 ? r.Y : y,
            Z = Math.Abs(z - r.Z) < 0.5e-4 ? r.Z : z
        }, "Saved typed position to local draft.");

    public void SetComment(string comment) => Commit(r => r with { Comment = comment.Trim() }, "Saved graveyard name to local draft.");

    public void AddLink(uint zone, ushort faction) => Commit(r => r with { Links = [.. r.Links, new GraveyardLink(zone, faction, ZoneName(zone))] }, $"Linked zone {zone}; saved local draft.");

    public void RemoveLink(uint zone) => Commit(r => r with { Links = r.Links.Where(l => l.GhostZone != zone).ToArray() }, $"Removed zone {zone} link; saved local draft.");

    public void SetLinkFaction(uint zone, ushort faction) => Commit(r => r with { Links = r.Links.Select(l => l.GhostZone == zone ? l with { Faction = faction } : l).ToArray() }, $"Changed zone {zone} faction; saved local draft.");

    public void SetLinkComment(uint zone, string comment) => Commit(r => r with { Links = r.Links.Select(l => l.GhostZone == zone ? l with { Comment = comment.Trim() } : l).ToArray() }, $"Changed zone {zone} comment; saved local draft.");

    string ZoneName(uint zone)
    {
        if (Areas() is not { } areas || !areas.Contains(zone))
        {
            return "";
        }

        var (name, parent) = areas.Describe(zone);
        return parent != null ? $"{parent} - {name}" : name;
    }

    void History(bool redo)
    {
        gizmo.Finish(false);
        try
        {
            if (redo)
            {
                draft?.Redo();
            }
            else
            {
                draft?.Undo();
            }

            draftMessage = redo ? "Redid last graveyard change." : "Undid last graveyard change.";
        }
        catch (Exception)
        {
            draftMessage = "Could not save graveyard draft history. Previous draft preserved.";
        }

        foreach (uint id in markers.Keys)
        {
            ApplyRecord(id);
        }

        ShowSelection();
    }

    /// <summary>
    /// Discard one graveyard's net edit (Pending Changes window) as an undoable command.
    /// </summary>
    public void Revert(GraveyardEdit net)
    {
        gizmo.Finish(false);
        try
        {
            draft?.Revert(net);
            draftMessage = "Discarded the graveyard edit.";
        }
        catch (Exception)
        {
            draftMessage = "Could not save graveyard draft history. Previous draft preserved.";
        }

        foreach (uint id in markers.Keys)
        {
            ApplyRecord(id);
        }

        ShowSelection();
    }

    public void Undo() => History(false);
    public void Redo() => History(true);

    /// <summary>
    /// GM teleport for checking the effective position in the client: `.go xyz X Y Z MAP`.
    /// </summary>
    public string? GoCommand() => selected.HasValue && lookup.TryGetValue(selected.Value, out var site)
        ? FormattableString.Invariant($".go xyz {RecordOf(site).X:F4} {RecordOf(site).Y:F4} {RecordOf(site).Z:F4} {site.Record.Map}")
        : null;

    public void CopyGoCommand()
    {
        if (GoCommand() is { } command)
        {
            DisplayServer.ClipboardSet(command);
            draftMessage = "Copied " + command;
            ShowSelection();
        }
    }

    public void FlyToSelection()
    {
        if (selected.HasValue && lookup.TryGetValue(selected.Value, out var site))
        {
            var r = RecordOf(site);
            FocusRequested?.Invoke(new WorldPose(r.Map, r.X, r.Y, r.Z, 0));
        }
    }
}
