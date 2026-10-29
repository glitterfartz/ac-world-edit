using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Opt-in check that a game_tele location loads cleanly: teleport to <c>--tele-name=</c> through the
/// dropdown's own path, wait until terrain, models and every overlay query have settled, then print
/// the totals. Warnings emitted between TELE_SMOKE_START and TELE_SMOKE_DONE belong to the load.
/// </summary>
public partial class TerrainViewer
{
    bool teleSmoke, teleSmokeAll;
    string teleSmokeName = "";
    TileKey? teleSmokeTile;
    bool teleSmokeMoved;
    int teleSmokeStage, teleSmokeSettled;
    double teleSmokeTime;

    void ConfigureTeleSmoke(HashSet<string> flags)
    {
        teleSmoke = flags.Contains("--tele-smoke");
        teleSmokeAll = flags.Contains("--tele-all");
        if (flags.FirstOrDefault(f => f.StartsWith("--tele-name=", StringComparison.Ordinal)) is { } nameFlag)
        {
            teleSmokeName = nameFlag["--tele-name=".Length..];
        }

        // --tele-tile=X,Y flies over that tile once the teleport has settled, then waits for it again.
        if (flags.FirstOrDefault(f => f.StartsWith("--tele-tile=", StringComparison.Ordinal)) is { } tileFlag)
        {
            var parts = tileFlag["--tele-tile=".Length..].Split(',');
            teleSmokeTile = new TileKey(int.Parse(parts[0]), int.Parse(parts[1]));
        }
    }

    bool OverlaysLoading => creatures.IsLoading || gameObjects.IsLoading || graveyards.IsLoading || areaTriggers.IsLoading || waypoints.IsLoading;

    void TeleSmoke(double delta)
    {
        teleSmokeTime += delta;
        if (teleSmokeTime > 600)
        {
            SmokeFail("TELE_SMOKE", $"timeout stage={teleSmokeStage} map={currentMapId} tiles={loaded.Count}/{wanted.Count} modelsBusy={models.Busy} overlays={OverlaysLoading} {message}");
            return;
        }

        if (busy || world == null || teleportList.Length == 0)
        {
            return;
        }

        if (teleSmokeStage == 0)
        {
            int index = Array.FindIndex(teleportList, t => t.Name == teleSmokeName);
            if (index < 0)
            {
                SmokeFail("TELE_SMOKE", $"no game_tele named '{teleSmokeName}' among {teleportList.Length}");
                return;
            }

            if (teleSmokeAll)
            {
                // Every stored spawn, including phased, helper and conditional ones, so hidden objects load too.
                var all = Foundation.SpawnPreview.AllStored with { Dialect = profile.Kind == ClientKind.Retail ? Foundation.WorldDialect.TrinityCore : Foundation.WorldDialect.AzerothCore, DifficultyId = Foundation.SpawnPreview.AnyDifficulty, ViewDistance = 400 };
                creatures.SetPreview(all);
                gameObjects.SetPreview(all);
            }

            GD.Print($"TELE_SMOKE_START name={teleSmokeName} map={teleportList[index].Map}");
            _ = TeleportTo(index + 1);
            teleSmokeStage = 1;
            return;
        }

        bool settled = currentMapId == teleportList.First(t => t.Name == teleSmokeName).Map
            && (teleSmokeMoved ? wanted.All(loaded.ContainsKey) && pending == null && building == null : TerrainSettled) && !models.Busy && !OverlaysLoading;
        teleSmokeSettled = settled ? teleSmokeSettled + 1 : 0;
        if (teleSmokeSettled < 120)
        {
            return;
        }

        if (teleSmokeTile is { } target && !teleSmokeMoved)
        {
            teleSmokeMoved = true;
            teleSmokeSettled = 0;
            Teleport(target);
            return;
        }

        if (DisplayServer.GetName() != "headless")
        {
            System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://artifacts"));
            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/tele-smoke.png"));
        }

        GD.Print($"TELE_SMOKE_DONE map={currentMapId} tiles={loaded.Count} models={models.ReadyModels} failedModels={models.ActiveFailedModels} objects={objects.ObjectCount} creatures={creatures.Records.Count} gameobjects={gameObjects.Records.Count} message={message}");
        GetTree().Quit();
    }
}
