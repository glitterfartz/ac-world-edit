# Client data and asset pipeline

Covers `Scripts/Assets/`. Everything here is engine-free: it produces plain CPU data (`byte[]`, records, arrays) that the viewer turns into Godot resources. The folder is compiled directly into the test projects, so it must not reference Godot.

## Source abstraction

```mermaid
classDiagram
    class IClientFiles {
        <<interface>>
        +ClientKind Kind
        +Build
        +Read(path) byte[]
        +Exists(path)
    }
    class AssetStore {
        MPQ set, Wrath 3.3.5
        rejects fdid: references
    }
    class CascFiles {
        CASC via CascLib, Retail
        reads by FileDataID
        optional PathResolver
    }
    class ClientKind {
        <<enum>>
        Wrath335
        Retail
    }
    class AssetRef {
        <<static>>
        fdid:N encoding
        optional .wmo suffix
    }
    IClientFiles <|.. AssetStore
    IClientFiles <|.. CascFiles
    AssetStore --> MpqArchive
    CascFiles --> CascNative
    IClientFiles ..> ClientKind
    IClientFiles ..> AssetRef
```

Key points:

- `IClientFiles` (`Scripts/Assets/Archives/IClientFiles.cs`) is the only file-access interface. Reads are read-only, thread-safe, and return detached `byte[]`. A missing file throws `FileNotFoundException`; a reference the client cannot resolve throws `NotSupportedException`.
- There are two implementations. `AssetStore` (`Archives/AssetStore.cs`) mounts a 3.3.5 archive set and searches archives newest first. `CascFiles` (`Retail/CascFiles.cs`) opens a retail install through `CascLib.dll`.
- `MpqArchive` (`Archives/MpqArchive.cs`) is a managed MPQ v0/v1 reader with zlib and BZip2 (SharpZipLib). StormLib is not used.
- `CascNative` is the P/Invoke surface for `CascLib.dll`. The DLL is located through `ACWORLD_CASCLIB`, next to the assembly, or in `native/win-x64/`. Only the Retail profile needs it.
- `ClientFolder` (`Archives/ClientFolder.cs`) is a validation helper, not a source. It checks that a chosen folder looks like a Retail install (`.build.info`) or a Wrath install (`common.MPQ`).
- `ClientMountProfile` (`Archives/ClientMountProfile.cs`) lists the ordered archives for the supported 3.3.5.12340 stock client (17 MPQs).

### `fdid:` references

Retail files are addressed by FileDataID. `AssetRef` (`Archives/AssetRef.cs`) encodes that as a string `fdid:N`, optionally with a `.wmo` suffix, so existing extension checks still tell buildings from doodads. MPQ paths cannot contain `:`, so the two forms never collide.

`fdid:` strings come from:

- the WDT MAID table (per-tile root and texture FileDataIDs);
- the ADT MDID table (texture FileDataIDs in split terrain);
- MDDF and MODF FileDataID flags (placements);
- M2 SFID and TXID lists;
- WMO GFID and MODI lists, and MOMT texture IDs.

Paths that are still stored by name on retail need the community listfile. `PathListfile` (`Retail/PathListfile.cs`) maps a path to a FileDataID through a 64-bit FNV-1a hash of the lower-cased path with `/` separators.

## Reading a file

```mermaid
flowchart TB
    caller["Caller: WorldMap, M2Model, WmoModel, catalogs, LiquidTextures, ModelLibrary"]
    caller -->|Read path| src{"Which source?"}

    src -->|Wrath AssetStore| w1{"fdid: reference?"}
    w1 -->|yes| wx["NotSupportedException"]
    w1 -->|no| w2["Search archives newest first<br/>MpqArchive hash lookup"]
    w2 -->|found| wout["byte[]"]
    w2 -->|missing| wmiss["FileNotFoundException"]

    src -->|Retail CascFiles| r1{"fdid: reference?"}
    r1 -->|yes, valid| r2["ReadId: CascOpenFile by FileDataID"]
    r1 -->|yes, malformed| rbad["InvalidDataException"]
    r1 -->|plain path| r3["PathResolver<br/>PathListfile FNV-1a lookup"]
    r3 -->|resolved| r2
    r3 -->|unresolved| rmiss["FileNotFoundException"]
    r2 --> rout["byte[] (256 MiB cap, one lock)"]

    caller -->|DB2 table via DBCD| db2["Db2Tables.Provider<br/>StreamForTableName"]
    db2 --> fid["Db2Tables.FileIds table to FileDataID"]
    fid --> rav["CascFiles.ReadAvailableDb2<br/>encrypted sections zero-filled, recorded in PartialDb2Files"]
```

