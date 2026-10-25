using ACWorldEdit.Foundation;
using Xunit;
using static Test;

/// <summary>
/// Waypoint draft persistence, float-column rounding, legacy position-only drafts and the
/// generated SQL guards.
/// </summary>
public class WaypointDraftTests
{
    static string TempDraft() => Path.Combine(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "acwe-tests", Guid.NewGuid().ToString("N"))), "waypoints.json");

    static readonly WaypointProps SourceProps = new(null, 0, 0, false, 0, 0, 0);

    static WaypointState Source(double x, double y, double z) => new WaypointState(new(x, y, z), SourceProps).ToColumn();

    [Fact]
    public void MovesPersistUndoAndConflict()
    {
        string path = TempDraft();
        var source = Source(-8913.23, -133.909, 80.9273);
        var moved = source with { Position = new(-8900, -130, 81) };
        using (var draft = new WaypointDraft(path))
        {
            Check(draft.Effective("db", 77, 3, source) == source, "Unedited point differs from source");
            draft.Edit("db", 77, 3, source, moved);
            draft.Edit("db", 77, 3, source, moved with { Position = moved.Position with { Z = 82 } });
            Check(draft.Effective("db", 77, 3, source).Position.Z == 82, "Second move lost");
            Check(draft.Effective("db", 77, 4, source) == source, "Edit leaked to another point");
            Check(draft.Effective("other", 77, 3, source) == source, "Different source shares draft");
            var stale = source with { Position = source.Position with { Z = 1 } };
            Check(draft.Conflict("db", 77, 3, stale), "Stale baseline not detected");
            Rejects(() => draft.Edit("db", 77, 3, stale, moved), "Edit over stale baseline accepted");
            Rejects(() => draft.Edit("db", 77, 3, source, moved with { Position = moved.Position with { X = double.NaN } }), "Non-finite coordinate accepted");
            int before = draft.NetEdits().Count;
            draft.Edit("db", 77, 3, source, draft.Effective("db", 77, 3, source));
            Check(draft.NetEdits().Count == before, "No-op edit added history");
            try
            {
                using var duplicate = new WaypointDraft(path);
                throw new Exception("Second writer accepted");
            }
            catch (IOException)
            {
            }
        }

        using (var draft = new WaypointDraft(path))
        {
            Check(draft.Effective("db", 77, 3, source).Position.Z == 82, "Reopen lost edits");
            draft.Undo();
            Check(draft.Effective("db", 77, 3, source).Position.X == -8900 && draft.Effective("db", 77, 3, source).Position.Z == 81, "Undo failed");
            draft.Redo();
            Check(draft.Effective("db", 77, 3, source).Position.Z == 82, "Redo failed");
            var net = Assert.Single(draft.NetEdits());
            Check(net.Before == source.Position && net.After.Z == 82, "Net edit does not span the whole history");
            draft.Revert(net);
            Check(draft.Effective("db", 77, 3, source) == source && draft.NetEdits().Count == 0, "Revert did not return to the source");
            draft.Undo();
            Check(draft.Effective("db", 77, 3, source).Position.Z == 82, "Revert is not undoable");
        }
    }

    [Fact]
    public void PropertyEditsPersistAndMixWithMoves()
    {
        string path = TempDraft();
        var source = Source(10, 20, 30);
        var waiting = source with { Props = source.Props with { Delay = 5000, Orientation = 1.5, MoveType = 1 } };
        using (var draft = new WaypointDraft(path))
        {
            draft.Edit("db", 9, 2, source, waiting);
            draft.Edit("db", 9, 2, source, waiting with { Position = new(11, 20, 30) });
            Check(draft.Effective("db", 9, 2, source).Props == waiting.Props.ToColumn(), "Properties lost by a later move");
            Rejects(() => draft.Edit("db", 9, 2, source, waiting with { Props = waiting.Props with { MoveType = 4 } }), "Move type the server skips accepted");
            Rejects(() => draft.Edit("db", 9, 2, source, waiting with { Props = waiting.Props with { Velocity = -1 } }), "Negative velocity accepted");
            Rejects(() => draft.Edit("db", 9, 2, source, waiting with { Props = waiting.Props with { ActionChance = 40000 } }), "Action chance beyond 16 bits accepted");
        }

        using (var draft = new WaypointDraft(path))
        {
            var effective = draft.Effective("db", 9, 2, source);
            Check(effective.Position.X == 11 && effective.Props.Delay == 5000 && effective.Props.Orientation == 1.5 && effective.Props.MoveType == 1, "Reopen lost property edits");
            var net = Assert.Single(draft.NetEdits());
            Check(net.BeforeProps == source.Props && net.AfterProps == effective.Props, "Net edit props wrong");
            draft.Undo();
            draft.Undo();
            Check(draft.Effective("db", 9, 2, source) == source, "Undo of property edits failed");
        }
    }

    [Fact]
    public void LegacyPositionOnlyDraftsKeepSourceProperties()
    {
        // Drafts saved before properties were editable carry no props on either side.
        string path = TempDraft();
        using (var history = new DraftHistory<WaypointEdit>(path, "waypoint", e => e))
        {
            history.Append(new WaypointEdit("db", 5, 1, new(1, 2, 3), new(4, 5, 6)));
        }

        var source = new WaypointState(new(1, 2, 3), SourceProps with { Delay = 2000, MoveType = 1 });
        using var draft = new WaypointDraft(path);
        var effective = draft.Effective("db", 5, 1, source);
        Check(effective.Position == new WaypointPosition(4, 5, 6) && effective.Props == source.Props, "Legacy edit lost the source properties");
        draft.Edit("db", 5, 1, source, effective with { Props = effective.Props with { Delay = 9000 } });
        var net = Assert.Single(draft.NetEdits());
        Check(net.BeforeProps == source.Props && net.AfterProps!.Value.Delay == 9000 && net.After == new WaypointPosition(4, 5, 6), "Legacy and property edits did not combine");
        // The legacy export is unchanged, so moves exported earlier keep their identity.
        var legacy = SqlExport.Build("db", [], [], [], null, [new WaypointEdit("db", 5, 1, new(1, 2, 3), new(4, 5, 6))]);
        Check(legacy.Apply.Contains("UPDATE `waypoint_data` SET `position_x` = 4, `position_y` = 5, `position_z` = 6 WHERE `id` = 5 AND `point` = 1 AND ABS(`position_x` - (1)) < 0.0001 AND ABS(`position_y` - (2)) < 0.0001 AND ABS(`position_z` - (3)) < 0.0001;", StringComparison.Ordinal), "Legacy export changed");
        Check(legacy.Summary[0] == "waypoint path 5 point 1: (1, 2, 3) -> (4, 5, 6)", "Legacy label changed");
    }

    [Fact]
    public void StoresFloatPrecisionSoSqlGuardsMatch()
    {
        // Far from the origin a float's half-ulp exceeds the export tolerance (0.0001), so the draft
        // must store exactly what the FLOAT column will hold.
        var source = Source(7551.9365234375, -8912.123456, 1543.7);
        using var draft = new WaypointDraft(TempDraft());
        draft.Edit("db", 5, 1, source, source with { Position = new(7555.123456789, -8900.987654321, 1544.55555), Props = source.Props with { Orientation = 3.14159265358979, Velocity = 4.123456789 } });
        var after = draft.Effective("db", 5, 1, source);
        Check(after.Position.X == (float)7555.123456789 && after.Position.Y == (float)-8900.987654321 && after.Position.Z == (float)1544.55555, "Position is not float precision");
        Check(after.Props.Orientation == (float)3.14159265358979 && after.Props.Velocity == (float)4.123456789, "Props are not float precision");
    }

    [Fact]
    public void ExportsGuardedUpdatesOfOnlyTheChangedColumns()
    {
        var stamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var move = new WaypointEdit("db", 1234, 7, new(-100.5, 20.25, 3), new(-101.5, 21.25, 4));
        var scripts = SqlExport.Build("db", [], [], [], stamp, [move]);
        Check(scripts.Changes == 1, "Expected one change");
        Check(scripts.Apply.Contains("UPDATE `waypoint_data` SET `position_x` = -101.5, `position_y` = 21.25, `position_z` = 4 WHERE `id` = 1234 AND `point` = 7 AND ABS(`position_x` - (-100.5)) < 0.0001", StringComparison.Ordinal), "Apply statement or guard wrong");
        Check(scripts.Revert.Contains("UPDATE `waypoint_data` SET `position_x` = -100.5, `position_y` = 20.25, `position_z` = 3 WHERE `id` = 1234 AND `point` = 7 AND ABS(`position_x` - (-101.5)) < 0.0001", StringComparison.Ordinal), "Revert statement or guard wrong");
        Check(!scripts.Apply.Contains("orientation", StringComparison.Ordinal) && !scripts.Apply.Contains("delay", StringComparison.Ordinal), "Position export touches other columns");

        var before = SourceProps with { Delay = 1000, Orientation = null };
        var props = new WaypointEdit("db", 1234, 7, new(1, 2, 3), new(1, 2, 3), before, before with { Delay = 4000, Orientation = 1.25, Smooth = true, MoveType = 1, Action = 12, ActionChance = 50, Velocity = 3.5 });
        var edited = SqlExport.Build("db", [], [], [], stamp, [props]);
        Check(edited.Apply.Contains("SET `orientation` = 1.25, `velocity` = 3.5, `delay` = 4000, `smoothTransition` = 1, `move_type` = 1, `action` = 12, `action_chance` = 50 WHERE `id` = 1234 AND `point` = 7 AND `orientation` IS NULL AND ABS(`velocity` - (0)) < 0.0001 AND `delay` = 1000 AND `smoothTransition` = 0 AND `move_type` = 0 AND `action` = 0 AND `action_chance` = 0;", StringComparison.Ordinal), "Property apply statement wrong: " + edited.Apply);
        Check(edited.Revert.Contains("SET `orientation` = NULL, `velocity` = 0, `delay` = 1000", StringComparison.Ordinal) && edited.Revert.Contains("ABS(`orientation` - (1.25)) < 0.0001", StringComparison.Ordinal), "Property revert statement wrong");
        Check(!edited.Apply.Contains("position_x", StringComparison.Ordinal), "Property export touches the position");

        var delayOnly = new WaypointEdit("db", 1234, 7, new(1, 2, 3), new(1, 2, 3), before, before with { Delay = 0 });
        var single = SqlExport.Build("db", [], [], [], stamp, [delayOnly]);
        Check(single.Apply.Contains("SET `delay` = 0 WHERE `id` = 1234 AND `point` = 7 AND `delay` = 1000;", StringComparison.Ordinal), "Single property statement wrong: " + single.Apply);
    }

    [Fact]
    public void PendingChangesListsWaypointsAndRecordsExport()
    {
        var edit = new WaypointEdit("db", 9, 2, new(1, 2, 3), new(4, 5, 6));
        string file = Path.Combine(Path.GetDirectoryName(TempDraft())!, "exported.json");
        var store = new PendingChanges(file);
        var changes = store.Collect([], [], [], [edit]);
        var change = Assert.Single(changes);
        Check(change.Kind == PendingKind.Waypoint && change.Waypoint == edit, "Waypoint change not listed");
        store.MarkExported(changes);
        Check(new PendingChanges(file).Collect([], [], [], [edit]).Count == 0, "Exported waypoint still pending");
        Check(store.Collect([], [], [], [edit with { After = new(7, 8, 9) }]).Count == 1, "Changed waypoint stayed hidden");
    }
}
