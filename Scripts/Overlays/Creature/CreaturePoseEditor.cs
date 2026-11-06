using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// What the pose editor needs from the creature overlay: the draft, the current selection, the
/// marker lookups and the publication steps (preview, select, list refresh) it must not own.
/// </summary>
internal interface ICreaturePoseHost
{
    Node HostNode { get; }
    SpawnPoses? Poses { get; }
    string SourceKey { get; }
    ulong? SelectedGuid { get; }
    IReadOnlyList<CreatureSpawn> Records { get; }
    bool SnapOnDrop { get; }
    Func<Vector3, GroundHit>? GroundAt { get; }
    GroundHit? LastGround { get; set; }
    bool HasGroup { get; }
    IReadOnlyList<ulong> MarkerKeys { get; }
    bool TryMarker(ulong guid, out CreatureMarkers.Marker marker);
    WorldPose PoseOf(CreatureSpawn spawn);
    void PreviewPose(ulong guid, Vector3 position, float yaw);
    void Say(string message);
    void Select(ulong guid);
    void Deselect();
    void PopulateList();
    IReadOnlyList<CreatureSpawn> WithDraft();
    void ReconcileSpawns(IReadOnlyList<CreatureSpawn> spawns);
    bool MoveGroup(string success, WorldPose before, WorldPose after);
    /// <summary>
    /// Polar-drag interception: when the overlay arms formation-offset dragging and this spawn
    /// follows a streamed leader, the drop moves its follow offset instead of its pose.
    /// </summary>
    bool TryFormationOffset(ulong guid, WorldPose drop);
}

/// <summary>
/// The creature gizmo flow: drag previews, the snapped commit, the group move and undo/redo
/// reconciliation. It owns the gizmo node and the gesture state; the overlay owns the draft, the
/// markers and the panel, and publishes what the editor asks for through <see cref="ICreaturePoseHost"/>.
/// </summary>
internal sealed class CreaturePoseEditor(ICreaturePoseHost host)
{
    public TransformGizmo Gizmo { get; private set; } = null!;
    public GizmoMode Mode { get => Gizmo.Mode; set => Gizmo.Mode = value; }
    public float RotationStep { get => Gizmo.RotationStep; set => Gizmo.RotationStep = value; }
    public bool Dragging => Gizmo.Dragging;

    /// <summary>Create the gizmo under the overlay and route its commit/preview events here.</summary>
    public void Attach()
    {
        Gizmo = new TransformGizmo();
        host.HostNode.AddChild(Gizmo);
        Gizmo.Changed += MoveSelection;
    }

    /// <summary>Require a writable draft, a selection and a hit on a gizmo handle before moving.</summary>
    public bool Begin(Camera3D camera, Vector2 screen) => host.Poses?.Available == true && host.SelectedGuid.HasValue && Gizmo.Begin(camera, screen);

    public void Drag(Camera3D camera, Vector2 screen) => Gizmo.Drag(camera, screen);

    public void Finish(bool commit) => Gizmo.Finish(commit);

    /// <summary>
    /// Apply gizmo previews to the marker/models and commit completed gestures. Untouched pose
    /// components keep their exact double source values: a move never rewrites heading, and a
    /// rotation never rewrites coordinates through float round-off.
    /// </summary>
    public void MoveSelection(GizmoEdit edit)
    {
        if (host.SelectedGuid is not ulong guid || !host.TryMarker(guid, out var marker))
        {
            return;
        }

        if (!edit.Commit)
        {
            host.PreviewPose(guid, edit.Position, edit.Yaw);
            return;
        }

        var before = host.PoseOf(marker.Spawn);
        var after = before;
        string success = "Saved.";
        if (edit.Moved)
        {
            var position = edit.Position;
            if (host.SnapOnDrop)
            {
                var ground = host.GroundAt?.Invoke(position) ?? new GroundHit(null, "Ground probe unavailable.");
                if (ground.Height is float height)
                {
                    position.Y = height;
                    success = "Saved; snapped to ground.";
                }
                else
                {
                    success = "Saved; not snapped: " + ground.Message;
                }
            }

            var startView = ViewSpace.ToView(before.X, before.Y, before.Z);
            var server = ViewSpace.ToServer(position);
            // View X = -server Y, view Y = server Z, view Z = -server X.
            after = before with
            {
                X = position.Z == startView.Z ? before.X : server.X,
                Y = position.X == startView.X ? before.Y : server.Y,
                Z = position.Y == startView.Y ? before.Z : server.Z
            };
        }
        else if (edit.Yaw != (float)before.Heading)
        {
            after = before with { Heading = WorldCoordinates.NormalizeHeading(edit.Yaw) };
        }

        if (edit.Moved && host.TryFormationOffset(guid, after))
        {
            return;
        }

        Commit(marker.Spawn, after, success);
    }

    /// <summary>
    /// Record one draft command for a creature and reconcile its marker/models from effective
    /// history. Failed saves or stale baselines keep the persisted pose.
    /// </summary>
    public void Commit(CreatureSpawn spawn, WorldPose after, string success)
    {
        if (host.Poses is not { } poses)
        {
            return;
        }

        // A click without a drag (or an unchanged typed pose) records nothing; just re-sync.
        var before = host.PoseOf(spawn);
        if (after == before)
        {
            ApplyPose(spawn.Guid);
            return;
        }

        if (host.HasGroup && host.MoveGroup(success, before, after))
        {
            return;
        }

        var (ok, message) = poses.Move(host.HostNode, host.SourceKey, spawn.Guid, after);
        host.Say(ok ? success : message);
        ApplyPose(spawn.Guid);
        host.Select(spawn.Guid);
    }

    /// <summary>Move a marker and its native model placements to the effective draft pose.</summary>
    public void ApplyPose(ulong guid)
    {
        if (!host.TryMarker(guid, out var marker))
        {
            return;
        }

        var pose = host.PoseOf(marker.Spawn);
        host.PreviewPose(guid, ViewSpace.ToView(pose.X, pose.Y, pose.Z), (float)pose.Heading);
    }

    /// <summary>
    /// Cancel the active preview, persist undo/redo and reconcile marker/native poses from
    /// effective history.
    /// </summary>
    public void History(bool redo)
    {
        Gizmo.Finish(false);
        if (host.Poses is not { } poses)
        {
            return;
        }

        string message = redo ? poses.Redo(host.HostNode) : poses.Undo(host.HostNode);
        if (message.Length > 0)
        {
            host.Say(message);
        }

        Resync();
    }

    public void Resync()
    {
        // The draft may have placed or deleted spawns: redraw the set before posing what is left.
        if (host.SourceKey.Length > 0)
        {
            host.ReconcileSpawns(host.WithDraft());
            if (host.SelectedGuid is ulong selected && !host.Records.Any(s => s.Guid == selected))
            {
                host.Deselect();
            }

            host.PopulateList();
        }

        foreach (ulong guid in host.MarkerKeys)
        {
            ApplyPose(guid);
        }

        if (host.SelectedGuid is ulong current)
        {
            host.Select(current);
        }
    }
}
