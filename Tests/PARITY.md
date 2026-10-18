# Legacy AssetChecks → xUnit parity (record of the port)

Legacy bodies were ported unchanged (same `Check` assertions, same queries) and wrapped in xUnit facts. Each legacy `*_PASSED` summary line was compared between the legacy run and the xUnit run (`--logger console;verbosity=detailed`): identical for all of them. Two lines differed only because parallel xUnit tests interleaved console output (Liquid's `Console.Write` prefix landed on the same line); content identical.

| Legacy flag / file | xUnit test (Tests/ACWorldEdit.Tests) | Trait / skip |
|---|---|---|
| `--draft` CreatureDraftChecks | Drafts/CreatureDraftTests.Passes | unit |
| `--draft` GraveyardDraftChecks | Drafts/GraveyardDraftTests.Passes | unit |
| `--draft` InteractionDraftChecks | Drafts/InteractionDraftTests.Passes | unit |
| `--draft` ContentDraftChecks (categories, quest, area trigger, smart) | Drafts/ContentDraftTests.Passes | unit |
| `--draft` SqlExportChecks | Sql/SqlExportTests.Passes | unit |
| `--placement` PlacementChecks | Placement/PlacementTests.Passes | unit |
| default: malformed inputs (7 Reject lines) | Assets/MalformedInputTests (7 facts) | unit (now independent of client data) |
| default: synthetic golden ADT + duplicate chunk index | MalformedInputTests.SyntheticGoldenAdtParses / DuplicateChunkIndexRejected | unit |
| default: alpha nibble order + RLE | MalformedInputTests.AlphaNibbleOrderAndRle | unit |
| default: geometry topology/pick/holes/boundary/full-hole | Assets/TerrainGeometryTests (3 facts) | unit |
| default: Map.dbc, WDT corpus (105 terrain + 1 unsupported) | Corpus/WorldCorpusTests.MapsAndWdtCorpus | Corpus, ClientData |
| default: per-continent tile, 256 MCNK anchors, textures | WorldCorpusTests.ContinentTileAndTextures (4 theory rows) | Corpus, ClientData |
| default: backup archive not mounted | WorldCorpusTests.BackupArchivesNotMounted | Corpus, ClientData |
| default: StormLib byte differential (2nd arg) | WorldCorpusTests via `STORMLIB_DLL` env (see Run-StormLibChecks.py) | Corpus, optional |
| `--models` ModelChecks (+ native clips, placement bounds, material animation) | Corpus/ModelCorpusTests.Passes | Corpus, ClientData |
| `--liquids` LiquidChecks | Corpus/LiquidCorpusTests.Passes | Corpus, ClientData |
| `--gameobjects` | Db/GameObjectDbTests.Passes | Corpus, Db, ClientData |
| `--spawns` | Db/SpawnFilterDbTests.Passes | Db |
| `--areatriggers` | Db/AreaTriggerDbTests.Passes | Db |
| `--graveyards` | Db/GraveyardDbTests.Passes | Db, ClientData |
| `--interactions` | Db/InteractionDbTests.Passes | Db, ClientData |
| `--content` | Db/ContentDbTests.Passes | Db, ClientData |

Result: legacy 11/11 invocations exit 0; xUnit 33/33 pass with ACWORLD_DB_INFO set and data present.
