# TODO

Work that is known to be unfinished. Deferred tests live in `NEEDED-TESTS.md`; this file is for features, gaps and housekeeping.

## TrinityCore database

- **Target release:** TrinityCore `master` with TDB 1210.26091 (`world.version`: core rev 868213670b76, 2026-10-24). `docs/DB-DIFFERENCES.md` compares against the same TDB. The `hotfixes` rows are verified up to build 69497 (12.1.0); the retail client in use is 12.1.5.70575, so check for hotfix rows the newer client no longer matches when TrinityCore publishes a newer TDB.
- **Phase 1 (done): read-only overlays.** Spawns, graveyards, teleports (`game_tele`), area triggers (client AreaTrigger.db2 + server rows) and creature display hotfixes are read (`Data/TrinityCore/Tc*Source`). Graveyards are read-only and waypoints are off for TrinityCore.
- **Phase 2 (needed next): editor reads.** `TcDBSource` still throws "not available" for the table catalog (`Schema`), waypoints, the quest editor, the behavior (SmartAI) editor and area-trigger behavior, the creature property/interaction editor (`ReadInteractionAsync`, `MenuExistsAsync`, `TrainerExistsAsync`), and the searches (SmartAI parameters, broadcast text, items, quests). Each needs TrinityCore's restructured tables; see `docs/DB-DIFFERENCES.md`.
- **Phase 3: TrinityCore SQL export for edits.** Graveyard edits export AzerothCore statements, so TrinityCore graveyards stay read-only until `world_safe_locs` and the graveyard team conditions have their own export.
- Hotfixes: `creature_display_info_extra` and any customization option rows in `hotfixes` are not read (both empty in TDB 1210.26091); a hotfixed humanoid resolves its extra against the client. Only the first three `TextureVariationFileDataID` columns are read (the table has four).

## Tests

- **`Db2Tables` is not thread-safe, and xUnit runs test classes in parallel.** Retail test classes shared `RetailData.Tables` concurrently; adding a TrinityCore + retail test made it fail in 8 tests with "Operations that change non-concurrent collections must have exclusive access". Every class that reads `RetailData` now joins the `RetailClient` collection (`[Collection(RetailData.Collection)]`), so those classes run one at a time. A new retail test class must add the attribute too. Making `Db2Tables.Table` thread-safe (a lock around the cache) would remove the trap. The viewer is unaffected today: the creature catalog loads its tables on the open worker and builds its indexes behind thread-safe `Lazy` values.
- `CLAUDE.md` says a command-line `--filter` overrides `default.runsettings`; it does not. Corpus/KnownBug tests still need `--settings corpus.runsettings` (or `all.runsettings`) alongside the filter.
- `RetailCorpusTests.EveryMapWdtParsesAndOneTilePerTerrainMapDecodes` still carries comments about global-WMO placements being a gap "until the retail WMO milestone"; that milestone is done. Re-check the accepted-failure filter and tighten or remove it.

## Retail creatures

- Equipment is not composed: `NPCModelItemSlotDisplayInfo` → `ItemDisplayInfo` (weapons, shoulders, helmets, capes and the armour geosets they switch).
- Customization elements other than geosets and hair/eye materials are ignored: bone sets (`ChrCustomizationBoneSet`, e.g. body proportions), skinned models (`ChrCustomizationSkinnedModel`, separate hair/horn models), conditional models, display info, particle colours.
- Groups a display does not choose show variant 1, except ears (7) and head (32), which show variant 2. That rule comes from the data (variant 1 is an empty stub in those groups) and the wiki's DNE notes, not from a documented client rule.
- A humanoid whose extra has no bake material is drawn without customization, so every geoset shows.
- Parent skeletons (`.skel` `SKPD`) are not followed.
- Retail `AnimationNames` is empty (no AnimationData DB2 loaded), so the animation preview menu shows IDs only.
- M2: multitexture batches use their base texture; spline tracks are interpolated linearly.

## Retail terrain and rendering

- Height texturing (MHID/MTXP) and MAMP scale are not supported; only the first 4 of up to 8 layers are drawn.
- Uncompressed alpha size follows the WDT big-alpha flag. If a map's alpha spans ever disagree with it (not observed on 12.1.5), decide the size per layer from the span.
- Placements that name models by path resolve only through the community listfile.
