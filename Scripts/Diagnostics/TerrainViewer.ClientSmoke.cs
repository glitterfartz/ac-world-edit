using Godot;
using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Opt-in check that the selected client profile opens: it waits for the client services and the
/// default map's WDT, prints one summary line and exits. It needs no rendering and no database, so
/// it runs headless: <c>--headless -- --client-smoke</c> with ACWORLD_CLIENT_PROFILE and the
/// profile's folder variable set. It proves loading, not visual output.
/// </summary>
public partial class TerrainViewer
{
    bool clientSmoke;
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

        GD.Print($"CLIENT_SMOKE_OK kind={store.Kind} build={store.Build} maps={mapList.Count} map={mapList[Math.Max(maps.Selected, 0)].Directory} tiles={world.Tiles.Count} globalWmos={world.GlobalObjects.Count} database={databaseEnabled} catalogs={models.Catalog != null} message=\"{message}\"");
        GetTree().Quit();
    }
}
