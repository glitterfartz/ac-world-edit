using Godot;
using ACWorldEdit.Data;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// GameObject placement commands shared by the gizmo, inspector, menus, toolbar and shortcuts. Each
/// command records at most one local draft command; source rows are never touched. Models, fallbacks
/// and labels show the effective (draft) placement.
/// </summary>
public partial class GameObjectOverlay : IPlacementEditor
{
    SpawnPoses? poses;
    ContentDrafts? content;
    readonly HashSet<ulong> loadingRows = [];
    string sourceKey = "";
    TransformGizmo gizmo = null!;

    /// <summary>Where editing feedback goes (the status bar); never the inspector.</summary>
    public Action<string>? Report { get; set; }

    void Say(string message) => Report?.Invoke(message);

    /// <summary>Viewer-supplied probe over loaded terrain and ready WMO floors; unset means no snapping.</summary>
    public Func<Vector3, GroundHit>? GroundAt { get; set; }
    public bool SnapOnDrop { get; set; }
    public GroundHit? LastGround { get; private set; }
    public bool HasSelection => selected.HasValue;
    public bool SupportsRotation => true;
    public bool IsDragging => gizmo.Dragging;
    public bool CanEdit => EditableSpawn() != null;
    public bool CanUndo => poses?.CanUndo == true;
    public bool CanRedo => poses?.CanRedo == true;

    public GizmoMode Mode
    {
        get => gizmo.Mode;
        set => gizmo.Mode = value;
    }

    public float RotationStep
    {
        get => gizmo.RotationStep;
        set => gizmo.RotationStep = value;
    }

    /// <summary>
    /// Attach the gizmo.
    /// </summary>
    void CreateGizmo()
    {
        gizmo = new TransformGizmo();
        AddChild(gizmo);
        gizmo.Changed += MoveSelection;
    }

    /// <summary>
    /// Keep GameObject placements on the shared content draft, and redraw whenever any editor changes
    /// it (an undo, a discard in Pending Changes, a profile switch).
    /// </summary>
    public void Attach(ContentDrafts drafts)
    {
        content = drafts;
        poses = new SpawnPoses(drafts, SpawnKind.GameObject, ContentDraftFiles.Sources.DialectOf);
        drafts.Changed += _ =>
        {
            if (IsInsideTree() && gizmo != null)
            {
                Resync();
            }
        };
    }

    /// <summary>
    /// Load the selected GameObject's database row in the background: a placement edit replaces the
    /// whole row, but the region read only carries the placement.
    /// </summary>
    async void LoadRow(ulong guid)
    {
        if (poses == null || !poses.Available || poses.Has(sourceKey, guid) || IsStaged(guid) || !loadingRows.Add(guid))
        {
            return;
        }

        string key = sourceKey;
        var config = Connection();
        // Rows are validated against the source's dialect, which the pending-changes poll may not have noted yet.
        ContentDraftFiles.Sources.Remember(key, config.Dialect);
        try
        {
            var rows = await DBSource.Open(config).ReadSpawnRowsAsync(SpawnKind.GameObject, [guid], default);
            poses.Load(key, rows);
        }
        catch (Exception)
        {
            Say("Could not read the GameObject's row; it cannot be moved yet.");
        }
        finally
        {
            loadingRows.Remove(guid);
        }
    }

    /// <summary>The selected spawn with its effective placement, when it can be edited.</summary>
    GameObjectSpawn? EditableSpawn() =>
        poses?.Available == true && selected.HasValue && lookup.TryGetValue(selected.Value, out var source) ? Effective(source) : null;

    GameObjectPlacement PlacementOf(GameObjectSpawn source) => poses?.Effective(sourceKey, source.Guid, GameObjectPlacement.From(source)) ?? GameObjectPlacement.From(source);

    /// <summary>The spawn as it stands with the draft applied: pose and facing replaced, the rest unchanged.</summary>
    GameObjectSpawn Effective(GameObjectSpawn source) => With(source, PlacementOf(source));

    static GameObjectSpawn With(GameObjectSpawn source, GameObjectPlacement placement)
    {
        var (x, y, z, w) = placement.FacingQuaternion();
        return source with { Pose = placement.Pose, Rotation = new((float)x, (float)y, (float)z, (float)w) };
    }

    /// <summary>
    /// Show a placement on the models, fallback and labels of one spawn without recording anything.
    /// </summary>
    void ShowPlacement(GameObjectSpawn source, GameObjectPlacement placement)
    {
        var spawn = With(source, placement);
        foreach (var batch in batches.Values)
        {
            int index = Array.IndexOf(batch.Guids, spawn.Guid);
            if (index >= 0)
            {
                batch.Node.SetPlacement(index, ModelTransform(spawn));
            }
        }

        if (fallbacks.TryGetValue(spawn.Guid, out var fallback))
        {
            fallback.Position = ViewPosition(spawn) + Vector3.Up * 0.5f;
        }

        if (names.TryGetValue(spawn.Guid, out var label))
        {
            label.Position = ViewPosition(spawn) + Vector3.Up * 2;
        }

        if (selected == spawn.Guid)
        {
            selectionLabel.Position = ViewPosition(spawn) + Vector3.Up * 2;
        }
    }

    void Resync()
    {
        // The draft may have placed or deleted spawns: rebuild the set before posing what is left.
        if (sourceKey.Length > 0)
        {
            Recompose();
        }

        foreach (var source in lookup.Values)
        {
            ShowPlacement(source, PlacementOf(source));
        }

        ShowSelection();
    }

