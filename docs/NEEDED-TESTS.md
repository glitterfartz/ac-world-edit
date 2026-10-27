# Needed tests

Tests deferred while implementing retail loading and rendering. Add them after the feature they cover works. Remove an entry once its test exists.

## Milestone 3 (retail terrain)

- [ ] Viewer notices: dropped layers and FileDataID-0 layers are reported once per map, not once per tile, and not through the "N missing/unsupported textures" message (`reportedNotices` in `TerrainViewer`). Godot-side, so a smoke or a manual check.

## Milestone 4 (retail placements and models)

- [ ] `WorldObjects.Read` on synthetic obj0 data: MDDF flag 0x40 / MODF flag 0x8 give `fdid:N` / `fdid:N.wmo` paths with no MMDX/MWMO tables; MODF scale is read only when flag 0x4 is set (`scale / 1024` at +62); a path-named entry without name tables still fails.
- [ ] `AssetRef`: `FromFileId(id, wmo: true)` round-trips through `TryParse`; `IsFileReference` accepts the `.wmo` form; `BinaryAsset.ModelPath` leaves references unchanged.
- [ ] `M2Model.Read` on a synthetic MD21 container (MD20 v272 + SFID + TXID + `.skin`): textures resolve through TXID, the skin through SFID, bare Wrath MD20 files still read, and an MD21 with version < 272 or no MD21 chunk is rejected. Also the animation paths added since: AFID names a sequence's `.anim` file and its AFM2 chunk feeds the tracks; a SKID `.skel` supplies bones (SKB1), sequences and global loops (SKS1) and its own AFID, whose `.anim` files are read from AFSB; a sequence missing from AFID falls back to the reference pose with a warning; a `.skel` without SKS1/SKB1 is rejected. The retail corpus covers these on real files only.
- [ ] `M2Model.Read` geoset filter with `GeosetVariants` on a synthetic skin: only the chosen variant per group draws, skin geoset 0 always draws, and type 19 batches take the eye texture or are hidden without one.
- [ ] `WmoModel.Read` on a synthetic retail root (no MOTX/MODN, with GFID/MODI): group files from GFID, a zero GFID entry skipped, MOMT texture as FileDataID, doodad paths from MODI, and a short GFID list rejected. Retail MLIQ groups add the "liquids are not loaded" warning (only when the liquid tables are unavailable) instead of failing.

## Milestone 5 (retail liquids and sky)

- [ ] `ClientLighting.TryLoadRetail` needs Godot types, so it is only covered by the viewer: a smoke that prints the resolved profile (sky/ambient/fog colours, skybox FileDataID) for a camera position on Azeroth.
- [ ] Visual: skybox model loads and follows the camera on retail (the sky colours and fog were inspected in `--client-shot`; the skybox M2 itself was not confirmed), and the time-of-day scrub changes the retail sky.
- [ ] Visual: ocean (type 2) and magma/slime layers on retail were not inspected; only a shallow lake (type 5, tile 32,49) was.
- [ ] Unlisted liquid textures (e.g. type 1163 `xtextures/ley/leydetail01_arden.blp`) fall back to the base liquid of their kind; add a `LiquidTextures` test with a fake `IClientFiles` for the fallback.
- [ ] Format 3 layers with real data: no retail layer with LVF 3 was observed in the sampled tiles, so that layout (heights, UV, depth) is from documentation and the synthetic test only.
- [ ] Retail corpus: every placement in a sample of tiles (doodads and WMOs) decodes (M2 + skin + textures; WMO + groups + textures); count the failures per cause.
- [ ] Global-WMO-only maps: all 228 open as a map (verified by hand: they parse). Open one in the viewer and check the building shows (visual, not yet inspected).
- [ ] Known gap to cover: placements that name models by path (old-style WDT/ADT) fail with "Path is not in the listfile"; test that the map still opens and the failure is reported once.
- [ ] `--client-shot` smoke: needs a window, so it is not part of the test run. The mage tower (fdid:107037.wmo, tile 32,48) and the Elwynn trees were inspected by eye and looked right. `--client-shot-overlays` (retail + TrinityCore, Goldshire) passes and saves `artifacts/client-shot-overlays.png`.

## Retail humanoid creatures

- [ ] Visual: the four hotfixed humanoid displays (136833, 136834, 137044, 137045) were checked by `TrinityReadDbTests.HotfixedHumanoidDisplaysKeepTheirSkinAndCustomization` only; find their spawns and inspect them in the viewer.
- [ ] Visual: humanoids were inspected at overlay-shot distance (two Stormwind guards in Goldshire). A close-up should confirm the face: eye texture placement, eyebrows (group 34), facial hair, and that the eye-glow geoset (group 17, hidden by default) does not draw over the eyes.
