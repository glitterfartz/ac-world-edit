namespace ACWorldEdit.Assets;
/// <summary>Native GO display paths; database GameObjects are distinct from ADT scenery placements.</summary>
public sealed class GameObjectCatalog(IClientFiles store)
{
    readonly DbcTable displays = new(store.Read("DBFilesClient/GameObjectDisplayInfo.dbc"));
    public string Resolve(uint displayId)
    {
        string path = displays.Str(displayId, 1).Replace('\\', '/');
        if (path.Length == 0)
        {
            throw new InvalidDataException("This GameObject display has no client model.");
        }

        return BinaryAsset.ModelPath(path);
    }
}
