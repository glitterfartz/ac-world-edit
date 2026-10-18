using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Opt-in check that the selected client profile opens: it waits for the client services, the
/// default map's WDT and, for a terrain map, its first streamed tile, prints one summary line and
/// exits. It needs no database, so it runs headless: <c>--headless -- --client-smoke</c> with
/// ACWORLD_CLIENT_PROFILE and the profile's folder variable set. It proves loading, not visual output.
/// </summary>
public partial class TerrainViewer
{
    bool clientSmoke, clientShot;
    int clientShotFrames;
    double clientSmokeTime;

    void ClientSmoke(double delta)
    {
        clientSmokeTime += delta;
        if (clientSmokeTime > 180)
        {
            SmokeFail("CLIENT_SMOKE_TIMEOUT " + message);
            return;
        }

        if (busy)
        {
            return;
        }

        if (store == null || mapList.Count == 0)
        {
            // LoadClient reports its failure through the status message and then goes idle.
            if (clientSmokeTime > 5 && !message.StartsWith("Opening", StringComparison.Ordinal) && !message.StartsWith("Reading", StringComparison.Ordinal))
            {
                SmokeFail("CLIENT_SMOKE_NO_CLIENT " + message);
            }

            return;
        }

        if (world == null)
        {
            if (message.StartsWith("Map error", StringComparison.Ordinal))
            {
                SmokeFail("CLIENT_SMOKE_MAP_FAILED " + message);
            }

            return;
        }

        // A terrain map must also stream its first tile (worker decode plus main-thread upload); the
        // dummy headless renderer accepts the uploads, so this exercises the whole path but not the output.
        if (world.Tiles.Count > 0 && loaded.Count == 0)
        {
            if (message.StartsWith("Tile error", StringComparison.Ordinal) || message.StartsWith("Render error", StringComparison.Ordinal))
            {
                SmokeFail("CLIENT_SMOKE_TILE_FAILED " + message);
            }

            return;
        }

        // --client-shot additionally waits for the visible terrain to finish, lets a few frames render and
        // saves artifacts/client-shot.png for inspection. It needs a real window (not --headless).
        if (clientShot && DisplayServer.GetName() != "headless")
        {
            if (!TerrainSettled || clientShotFrames++ < 30)
            {
                return;
            }

            GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://artifacts/client-shot.png"));
        }

        GD.Print($"CLIENT_SMOKE_OK kind={store.Kind} build={store.Build} maps={mapList.Count} map={mapList[Math.Max(maps.Selected, 0)].Directory} tiles={world.Tiles.Count} streamed={loaded.Count} textures={renderer.TextureCount} globalWmos={world.GlobalObjects.Count} database={databaseEnabled} catalogs={models.Catalog != null} message=\"{message}\"");
        GetTree().Quit();
    }
}
