using Godot;

namespace ACWorldEdit;

/// <summary>
/// Opt-in --gameobject-smoke: live GameObjects with ready native models, selection, then the full
/// move/turn/undo/export workflow on an isolated draft (see GameObjectOverlay.SmokeEditing).
/// </summary>
public partial class ViewerShell
{
    bool gameObjectSmoke;
    int gameObjectStage, gameObjectWait;
    double gameObjectTime;

    void GameObjectSmoke(double delta)
    {
        gameObjectTime += delta;
        if (gameObjectTime > 240)
        {
            SmokeFail($"GAMEOBJECT_SMOKE_TIMEOUT stage={gameObjectStage} records={gameObjects.Records.Count} models={gameObjects.ModelObjectCount} admitted={gameObjects.AdmittedObjectCount} map={currentMapId} {gameObjects.QueryStatus}");
            return;
        }

        if (gameObjectWait > 0)
        {
            gameObjectWait--;
            return;
        }

        if (busy || world == null || !TerrainSettled || gameObjects.IsLoading || models.Busy)
        {
            return;
        }

        switch (gameObjectStage)
        {
            case 0:
                if (gameObjects.Records.Count == 0 || gameObjects.ModelObjectCount == 0)
                {
                    return;
                }

                var target = gameObjects.SmokeTarget(camera.Position);
                if (target == null)
                {
                    return;
                }

                var position = GameObjectOverlay.ViewPosition(target);
                camera.Position = position + new Vector3(0, 6, 12);
                camera.LookAt(position);
                gameObjects.Select(target.Guid);
                GD.Print($"GAMEOBJECT_SMOKE_SELECTED count={gameObjects.Records.Count} models={gameObjects.ModelObjectCount} guid={target.Guid} display={target.DisplayId}");
                gameObjectStage = 1;
                gameObjectWait = 20;
                break;
            case 1:
                if (DisplayServer.GetName() != "headless")
                {
                    GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/gameobject-preview.png"));
                }

                if (!gameObjects.SmokeHasSelection || !gameObjects.SmokeEditing(camera))
                {
                    GetTree().Quit(1);
                    return;
                }

                GetTree().Quit();
                break;
        }
    }
}
