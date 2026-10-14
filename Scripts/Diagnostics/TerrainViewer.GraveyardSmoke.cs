using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Opt-in --graveyard-smoke: live read-only graveyard markers in Elwynn, a real viewport click on an
/// icon, list/viewport agreement, the full editing workflow on an isolated draft, layer hiding that
/// stops queries, and a map switch that discards stale results. A graphical run also saves
/// artifacts/graveyard-preview.png.
/// </summary>
public partial class TerrainViewer
{
    bool graveyardSmoke;
    int graveyardStage, graveyardWait;
    double graveyardTime;
    uint graveyardTarget;

    void GraveyardSmoke(double delta)
    {
        graveyardTime += delta;
        if (graveyardTime > 240)
        {
            SmokeFail($"GRAVEYARD_SMOKE_TIMEOUT stage={graveyardStage} {graveyards.QueryStatus}");
            return;
        }

        if (graveyardWait > 0)
        {
            graveyardWait--;
            return;
        }

        bool settled = !busy && world != null && TerrainSettled;
        if (!settled)
        {
            return;
        }

        switch (graveyardStage)
        {
            case 0:
                if (currentMapId != 0 || !graveyards.SmokeQueryIdle || graveyards.Sites.Count == 0)
                {
                    return;
                }

                var nearest = graveyards.Sites.OrderBy(s => graveyards.PositionOf(s.Record.Id)!.Value.DistanceSquaredTo(camera.Position)).First();
                graveyardTarget = nearest.Record.Id;
                FocusPose(new(nearest.Record.Map, nearest.Record.X, nearest.Record.Y, nearest.Record.Z, 0));
                GD.Print($"GRAVEYARD_SMOKE_TARGET id={graveyardTarget} '{nearest.Record.Comment}' links={nearest.Record.Links.Length} healers={nearest.Healers.Length} sites={graveyards.Sites.Count}");
                graveyardStage = 1;
                graveyardWait = 20;
                break;
            case 1:
                if (graveyards.SmokeIconPosition(graveyardTarget) is not Vector3 icon || camera.IsPositionBehind(icon))
                {
                    SmokeFail("GRAVEYARD_SMOKE_ICON_NOT_VISIBLE");
                    return;
                }

                // A real click through the viewport picking path, not a direct Select call.
                var screen = camera.UnprojectPosition(icon);
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = screen, GlobalPosition = screen });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = screen, GlobalPosition = screen });
                graveyardStage = 2;
                graveyardWait = 3;
                break;
            case 2:
                if (graveyards.SelectedId != graveyardTarget || graveyards.SmokeListSelection != graveyardTarget || creatures.SelectedGuid.HasValue || gameObjects.SelectedGuid.HasValue)
                {
                    SmokeFail($"GRAVEYARD_SMOKE_PICK_FAILED selected={graveyards.SelectedId} list={graveyards.SmokeListSelection}");
                    return;
                }

                GD.Print("GRAVEYARD_SMOKE_PICK_PASSED viewport click and list agree; other selections cleared");
                if (DisplayServer.GetName() != "headless")
                {
                    GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/graveyard-preview.png"));
                }

                graveyardStage = 3;
                break;
            case 3:
                if (!graveyards.SmokeEditing(camera))
                {
                    GetTree().Quit(1);
                    return;
                }

                graveyards.SetLayerEnabled(false);
                graveyardStage = 4;
                graveyardWait = 10;
                break;
            case 4:
                if (graveyards.SmokeVisibleMarkers != 0 || graveyards.Sites.Count != 0 || graveyards.IsLoading || graveyards.HasSelection)
                {
                    SmokeFail("GRAVEYARD_SMOKE_LAYER_HIDE_FAILED");
                    return;
                }

                GD.Print("GRAVEYARD_SMOKE_LAYER_PASSED hidden layer has no markers, selection or queries");
                graveyards.SetLayerEnabled(true);
                int kalimdor = mapList.FindIndex(m => m.Directory == "Kalimdor");
                maps.Select(kalimdor);
                tileX.Value = 32;
                tileY.Value = 32;
                _ = OpenMap();
                graveyardStage = 5;
                graveyardWait = 5;
                break;
            case 5:
                if (currentMapId != 1 || !graveyards.SmokeQueryIdle)
                {
                    return;
                }

                if (graveyards.Sites.Any(s => s.Record.Map != 1))
                {
                    SmokeFail("GRAVEYARD_SMOKE_STALE_MAP");
                    return;
                }

                GD.Print($"GRAVEYARD_SMOKE_PASSED map-switch sites={graveyards.Sites.Count}");
                GetTree().Quit();
                break;
        }
    }
}
