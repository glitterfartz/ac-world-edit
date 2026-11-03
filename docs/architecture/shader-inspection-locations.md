# Shader inspection locations

Where to see each implemented retail liquid shader in the running viewer. These were
read from actual ADT liquid geometry and checked for intended material routing and
resolvable base assets: each entry is a surface triangle whose LiquidType routes to
the named material and whose authored textures/bump resolve. They are
**data- and routing-verified, not all visually inspected or compared against
retail** — treat them as a reliable place to look, not proof of visual parity.

To use one: open the named map first (the server XYZ is map-relative), then enter the
XYZ in the teleport box and look down. `XYZ` is 10 yards above the center of a
visible surface triangle.

| Shader | Map / ID | Directory | Tile | Type | Server XYZ |
|---|---|---|---|---|---|
| Magma, generic frame path | Eastern Kingdoms / 0 | Azeroth | 40,41 | 3 | -4872.22, -4559.72, 326.93 |
| Swamp | Eastern Kingdoms / 0 | Azeroth | 46,23 | 1225 | 4786.11, -7609.72, 13.44 |
| Fel core | Khaz Algar / 2601 | 2601 | 36,25 | 1177 | 3498.61, -2447.22, 421.47 |
| Ley-line core | Suramar / 1662 | SuramarEndScenario | 23,29 | 890 | 1402.78, 4306.94, 122.24 |
| Fog-liquid sheet | Ardenweald Covenant Finale Drust Scenario / 2371 | 2371 | 29,46 | 1147 | -7859.72, 1356.94, 5525.15 |
| Mercury | Deepholm / 646 | Deephome | 28,28 | 350 | 1873.61, 1669.44, 184.92 |
| Procedural water | Den of Nalorakk / 2825 | 2825 | 40,21 | 100 | 5386.11, -4697.22, 59.65 |
| Procedural magma | FW Horde Garrison Level 4 / 1154 | FWHordeGarrisonLevel3 | 23,21 | 302 | 5373.61, 4477.78, 161.66 |
| Azerite | Zandalar / 1642 | Zandalar | 27,29 | 940 | 1425.0, 2516.67, 146.7 |

The full machine-generated export is `artifacts/shader-inspection-locations.txt`
(git-ignored).

Notes:

- Swamp reflection check: on Azeroth tile 46,23, eye `4712.7, -7733.0, 20.5` at
  09:00 with yaw 270°, pitch -8° faces the temple across open water and shows the
  screen-space reflection at a grazing angle; yaw 90° faces the waterfall. A
  `--client-shot-eye=4712.7,-7733.0,20.5,270,-8 --client-shot-sky=9,0,0,-8` capture
  reproduces it. The 10-yard-overhead XYZ above looks down at the same surface but
  shows mostly fog, so it is not a good reflection check.
- An initial material-12 find on Khaz Algar 39,29 (type 1162) selected the fallback,
  so it is not listed. The Suramar ley-line entry above was verified instead.
- Types that still use the generic fallback (for example venom 1233/1234) are not
  listed; a location is only a useful check once the named material routes and its
  base assets resolve.
- Underwater-surface check: on Azeroth tile 36,35 the stock water (type 5, material 1,
  `mediumwaterbelow`) has a level-50 pool spanning server `-1800..-1817` X and
  `-2442..-2538` Y. Eye `-1804, -2490, 45` with pitch 89 looks straight up at the
  underside through the same material; it reads as a dark gradient-weighted sheet,
  where the above-surface path used to show a flat fog-coloured plane. Den of Nalorakk
  (2825, 40,21, type 100) works for `procwaterbelow` but sits under a dark cave
  ceiling, so its underside reads nearly black; use a lit procedural-water site before
  judging colour.
- The azerite entry is inside a large Zandalar pool (type 940) that spans server
  X 1258..1600 and Y 2154..2667 across tile 27,29. The surface under
  `1425, 2516.67` is that pool at height 136.2, so the listed XYZ sits about 10
  yards above it. Eye `1425, 2516.67, 168` with pitch 89 looks straight down at
  the pool and shows the blue centre layer with the cream `azeritedeep` swirls
  banded by the 8-bit depth interpolation. The stock water (type 5) in this tile
  is 300 yards west at X 1067..1146, so it never covers this point.
- The map, tile, type and XYZ are read from the retail client data. Visual parity
  against a retail reference is a separate, manual step and is not claimed by this
  table.
