using ACWorldEdit.Assets;
using ACWorldEdit.Data;

namespace ACWorldEdit;

public partial class TerrainViewer
{
    /// <summary>
    /// Layer the server's creature display hotfixes over the retail client's display table, so a
    /// creature the server re-skinned is drawn as the live client would draw it. Read once per client
    /// load, before any creature asks for a display; a hotfixes database that cannot be read leaves the
    /// client rows in place and says why.
    /// </summary>
    async Task ApplyDisplayHotfixes(RetailCreatureCatalog catalog)
    {
        var source = new TcHotfixSource(DatabaseConnection().HotfixesSettings);
        if (!source.Available)
        {
            return;
        }

        try
        {
            var (displays, modelFiles) = await Task.Run(() => source.ReadCreatureDisplaysAsync(CancellationToken.None));
            catalog.Apply(displays, modelFiles);
        }
        catch (Exception error)
        {
            statusLog.Post("Creature display hotfixes were not applied: " + DbErrors.Safe(error));
        }
    }
}