## Client opening

`ClientServices.Open` (`Scripts/Viewer/ClientServices.cs`) returns a `ClientServices` record: `Files`, `Maps`, `Lighting`, `Areas`, `DatabaseEnabled` and a description.

Wrath 3.3.5:

1. Resolve the folder and create `AssetStore`.
2. Read `DBFilesClient/Map.dbc` through `store.Maps()`; keep maps that have a WDT.
3. `ClientLighting.TryLoad` and `AreaLookup.TryLoad` read the DBC tables.
4. The world database is enabled.

Retail:

1. Find WoWDBDefs definitions (`Db2Tables.FindDefinitions`, honours `ACWORLD_DBD_DIR`).
2. Open `CascFiles`.
3. Read maps through `Db2Tables`.
4. If a listfile is present, install `PathListfile.Resolve` as `PathResolver`.
5. Register `RetailLiquids` and call `ClientLighting.TryLoadRetail`.
6. Areas are `null` and the database is disabled for this profile.

If opening fails, the partly opened store is disposed.

## Terrain pipeline

```mermaid
flowchart LR
    wdt["WDT via WorldMap<br/>MPHD flags, MAIN tiles, MAID files"]
    wdt -->|ReadTile| split{"Split terrain?<br/>Retail"}
    split -->|no, Wrath| adt["ADT v18<br/>TerrainTile"]
    split -->|yes, Retail| rt["RetailTerrain<br/>root + _tex0 + _obj0"]
    adt --> tt["TerrainTile<br/>chunks, layers, alpha, holes"]
    rt --> tt
    tt --> geo["TerrainGeometry<br/>145 vertices per chunk"]
    tt --> hgt["TerrainHeights<br/>ground probes"]
    tt --> obj["WorldObjects<br/>MDDF / MODF placements"]
    rt -.->|MH2O if RetailLiquids registered| liq["LiquidData"]
    adt -.->|MCLQ| liq
    tt --> lay["AddLayers<br/>up to 4 blended layers"]
```

Notes:

- **WDT** (`World/WorldMap.cs`): MPHD bit `0x4` (or `0x80` on retail) sets big alpha. MAIN gives tile existence. MAID gives retail per-tile FileDataIDs. Incomplete tiles are counted and skipped.
- **Unsplit ADT** (`World/TerrainData.cs`): requires MVER 18. Each MCNK is found through its header offsets (MCVT, MCNR, MCCV, MCSH, MCLY/MCAL).
- **Split ADT** (`Retail/RetailTerrain.cs`): root and `_tex0` must both be MVER 18 with 256 MCNKs each, paired by order. Sub-chunks are found by walking the chunk stream because split files leave offsets at zero. MDID supplies texture FileDataIDs. Up to 8 layers are read, but only the first 4 are blended; the rest are counted in `DroppedLayers`.
- **Liquids**: retail MH2O is read from the root file only when `RetailLiquids` is registered. MH2O's second `uint16` is a vertex format (below 42) or a LiquidObject ID; 42 means depth only. Wrath uses MCLQ.
- **Holes**: retail high-resolution holes (flag `0x10000`) come from the 8 bytes at offset +20.

## Model pipeline

```mermaid
flowchart LR
    path["Model path or fdid: reference"] --> kind{"WMO?"}
    kind -->|yes| wmo["WmoModel<br/>MVER 17, groups, materials, MLIQ, doodads"]
    kind -->|no| m2["M2Model<br/>MD20 v264 or MD21 with SFID/TXID"]
    wmo --> md["ModelData<br/>engine-free surfaces, bones, tracks"]
    m2 --> md
    md --> anim["AnimationPalette<br/>bone matrices as RGBA float texture"]
    md --> mat["MaterialPalette<br/>batch colour and UV animation"]
    tex["BlpTexture decode"] --> md
```

- **M2** (`Models/M2Model.cs`): on retail the file starts with `MD21`; the MD20 block is extracted with SFID (skins) and TXID (textures). MD20 must be version 272 or later. Retail models are drawn in their reference pose, so `.anim` files are not read.
- **WMO** (`Models/WmoModel.cs`): requires MVER 17. Groups come from GFID on retail or from `<root>_NNN.wmo` names on Wrath. Liquid (MLIQ) goes through `LiquidData`.
- **Shared types** (`Models/ModelData.cs`): `ModelData`, `ModelSurface`, `ModelBone`, `ModelTrack<T>`, `ModelDoodad`, `SurfaceAnimation`, `VertexColorMode`. They carry no engine types, and M2, WMO and liquids all produce them.
- **Palettes** (`Models/AnimationPalette.cs`, `Models/MaterialPalette.cs`): bake animation into textures so the GPU does the skinning and UV animation.

