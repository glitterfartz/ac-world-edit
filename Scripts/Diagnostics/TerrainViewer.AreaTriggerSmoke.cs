using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Opt-in --areatrigger-smoke: open the smoke trigger's map, show the AreaTrigger layer, select the
/// trigger with a real viewport click, check list/inspector agreement and that the placed volume
/// matches the server's membership test, open its behavior from the inspector, add and undo a rule in
/// the isolated smoke content draft, hide the layer, then switch maps and reject stale volumes.
/// AzerothCore uses trigger 5140 in Utgarde Pinnacle (map 575, Svala's intro, SmartTrigger);
/// TrinityCore uses server spawn 290 in Stormwind (a box running custom template 188's rules through
/// SmartAreaTriggerAI). A graphical run saves artifacts/areatrigger-preview.png and
/// areatrigger-behavior.png.
/// </summary>
public partial class TerrainViewer
{
    /// <summary>The trigger the smoke selects, where it is, and the rules and script it should find.</summary>
    sealed record AreaTriggerSmokeTarget(AreaTriggerKey Key, ushort Map, int TileX, int TileY, SmartScope Scope, string Script);

    AreaTriggerSmokeTarget SmokeTarget => profile.Dialect == WorldDialect.TrinityCore
        ? new(new(true, 290), 0, 31, 47, new(188, SmartScope.AreaTriggerEntityCustom), AreaTriggerRecord.SmartEntity)
        : new(new(false, 5140), 575, 32, 32, new(5140, SmartScope.AreaTrigger), AreaTriggerRecord.SmartTrigger);

    bool areaTriggerSmoke, areaTriggerUndone;
    int areaTriggerStage, areaTriggerWait, areaTriggerUndoFrames;
    double areaTriggerTime;

