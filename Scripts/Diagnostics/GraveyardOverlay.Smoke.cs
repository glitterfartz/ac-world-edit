using System.Globalization;
using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Opt-in graveyard editing checks run by --graveyard-smoke against the isolated smoke draft.
/// They drive the real gizmo, commands and validation paths; normal editing never calls them.
/// </summary>
public partial class GraveyardOverlay
{
    public uint? SelectedId => selected;
    public Vector3? PositionOf(uint id) => markers.TryGetValue(id, out var marker) ? marker.Node.GlobalPosition : null;
    public uint? SmokeListSelection => list.GetSelectedItems() is { Length: > 0 } items ? uint.Parse(list.GetItemMetadata(items[0]).AsString(), CultureInfo.InvariantCulture) : null;
    public int SmokeVisibleMarkers => markers.Values.Count(m => m.Node.Visible);
    public Vector3? SmokeIconPosition(uint id) => markers.TryGetValue(id, out var m) ? m.Icon.GlobalPosition : null;
    public bool SmokeQueryIdle => pending == null && requested == generation;

    static bool Fail(string step)
    {
        GD.PushError("GRAVEYARD_SMOKE_STEP_FAILED " + step);
        return false;
    }

    GraveyardRecord Current(uint id) => RecordOf(lookup[id]);

    /// <summary>
    /// Move (cancel, commit, undo/redo), drop to ground, name and link edits, server-rule
    /// rejections and draft reopen, ending back at the source row.
    /// </summary>
    public bool SmokeEditing(Camera3D camera)
    {
        if (!selected.HasValue || draft == null)
        {
            return Fail("no editable selection");
        }

        uint id = selected.Value;
        var source = lookup[id].Record;
        var node = markers[id].Node;
        Mode = GizmoMode.Move;
        gizmo.Update(camera, node.GlobalPosition, 0);
        var original = node.Position;
        var handle = original + Vector3.Right * gizmo.Scale.X * 0.7f;
        if (!BeginDrag(camera, camera.UnprojectPosition(handle)))
        {
            return Fail("gizmo pick");
        }

        Drag(camera, camera.UnprojectPosition(handle + Vector3.Right * 3));
        EndDrag(false);
        if (node.Position.DistanceTo(original) > 0.01f || Current(id) != source)
        {
            return Fail("move cancel");
        }

        gizmo.Update(camera, node.GlobalPosition, 0);
        BeginDrag(camera, camera.UnprojectPosition(handle));
        Drag(camera, camera.UnprojectPosition(handle + Vector3.Right * 3));
        EndDrag(true);
        var moved = Current(id);
        // View +X is server -Y; only Y may change and X/Z keep exact source doubles.
        if (Math.Abs(moved.Y - (source.Y - 3)) > 0.05 || moved.X != source.X || moved.Z != source.Z || node.Position.DistanceTo(original + Vector3.Right * 3) > 0.05f)
        {
            return Fail($"move commit y={moved.Y} source={source.Y}");
        }

        Undo();
        if (Current(id) != source || node.Position.DistanceTo(original) > 0.01f)
        {
            return Fail("move undo");
        }

        Redo();
        if (Current(id) != moved)
        {
            return Fail("move redo");
        }

        Undo();
        DropToGround();
        if (LastGround is not { Height: float height } || Math.Abs(Current(id).Z - height) > 1e-4 || Current(id).X != source.X)
        {
            return Fail("drop to ground: " + LastGround?.Message);
        }

        GD.Print(FormattableString.Invariant($"GRAVEYARD_SMOKE_DROP z {source.Z:F3}->{Current(id).Z:F3} via={LastGround?.Message}"));
        Undo();

        SetComment("Smoke Graveyard");
        if (Current(id).Comment != "Smoke Graveyard")
        {
            return Fail("comment edit");
        }

        uint zone = Current(id).Links.Any(l => l.GhostZone == 40) ? 44u : 40u;
        int linkCount = Current(id).Links.Length;
        AddLink(zone, GraveyardLink.Horde);
        if (Current(id).Links.Length != linkCount + 1 || Current(id).Links.Single(l => l.GhostZone == zone).Faction != GraveyardLink.Horde)
        {
            return Fail("add link");
        }

        SetLinkFaction(zone, GraveyardLink.Alliance);
        SetLinkComment(zone, "Smoke link");
        var link = Current(id).Links.Single(l => l.GhostZone == zone);
        if (link.Faction != GraveyardLink.Alliance || link.Comment != "Smoke link")
        {
            return Fail("link faction/comment");
        }

        // Server rules: a duplicate zone and an unknown AreaTable ID must be refused unchanged.
        var beforeInvalid = Current(id);
        AddLink(zone, GraveyardLink.Neutral);
        AddLink(9999999, GraveyardLink.Neutral);
        if (Current(id) != beforeInvalid || !draftMessage.StartsWith("Not saved", StringComparison.Ordinal))
        {
            return Fail("invalid link accepted");
        }

        // Reopen the draft file: edits survive.
        var expected = Current(id);
        draft.Dispose();
        draft = new GraveyardDraft(draftPath);
        if (Current(id) != expected)
        {
            return Fail("reopen");
        }

        RemoveLink(zone);
        if (Current(id).Links.Any(l => l.GhostZone == zone))
        {
            return Fail("remove link");
        }

        ResetToSource();
        if (Current(id) != source.Normalized())
        {
            return Fail("reset to source");
        }

        GD.Print($"GRAVEYARD_SMOKE_EDITING_PASSED id={id} move/cancel/undo/redo/drop/comment/links/rules/reopen/reset");
        return true;
    }
}