    /// <summary>
    /// Gizmo previews move the models only; a completed gesture commits one draft command. Untouched
    /// components keep their exact stored values, so a move never rewrites the heading and a turn
    /// never rewrites the coordinates through float round-off.
    /// </summary>
    void MoveSelection(GizmoEdit edit)
    {
        if (!selected.HasValue || !lookup.TryGetValue(selected.Value, out var source))
        {
            return;
        }

        var before = PlacementOf(source);
        var after = before;
        string success = "Saved.";
        if (edit.Moved)
        {
            var position = edit.Position;
            if (edit.Commit && SnapOnDrop)
            {
                var ground = GroundAt?.Invoke(position) ?? new GroundHit(null, "Ground probe unavailable.");
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

            var start = ViewPosition(With(source, before));
            var server = ViewSpace.ToServer(position);
            // View X = -server Y, view Y = server Z, view Z = -server X.
            after = before with
            {
                Pose = before.Pose with
                {
                    X = position.Z == start.Z ? before.Pose.X : server.X,
                    Y = position.X == start.X ? before.Pose.Y : server.Y,
                    Z = position.Y == start.Y ? before.Pose.Z : server.Z
                }
            };
        }
        else if (edit.Yaw != (float)before.Pose.Heading)
        {
            after = before.WithHeading(edit.Yaw);
        }

        if (!edit.Commit)
        {
            ShowPlacement(source, after);
            return;
        }

        Commit(source, after, success);
    }

    /// <summary>
    /// Record one draft command and reconcile models and inspector from effective history. Failed
    /// saves or stale baselines keep the persisted placement.
    /// </summary>
    void Commit(GameObjectSpawn source, GameObjectPlacement after, string success)
    {
        if (poses == null)
        {
            return;
        }

        // A click without a drag (or an unchanged typed pose) records nothing; just re-sync.
        if (after == PlacementOf(source))
        {
            ShowPlacement(source, after);
            ShowSelection();
            return;
        }

        var (ok, message) = poses.Move(this, sourceKey, source.Guid, after);
        Say(ok ? success : message);
        ShowPlacement(source, PlacementOf(source));
        ShowSelection();
    }

    public bool BeginDrag(Camera3D camera, Vector2 screen) => CanEdit && gizmo.Begin(camera, screen);
    public void Drag(Camera3D camera, Vector2 screen) => gizmo.Drag(camera, screen);
    public void EndDrag(bool commit) => gizmo.Finish(commit);

    /// <summary>
    /// Snap Z to the ground below the effective placement. Probe failures leave the draft unchanged
    /// and explain why.
    /// </summary>
    public void DropToGround()
    {
        if (!selected.HasValue || !lookup.TryGetValue(selected.Value, out var source) || poses?.Available != true)
        {
            return;
        }

        gizmo.Finish(false);
        var before = PlacementOf(source);
        var ground = GroundAt?.Invoke(ViewPosition(With(source, before))) ?? new GroundHit(null, "Ground probe unavailable.");
        LastGround = ground;
        if (ground.Height is not float height)
        {
            Say("Not dropped: " + ground.Message);
            ShowSelection();
            return;
        }

        Commit(source, before with { Pose = before.Pose with { Z = height } }, "Dropped to ground.");
    }

    /// <summary>Return the selection to its source placement as a normal undoable command.</summary>
    public void ResetToSource()
    {
        if (selected.HasValue && lookup.TryGetValue(selected.Value, out var source))
        {
            gizmo.Finish(false);
            Commit(source, GameObjectPlacement.From(source), "Reset to source placement.");
        }
    }

    /// <summary>Commit typed coordinates (server space) and heading in degrees.</summary>
    public void SetPose(double x, double y, double z, double headingDegrees)
    {
        if (!selected.HasValue || !lookup.TryGetValue(selected.Value, out var source) || poses?.Available != true)
        {
            return;
        }

        gizmo.Finish(false);
        var before = PlacementOf(source);
        var pose = PoseText.Typed(before.Pose, x, y, z, headingDegrees);
        try
        {
            pose.Validate();
        }
        catch (ArgumentException)
        {
            Say("Coordinates must be finite numbers.");
            ShowSelection();
            return;
        }

        var after = pose.Heading == before.Pose.Heading ? before with { Pose = pose } : (before with { Pose = pose with { Heading = before.Pose.Heading } }).WithHeading(pose.Heading);
        Commit(source, after, "Saved typed pose.");
    }

    void History(bool redo)
    {
        gizmo.Finish(false);
        if (poses == null)
        {
            return;
        }

        string message = redo ? poses.Redo(this) : poses.Undo(this);
        if (message.Length > 0)
        {
            Say(message);
        }

        Resync();
    }

    public void Undo() => History(false);
    public void Redo() => History(true);

    /// <summary>GM teleport for comparing the effective pose in the client: `.go xyz X Y Z MAP O`.</summary>
    public string? GoCommand()
    {
        if (!selected.HasValue || !lookup.TryGetValue(selected.Value, out var source))
        {
            return null;
        }

        var pose = PlacementOf(source).Pose;
        return PoseText.GoCommand(pose.Map, pose.X, pose.Y, pose.Z, pose.Heading);
    }

    public void CopyGoCommand()
    {
        if (GoCommand() is { } command)
        {
            DisplayServer.ClipboardSet(command);
            Say("Copied " + command);
            ShowSelection();
        }
    }
}