    void AreaTriggerSmoke(double delta)
    {
        areaTriggerTime += delta;
        bool settled = !busy && world != null && TerrainSettled;
        if (areaTriggerTime > 180)
        {
            SmokeFail("AREATRIGGER", $"timeout stage={areaTriggerStage} busy={busy} tiles={loaded.Count}/{wanted.Count} failed={failed.Count} tileWorker={pending != null || building != null} query={areaTriggers.QueryStatus} {message}");
            return;
        }

        if (areaTriggerWait > 0)
        {
            areaTriggerWait--;
            return;
        }

        // Only the first map load waits for streaming to settle. Later stages do not depend on
        // terrain (moving the camera restarts tile streaming, which can take minutes in a large
        // instance); opening another map only needs the viewer to be idle, since OpenMap is a
        // no-op while busy.
        if (areaTriggerStage is 0 or 1 ? !settled : busy)
        {
            return;
        }

        var goal = SmokeTarget;
        var scope = goal.Scope;
        switch (areaTriggerStage)
        {
            case 0:
                if (currentMapId != 0)
                {
                    return;
                }

                areaTriggers.SetLayerEnabled(true);
                maps.Select(mapList.FindIndex(m => m.Id == goal.Map));
                tileX.Value = goal.TileX;
                tileY.Value = goal.TileY;
                _ = OpenMap();
                areaTriggerStage = 1;
                areaTriggerWait = 5;
                break;
            case 1:
                if (currentMapId != goal.Map || !areaTriggers.SmokeQueryIdle || areaTriggers.Triggers.All(t => t.Key != goal.Key))
                {
                    return;
                }

                // Stand outside the box (5140 is 37 x 14 x 35 yd, spawn 290 54 x 54 x 80; FocusPose's
                // 15 yd offset lands inside, where picking deliberately ignores the volume) and look at
                // its center.
                var trigger = areaTriggers.Triggers.First(t => t.Key == goal.Key);
                var center = AreaTriggerOverlay.ViewPosition(trigger);
                camera.Position = center + new Vector3(0, 30, 70);
                camera.LookAt(center);
                yaw = camera.Rotation.Y;
                pitch = camera.Rotation.X;
                UpdateWanted();
                GD.Print($"AREATRIGGER_SMOKE_TARGET {goal.Key} {trigger.Shape} triggers={areaTriggers.Triggers.Count} {areaTriggers.QueryStatus}");
                areaTriggerStage = 2;
                areaTriggerWait = 30;
                break;
            case 2:
                {
                    var target = areaTriggers.Triggers.First(t => t.Key == goal.Key);
                    // Input events carry window pixels; the viewport may be scaled to the window (UI scale).
                    var screen = GetViewport().GetFinalTransform() * camera.UnprojectPosition(AreaTriggerOverlay.ViewPosition(target));
                    // A real click through the viewport picking path, not a direct Select call.
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = screen, GlobalPosition = screen });
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = screen, GlobalPosition = screen });
                    areaTriggerStage = 3;
                    areaTriggerWait = 3;
                    break;
                }
            case 3:
                {
                    if (areaTriggers.Selected?.Key != goal.Key || areaTriggers.SmokeListSelection != goal.Key || creatures.SelectedGuid.HasValue || gameObjects.SelectedGuid.HasValue || graveyards.HasSelection || !areaTriggers.SmokeHasOutline)
                    {
                        SmokeFail("AREATRIGGER", $"pick selected={areaTriggers.Selected?.Key} list={areaTriggers.SmokeListSelection} outline={areaTriggers.SmokeHasOutline}");
                        return;
                    }

                    if (!areaTriggers.SmokeDetails.Contains(goal.Script, StringComparison.Ordinal))
                    {
                        SmokeFail("AREATRIGGER", $"inspector does not show the {goal.Script} link: " + areaTriggers.SmokeDetails);
                        return;
                    }

                    // The rendered node, not a recomputation: unit-cube corners and face centers just
                    // inside/outside must agree with Position::IsWithinBox on the source row.
                    var record = areaTriggers.Triggers.First(t => t.Key == goal.Key);
                    var transform = areaTriggers.SmokeVolumeTransform(goal.Key)!.Value;
                    foreach (var direction in new[] { Vector3.Right, Vector3.Up, Vector3.Back, Vector3.Left, Vector3.Forward, new Vector3(1, 1, 1), new Vector3(-1, -1, 1) })
                    {
                        foreach (var (factor, inside) in new[] { (0.49f, true), (0.52f, false) })
                        {
                            var world = transform * (direction * factor);
                            var server = WorldCoordinates.ToServer(new(world.X, world.Y, world.Z));
                            if (record.Contains(server) != inside)
                            {
                                SmokeFail("AREATRIGGER", $"rendered volume disagrees with the server test at {direction}*{factor}");
                                return;
                            }
                        }
                    }

                    GD.Print("AREATRIGGER_SMOKE_PICK_PASSED viewport click, list, inspector link, outline and rendered box agree with the server test");
                    if (DisplayServer.GetName() != "headless")
                    {
                        GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/areatrigger-preview.png"));
                    }

                    areaTriggers.SmokePressBehavior();
                    areaTriggerStage = 4;
                    areaTriggerWait = 5;
                    break;
                }
            case 4:
                {
                    if (areaBehavior.Panel.IsLoading || !areaBehavior.Panel.IsAreaTrigger || areaBehavior.Panel.Entry != scope.EntryOrGuid || areaBehavior.Panel.SmokeRunningScript != goal.Script)
                    {
                        return;
                    }

                    // TrinityCore's live rule links to a rule that does not exist; only that error is expected.
                    var rules = areaBehavior.Panel.SmokeRules(scope);
                    if (rules.Count != 1 || !areaBehavior.Panel.SmokeDescribe(rules[0]).Contains("enters the", StringComparison.Ordinal) || areaBehavior.Panel.Issues.Any(i => i.Error && !i.Message.Contains("which does not exist", StringComparison.Ordinal)))
                    {
                        SmokeFail("AREATRIGGER", $"behavior rules={rules.Count} issues={string.Join("; ", areaBehavior.Panel.Issues.Select(i => i.Message))}");
                        return;
                    }

                    GD.Print("AREATRIGGER_SMOKE_BEHAVIOR " + areaBehavior.Panel.SmokeDescribe(rules[0]));
                    // "Cast a spell on the player who entered" from the AreaTrigger templates.
                    areaBehavior.Panel.SmokeAddRule(2);
                    var added = areaBehavior.Panel.SmokeRules(scope);
                    if (added.Count != 2 || added.Any(r => r.Event != SmartCatalog.AreaTriggerEvent) || added.Last().Target != 7)
                    {
                        SmokeFail("AREATRIGGER", $"template rule not drafted: {areaBehavior.Panel.SmokeStatus}");
                        return;
                    }

                    areaTriggerStage = 5;
                    areaTriggerWait = 3;
                    break;
                }
            case 5:
                if (!areaTriggerUndone)
                {
                    // A few frames after the edit, so the capture shows the re-rendered window.
                    if (DisplayServer.GetName() != "headless")
                    {
                        GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/areatrigger-behavior.png"));
                    }

                    areaBehavior.Panel.SmokeHistory(false);
                    areaTriggerUndone = true;
                    areaTriggerWait = 5;
                    return;
                }

                if (areaBehavior.Panel.IsLoading)
                {
                    return;
                }

                // The undo can start a reload from a remote database; give it a few seconds to settle.
                if (areaBehavior.Panel.SmokeRules(scope).Count != 1)
                {
                    if (++areaTriggerUndoFrames < 300)
                    {
                        return;
                    }

                    SmokeFail("AREATRIGGER", $"undo did not remove the drafted rule: {areaBehavior.Panel.SmokeRules(scope).Count} rules, status {areaBehavior.Panel.SmokeStatus}");
                    return;
                }

                GD.Print("AREATRIGGER_SMOKE_DRAFT_PASSED template rule added and undone in the isolated content draft");
                areaBehavior.Visible = false;
                areaTriggers.SetLayerEnabled(false);
                areaTriggerStage = 6;
                areaTriggerWait = 10;
                break;
            case 6:
                if (areaTriggers.SmokeVisibleVolumes != 0 || areaTriggers.Triggers.Count != 0 || areaTriggers.IsLoading || areaTriggers.HasSelection)
                {
                    SmokeFail("AREATRIGGER", "hidden layer kept volumes, selection or queries");
                    return;
                }

                GD.Print("AREATRIGGER_SMOKE_LAYER_PASSED hidden layer has no volumes, selection or queries");
                areaTriggers.SetLayerEnabled(true);
                maps.Select(mapList.FindIndex(m => m.Directory == "Kalimdor"));
                tileX.Value = 32;
                tileY.Value = 32;
                _ = OpenMap();
                areaTriggerStage = 7;
                areaTriggerWait = 5;
                break;
            case 7:
                if (currentMapId != 1 || !areaTriggers.SmokeQueryIdle)
                {
                    return;
                }

                if (areaTriggers.Triggers.Any(t => t.Map != 1))
                {
                    SmokeFail("AREATRIGGER", "stale triggers from the previous map");
                    return;
                }

                GD.Print($"AREATRIGGER_SMOKE_PASSED map-switch triggers={areaTriggers.Triggers.Count}");
                GetTree().Quit();
                break;
        }
    }
}
