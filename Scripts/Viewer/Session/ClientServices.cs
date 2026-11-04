using ACWorldEdit.Assets;

namespace ACWorldEdit;

/// <summary>
/// Everything the viewer needs from one opened client, chosen once at load time so no parser has to
/// branch on the client version. Lighting and area names are null where a client family does not
/// provide them yet; the creature and GameObject catalogs read DBC (Wrath) or DB2 (retail) display tables; Lookup serves the DB2 reference data (trigger volumes, texts, item and spell names) a retail world database lacks; DatabaseEnabled is false when the world database cannot describe that client.
/// Open runs on a worker: it reads archives and tables but creates no Godot resources.
/// </summary>
public sealed partial record ClientServices(IClientFiles Files, List<MapInfo> Maps, ClientLighting? Lighting, AreaLookup? Areas, bool DatabaseEnabled, string Description, ICreatureCatalog? Creatures = null, IGameObjectCatalog? GameObjects = null, IClientLookup? Lookup = null, IReadOnlyDictionary<int, int>? NavigationParents = null)
{
}