## Tables: DBC and DB2

Two readers exist and share no abstraction:

- `DbcTable` (`Formats/DbcTable.cs`) reads WDBC by fixed field index. Callers hard-code the indexes and document them in comments (for example `AreaLookup.cs` and `InteractionCatalog.cs`).
- `Db2Tables` (`Retail/Db2Tables.cs`) reads through DBCD with WoWDBDefs layouts and accesses fields by column name, for example `row.FieldAs<uint>("WdtFileDataID")`. It caches tables lazily and is not thread-safe. A build the definitions do not cover raises `InvalidDataException`.

Callers choose the reader that matches their client. `ClientLighting` has both `TryLoad` (Wrath) and `TryLoadRetail` (retail).

## Catalogs and side systems

| Type | File | Purpose | Client |
|---|---|---|---|
| `CreatureCatalog` | `Client/CreatureCatalog.cs` | Joins display, model, extra and char-section tables into a model path, skins, scale and textures | Wrath (DBC) |
| `GameObjectCatalog` | `Client/GameObjectCatalog.cs` | Display ID to model path | Wrath (DBC) |
| `InteractionCatalog` | `Client/InteractionCatalog.cs` | Spell, taxi, item cost, emote, faction lookups for the NPC and content editors | Wrath (DBC) |
| `LiquidTextures` | `Client/LiquidTextures.cs` | Liquid texture atlases; uses `RetailLiquids` on retail | Both |
| `ClientLighting`, `LightBand` | `Viewer/ClientLighting.cs`, `World/LightBand.cs` | Time-of-day colour keyframes and light regions | Both |
| `AreaLookup` | `World/AreaLookup.cs`, `Viewer/AreaTable.cs` | Zone and parent names | Wrath only (`null` on retail) |
| `RetailLiquids` | `Retail/RetailLiquids.cs` | LiquidType, LiquidObject and LiquidMaterial as plain dictionaries | Retail |
| `ListfileStore` | `Retail/ListfileStore.cs` | Downloads and validates the wowdev community listfile on request; `ACWORLD_LISTFILE` overrides the path | Retail |

The Wrath-only catalogs read `DBFilesClient/*.dbc` through `IClientFiles`. `ModelLibrary` only builds them when `Kind == Wrath335`.

## Type reference by folder

| Folder | Key types |
|---|---|
| `Archives/` | `IClientFiles`, `ClientKind`, `AssetStore`, `MapInfo`, `MpqArchive`, `ClientMountProfile`, `ClientFolder`, `AssetRef` |
| `Retail/` | `CascFiles`, `RetailInstall`, `CascReadException`, `CascNative`, `ListfileStore`, `PathListfile`, `Db2Tables`, `RetailLiquids`, `RetailTerrain` |
| `Formats/` | `BinaryAsset` (bounds-checked little-endian reader), `Chunks` / `ChunkHeader`, `DbcTable`, `BlpTexture` |
| `Models/` | `ModelData` family, `M2Model`, `WmoModel`, `AnimationPalette`, `MaterialPalette`, `SurfaceMaterial` |
| `World/` | `WorldMap`, `TileFiles`, `TileKey`, `TerrainTile`, `TerrainChunk`, `TerrainLayer`, `TerrainGeometry`, `TerrainHeights`, `WorldObject`, `WorldObjects`, `LiquidData`, `GroundProbe`, `AreaLookup`, `LightBand`, `LightCoordinates` |
| `Client/` | `CreatureCatalog`, `GameObjectCatalog`, `InteractionCatalog`, `LiquidTextures` |

## Dependency directions

```mermaid
flowchart LR
    Archives --> Formats
    Formats --> Archives
    Models --> Formats
    Models --> World
    World --> Retail
    Retail --> World
    Retail --> Models
    Client --> Formats
    Client --> Retail
    World --> Foundation
    Retail --> Foundation
```

The two-way links are deliberate and small: `WorldMap` calls `RetailTerrain`, which builds `TerrainTile` through an internal constructor; `Chunks` uses `MpqArchive.U` (a little-endian read helper), and `AssetStore` uses `DbcTable`.

Per-client side data (`RetailLiquids`, `LiquidTextures`) is attached to an `IClientFiles` instance through `ConditionalWeakTable`, not passed as an argument.
