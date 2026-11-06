# Needed tests

Tests deferred while retail loading and rendering were built. Add one once the feature it covers works,
and remove the entry when the test exists. The current feature status is in
[open-work.md](open-work.md); the perf and model-decode coverage lives in the test projects themselves.
`GeosetVariantsTests`, `AssetRefTests` and `RetailLiquidTests` already cover the cases they were listed
for; what is left is below.

## Known broken smokes

- [x] `--client-shot-overlays` used to get stuck after opening the quest editor: switching to the Story tab threw `KeyNotFoundException` because retail `Emotes.db2` has `EmoteSlashCommand`, not `Name`. Fixed; the smoke now reaches `CLIENT_SHOT_QUEST_EDITOR_OK` and completes.

## Retail terrain

- [ ] Viewer notices: dropped layers and FileDataID-0 layers are reported once per map, not once per
  tile, and not through the "N missing/unsupported textures" message (`reportedNotices` in
  `ViewerShell`). Godot-side, so a smoke or a manual check.
- [ ] Path-named placements: a map whose WDT/ADT names models by path (old style) still opens, and the
  "Path is not in the listfile" failure is reported once instead of per placement. Needs the community
  listfile absent to reproduce.

## Retail placements and models

- [ ] `WorldObjects.Read` on synthetic obj0 data: MDDF flag 0x40 / MODF flag 0x8 give `fdid:N` /
  `fdid:N.wmo` paths with no MMDX/MWMO tables; MODF scale is read only when flag 0x4 is set
  (`scale / 1024` at +62); a path-named entry without name tables still fails.
- [ ] `AssetRef.FromFileId(id, wmo: true)` round-trips through `TryParse` and `IsFileReference` accepts
  the `.wmo` form; `BinaryAsset.ModelPath` leaves references unchanged. (`AssetRefTests` covers the
  plain FileDataID form.)
- [ ] `M2Model.Read` on a synthetic MD21 container (MD20 v272 + SFID + TXID + `.skin`): textures resolve
  through TXID, the skin through SFID, bare Wrath MD20 files still read, and an MD21 with version < 272
  or no MD21 chunk is rejected. Also the animation paths: AFID names a sequence's `.anim` file and its
  AFM2 chunk feeds the tracks; a SKID `.skel` supplies bones (SKB1), sequences and global loops (SKS1)
  and its own AFID, whose `.anim` files are read from AFSB; a sequence missing from AFID falls back to
  the reference pose with a warning; a `.skel` without SKS1/SKB1 is rejected. The retail corpus covers
  these on real files only.
- [ ] `WmoModel.Read` on a synthetic retail root (no MOTX/MODN, with GFID/MODI): group files from GFID,
  a zero GFID entry skipped, MOMT texture as FileDataID, doodad paths from MODI, and a short GFID list
  rejected. Retail MLIQ groups add the "liquids are not loaded" warning (only when the liquid tables are
  unavailable) instead of failing.
- [ ] Retail corpus: every placement in a sample of tiles (doodads and WMOs) decodes (M2 + skin +
  textures; WMO + groups + textures); count the failures per cause.
- [ ] Global-WMO-only maps: all 228 open as a map (verified by hand: they parse). Open one in the viewer
  and check the building shows (visual, not yet inspected).
- [ ] `--client-shot` visual pass: needs a window, so it is not part of the default test run. The mage
  tower (fdid:107037.wmo, tile 32,48) and the Elwynn trees were inspected by eye and looked right.
  `--client-shot-overlays` (retail + TrinityCore, Goldshire) passes and saves
  `artifacts/client-shot-overlays.png`.

## Retail liquids and sky

- [ ] `ClientLighting.TryLoadRetail` no longer needs Godot types, but it still reads concrete `Db2Tables`,
  so a unit fixture needs an in-memory row seam. Until then the retail `--state-dump` pins the resolved
  profile and the whole `--state-retail` golden catches a parser change.
- [ ] Visual: the skybox model follows the camera and the time-of-day scrub changes the retail sky.
  `--skybox-smoke` covers loading and occlusion against world geometry; the follow and scrub check is
  still manual.
- [ ] Visual: ocean (type 2) and magma/slime layers on retail were not inspected; only a shallow lake
  (type 5, tile 32,49) was.
- [ ] Unlisted liquid textures (for example type 1163 `xtextures/ley/leydetail01_arden.blp`) fall back to
  the base liquid of their kind; add a `LiquidTextures` test with a fake `IClientFiles` for that naming
  fallback. (`RetailLiquidTests` already covers missing layers within a known type.)
- [ ] Format 3 layers with real data: no retail layer with LVF 3 was observed in the sampled tiles, so
  that layout (heights, UV, depth) is from documentation and the synthetic test only.

## Retail humanoid creatures

- [ ] Visual: the four hotfixed humanoid displays (136833, 136834, 137044, 137045) were checked by
  `TrinityReadDbTests.HotfixedHumanoidDisplaysKeepTheirSkinAndCustomization` only; find their spawns and
  inspect them in the viewer.
- [ ] Visual: humanoids were inspected at overlay-shot distance (two Stormwind guards in Goldshire). A
  close-up should confirm the face: eye texture placement, eyebrows (group 34), facial hair, and that the
  eye-glow geoset (group 17, hidden by default) does not draw over the eyes.

## Navigation

Deterministic parser coverage lives in `NavigationTests`; full-folder checks are opt-in with
`ACWORLD_AC_MMAP_DIR` / `ACWORLD_TC_MMAP_DIR`. The `--navigation-smoke` workflow covers upload, picking,
X-ray and the reset/disable lifecycle. Manual landmark comparisons across every generated map and
live-server obstacle parity are not automated.
