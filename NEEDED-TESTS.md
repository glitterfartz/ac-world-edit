# Needed tests

Tests deferred while implementing retail loading and rendering. Add them after the feature they cover works. Remove an entry once its test exists.

## Milestone 3 (retail terrain)

- [ ] `RetailCorpusTests.EveryMapWdtParsesAndOneTilePerTerrainMapDecodes`: skip empty tile files (`.Where(t => t.Length > 0)`) before decoding; then check that the alpha-span check passes on every map.
- [ ] Synthetic test: if the alpha span on a real map disagrees with the WDT big-alpha flag, alpha size is decided per layer from the span.
- [ ] Viewer notices: dropped layers and FileDataID-0 layers are reported once per map, not once per tile, and not through the "N missing/unsupported textures" message (`reportedNotices` in `TerrainViewer`).
- [ ] Verification gaps: run the Wrath corpus (`corpus.runsettings`, `ACWORLD_DATA_DIR` set), `--terrain-smoke` and the StormLib differential after the `TerrainTile`/holes refactor.

## Milestone 4 (retail placements and models)

- [ ] `WorldObjects.Read` on synthetic obj0 data: MDDF flag 0x40 / MODF flag 0x8 give `fdid:N` / `fdid:N.wmo` paths with no MMDX/MWMO tables; MODF scale is read only when flag 0x4 is set (`scale / 1024` at +62); a path-named entry without name tables still fails.
- [ ] `AssetRef`: `FromFileId(id, wmo: true)` round-trips through `TryParse`; `IsFileReference` accepts the `.wmo` form; `BinaryAsset.ModelPath` leaves references unchanged.
- [ ] `M2Model.Read` on a synthetic MD21 container (MD20 v272 + SFID + TXID + `.skin`): textures resolve through TXID, the skin through SFID, bare Wrath MD20 files still read, and an MD21 with version < 272 or no MD21 chunk is rejected.
- [ ] `WmoModel.Read` on a synthetic retail root (no MOTX/MODN, with GFID/MODI): group files from GFID, a zero GFID entry skipped, MOMT texture as FileDataID, doodad paths from MODI, and a short GFID list rejected. Retail MLIQ groups add the "liquids are not loaded yet" warning instead of failing.
- [ ] Retail corpus: every placement in a sample of tiles (doodads and WMOs) decodes (M2 + skin + textures; WMO + groups + textures); count the failures per cause.
- [ ] Global-WMO-only maps: all 228 open as a map (verified by hand: they parse). Open one in the viewer and check the building shows (visual, not yet inspected).
- [ ] Known gap to cover: placements that name models by path (old-style WDT/ADT) fail with "Path is not in the listfile"; test that the map still opens and the failure is reported once.
- [ ] `--client-shot` smoke: not automated (needs a window); the mage tower (fdid:107037.wmo, tile 32,48) and the Elwynn trees were inspected by eye and looked right.
