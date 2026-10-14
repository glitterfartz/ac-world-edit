using Godot;
using ACWorldEdit.Foundation;

namespace ACWorldEdit;

/// <summary>
/// Opt-in --areatrigger-smoke: open Utgarde Pinnacle (map 575), show the AreaTrigger layer, select
/// trigger 5140 (Svala's intro) with a real viewport click, check list/inspector agreement and that
/// the placed volume matches the server's membership test, open its behavior from the inspector,
/// add and undo a rule in the isolated smoke content draft, hide the layer, then switch maps and
/// reject stale volumes. A graphical run saves artifacts/areatrigger-preview.png and
/// areatrigger-behavior.png.
/// </summary>
public partial class TerrainViewer
{
    const uint SmokeTrigger = 5140;
    bool areaTriggerSmoke, areaTriggerUndone;
    int areaTriggerStage, areaTriggerWait;
    double areaTriggerTime;

    void AreaTriggerSmokeFail(string message)
    {
        GD.PushError("AREATRIGGER_SMOKE_FAILED " + message);
        GetTree().Quit(1);
    }

    void AreaTriggerSmoke(double delta)
    {
        areaTriggerTime += delta;
        bool settled = !busy && world != null && loaded.Count == wanted.Count && pending == null && building == null;
        if (areaTriggerTime > 180)
        {
            AreaTriggerSmokeFail($"timeout stage={areaTriggerStage} busy={busy} tiles={loaded.Count}/{wanted.Count} failed={failed.Count} tileWorker={pending != null || building != null} query={areaTriggers.QueryStatus} {message}");
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

        var scope = new SmartScope((int)SmokeTrigger, SmartScope.AreaTrigger);
        switch (areaTriggerStage)
        {
            case 0:
                if (currentMapId != 0)
                {
                    return;
                }

                areaTriggers.SetLayerEnabled(true);
                maps.Select(mapList.FindIndex(m => m.Id == 575));
                tileX.Value = 32;
                tileY.Value = 32;
                _ = OpenMap();
                areaTriggerStage = 1;
                areaTriggerWait = 5;
                break;
            case 1:
                if (currentMapId != 575 || !areaTriggers.SmokeQueryIdle || areaTriggers.Triggers.All(t => t.Entry != SmokeTrigger))
                {
                    return;
                }

                // Stand outside the 37 x 14 x 35 yd box (FocusPose's 15 yd offset lands inside it,
                // where picking deliberately ignores the volume) and look at its center.
                var trigger = areaTriggers.Triggers.First(t => t.Entry == SmokeTrigger);
                var center = AreaTriggerOverlay.ViewPosition(trigger);
                camera.Position = center + new Vector3(0, 30, 70);
                camera.LookAt(center);
                yaw = camera.Rotation.Y;
                pitch = camera.Rotation.X;
                UpdateWanted();
                GD.Print($"AREATRIGGER_SMOKE_TARGET {SmokeTrigger} {trigger.Shape} triggers={areaTriggers.Triggers.Count} {areaTriggers.QueryStatus}");
                areaTriggerStage = 2;
                areaTriggerWait = 30;
                break;
            case 2:
                {
                    var target = areaTriggers.Triggers.First(t => t.Entry == SmokeTrigger);
                    var screen = camera.UnprojectPosition(AreaTriggerOverlay.ViewPosition(target));
                    // A real click through the viewport picking path, not a direct Select call.
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = screen, GlobalPosition = screen });
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = screen, GlobalPosition = screen });
                    areaTriggerStage = 3;
                    areaTriggerWait = 3;
                    break;
                }
            case 3:
                {
                    if (areaTriggers.SelectedEntry != SmokeTrigger || areaTriggers.SmokeListSelection != SmokeTrigger || creatures.SelectedGuid.HasValue || gameObjects.SelectedGuid.HasValue || graveyards.HasSelection || !areaTriggers.SmokeHasOutline)
                    {
                        AreaTriggerSmokeFail($"pick selected={areaTriggers.SelectedEntry} list={areaTriggers.SmokeListSelection} outline={areaTriggers.SmokeHasOutline}");
                        return;
                    }

                    if (!areaTriggers.SmokeDetails.Contains("SmartTrigger", StringComparison.Ordinal))
                    {
                        AreaTriggerSmokeFail("inspector does not show the SmartTrigger link: " + areaTriggers.SmokeDetails);
                        return;
                    }

                    // The rendered node, not a recomputation: unit-cube corners and face centers just
                    // inside/outside must agree with Position::IsWithinBox on the source row.
                    var record = areaTriggers.Triggers.First(t => t.Entry == SmokeTrigger);
                    var transform = areaTriggers.SmokeVolumeTransform(SmokeTrigger)!.Value;
                    foreach (var direction in new[] { Vector3.Right, Vector3.Up, Vector3.Back, Vector3.Left, Vector3.Forward, new Vector3(1, 1, 1), new Vector3(-1, -1, 1) })
                    {
                        foreach (var (factor, inside) in new[] { (0.49f, true), (0.52f, false) })
                        {
                            var world = transform * (direction * factor);
                            var server = WorldCoordinates.ToServer(new(world.X, world.Y, world.Z));
                            if (record.Contains(server) != inside)
                            {
                                AreaTriggerSmokeFail($"rendered volume disagrees with the server test at {direction}*{factor}");
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
                    if (behaviorEditor.IsLoading || !behaviorEditor.IsAreaTrigger || behaviorEditor.Entry != SmokeTrigger || behaviorEditor.SmokeScriptName != AreaTriggerRecord.SmartTrigger)
                    {
                        return;
                    }

                    var rules = behaviorEditor.SmokeRules(scope);
                    if (rules.Count != 1 || !behaviorEditor.SmokeDescribe(rules[0]).Contains("enters the trigger", StringComparison.Ordinal) || behaviorEditor.Issues.Any(i => i.Error))
                    {
                        AreaTriggerSmokeFail($"behavior rules={rules.Count} issues={string.Join("; ", behaviorEditor.Issues.Select(i => i.Message))}");
                        return;
                    }

                    GD.Print("AREATRIGGER_SMOKE_BEHAVIOR " + behaviorEditor.SmokeDescribe(rules[0]));
                    // "Cast a spell on the player who entered" from the AreaTrigger templates.
                    behaviorEditor.SmokeAddRule(2);
                    var added = behaviorEditor.SmokeRules(scope);
                    if (added.Count != 2 || added.Any(r => r.Event != SmartCatalog.AreaTriggerEvent) || added.Last().Target != 7)
                    {
                        AreaTriggerSmokeFail($"template rule not drafted: {behaviorEditor.SmokeStatus}");
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

                    behaviorEditor.SmokeHistory(false);
                    areaTriggerUndone = true;
                    areaTriggerWait = 5;
                    return;
                }

                if (behaviorEditor.IsLoading)
                {
                    return;
                }

                if (behaviorEditor.SmokeRules(scope).Count != 1)
                {
                    AreaTriggerSmokeFail("undo did not remove the drafted rule");
                    return;
                }

                GD.Print("AREATRIGGER_SMOKE_DRAFT_PASSED template rule added and undone in the isolated content draft");
                behaviorEditor.Visible = false;
                areaTriggers.SetLayerEnabled(false);
                areaTriggerStage = 6;
                areaTriggerWait = 10;
                break;
            case 6:
                if (areaTriggers.SmokeVisibleVolumes != 0 || areaTriggers.Triggers.Count != 0 || areaTriggers.IsLoading || areaTriggers.HasSelection)
                {
                    AreaTriggerSmokeFail("hidden layer kept volumes, selection or queries");
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
                    AreaTriggerSmokeFail("stale triggers from the previous map");
                    return;
                }

                GD.Print($"AREATRIGGER_SMOKE_PASSED map-switch triggers={areaTriggers.Triggers.Count}");
                GetTree().Quit();
                break;
        }
    }
}
