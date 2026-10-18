# Needed tests

Tests deferred while implementing retail loading and rendering. Add them after the feature they cover works. Remove an entry once its test exists.

## Milestone 3 (retail terrain)

- [ ] `RetailCorpusTests.EveryMapWdtParsesAndOneTilePerTerrainMapDecodes`: skip empty tile files (`.Where(t => t.Length > 0)`) before decoding; then check that the alpha-span check passes on every map.
- [ ] Synthetic test: if the alpha span on a real map disagrees with the WDT big-alpha flag, alpha size is decided per layer from the span.
- [ ] Viewer notices: dropped layers and FileDataID-0 layers are reported once per map, not once per tile, and not through the "N missing/unsupported textures" message (`reportedNotices` in `TerrainViewer`).
- [ ] Verification gaps: run the Wrath corpus (`corpus.runsettings`, `ACWORLD_DATA_DIR` set), `--terrain-smoke` and the StormLib differential after the `TerrainTile`/holes refactor.
- [ ] Visual check in Godot (no automated oracle): retail terrain blending and chunks with placements.
