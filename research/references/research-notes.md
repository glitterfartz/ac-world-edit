# Research notes and reproducibility

## Scope and method
This research inspected the viewer source, AzerothCore loaders/update mechanisms, client inventory, read-only test schema, and primary upstream documentation. All created files are under `X:\AzerothCore\research`. No project/server/client files or database rows were edited; no server was started or compiled, no live database connected, and no deployments occurred.

The viewer directory is not a Git checkout according to `git status --short`; its inspected files are recorded by SHA256 manifests. AzerothCore HEAD is `5ddc706b7fdcbda01164862652a9b61d60e9a8e0`. No claim is made that that repository's working tree is clean.

## Client correction and observed results
Initially the workspace `wow-data` junction referenced a modified HD client. A read-only execution of the existing AssetChecks binary found 143 archives/102 maps and failed while parsing the first continent WDT with a truncated chunk header. The user identified the HD client and corrected the junction to `X:\AzerothCore\OGClient\Data`. The corrected junction was inspected and the same binary rerun successfully.

**Current state:** both direct OGClient Data and corrected wow-data paths open 18 archives and discover 101 maps. The previous failure is historical profile incompatibility, not a current blocker or a verified stock-client parser defect. Exact failing asset provenance/root cause was not diagnosed before the correction; do not claim the HD archive itself was corrupt.

Client executable file version is `3, 3, 5, 12340`; SHA256 `AA63A5750D60EF16746C686B3D5E26876D98953EAB08B1C026CD0FAF78E88CB8`. This establishes executable metadata, not every asset's authenticity. Archive inventory includes stock-style common, expansion, lichking, patch and enUS locale files, including backup-enUS; explicit mount selection is still useful.

## Read-only checks executed
Command, with absolute paths substituted:
```powershell
dotnet <viewer>/Tests/bin/Debug/net8.0/AssetChecks.dll <client>/Data
dotnet <viewer>/Tests/bin/Debug/net8.0/AssetChecks.dll <viewer>/wow-data
```
No build/restore/launcher ran. Binary was present beforehand (mtime 2026-10-10 22:06:51, 36,864 bytes). Binary-source identity remains unverified. Both corrected runs return zero. Approximate elapsed time: direct path 0.451 s; corrected junction 0.243 s. These are warm, tiny CPU parsing checks, not render FPS or streaming throughput benchmarks.

Samples:
| Map | WDT available tiles | Sample ADT | Texture paths | Alpha |
|---|---:|---|---:|---|
| Azeroth | 687 | 32,48 | 7 | legacy |
| Kalimdor | 988 | 32,32 | 12 | legacy |
| Expansion01 | 800 | 32,32 | 1 | legacy |
| Northrend | 1131 | 32,32 | 2 | big |

Each selected tile has 256 chunks and finite heights; texture output lengths and synthetic nibble/RLE cases pass. This does not validate every tile, seams, orientation, liquids, M2/WMO, editing, packing, or arbitrary compression.

Existing graphical.log records Godot 4.7.1, OpenGL 3.3, RTX 5080, initial/moved/returned smoke success. runtime.log is an earlier two-stage variant. Both have a certificate-store error. Historical logs are not proof of fresh execution. A new viewer launch was intentionally avoided because Smoke creates project artifacts and Godot can update .godot/import state.

## Database query inventory
All statements use the read-only MCP:
1. SELECT TABLE_SCHEMA, COUNT(*) from information_schema.TABLES, grouped excluding system schemas.
2. SELECT TABLE_NAME, ENGINE, TABLE_ROWS for the 16 main world tables in database-query-0.txt. TABLE_ROWS is approximate InnoDB metadata.
3. SELECT table/column/type/nullability/default/key/order for 16 entity and behavior tables -> database-query-1.txt.
4. SELECT column/type/key/order for quests, relationships, loot and update tables -> database-query-2.txt.
5. SELECT table/constraint/type where constraint_type='FOREIGN KEY' -> database-query-3.txt. Only creature_sparring FK reported.
6. SELECT guid,id,map,position_x,position_y,position_z,orientation,MovementType FROM acore_world.creature WHERE map=0 ORDER BY guid LIMIT 5. First sample guid 1, template 2843, position (-14467.8,468.374,15.1064), orientation 0.139626. No character/account rows queried.
7. SELECT core_revision,db_version,cache_id FROM acore_world.version -> version recorded above.
8. SELECT COUNT(*) AS orphan_spawns FROM creature c LEFT JOIN creature_template t ON c.id=t.entry WHERE t.entry IS NULL -> 0.
9. SELECT INDEX_NAME,GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) FROM information_schema.STATISTICS for creature -> PK guid, idx_id id, idx_map map.
10. SELECT path,state FROM updates_include -> archive, custom, released, pending paths.
11. SELECT selected SmartAI columns WHERE entryorguid=2843 ORDER BY source_type,id LIMIT 5 -> empty; does not imply SmartAI absence generally.

One exploratory SELECT mistakenly used id1/id2/id3 (a different-core schema assumption) and received Unknown column id1. It was corrected to the observed id column. No failing query mutated data. The proposal explicitly rejects generic cross-core column assumptions.

## Evidence labels
**Confirmed:** inspected implementation/schema or observed read-only result.
**Proposed:** recommendation, contract or acceptance criterion; not implemented.
**Unverified:** requires experiments or inaccessible details.
**Derived:** calculation/inference from confirmed inputs, with its limits stated.

## Limitations and follow-up
No full renderer profiling, current-source rebuild, runtime database cache tests, client patch write/read/client-launch validation, ALE build, debugger attachment, or multi-core adapter validation was conducted. Source is sufficient for the architectural proposal; these are explicit Phase 0/later gates, not promised capabilities. External licensing conclusions describe inspected notices and integration decisions, not legal opinions. See 13-open-questions.md for owners and decision gates.

