# Retail shader port: progress

Working notes for the retail shader port (landed on `master`; the port began on a `render-upgrade` branch that is now merged). Not committed on purpose; keep it up to date as work lands.
Fidelity target: the retail client (12.1.5), read from its shipped shader bytecode.

## Where the retail shaders are

| What | Path |
|---|---|
| Retail install (CASC) | `X:\Games\World of Warcraft` (build 12.1.5.70688, enUS) |
| Extracted raw shaders (git-ignored) | `X:\AzerothCore\ACWorldEdit\ac-world-edit\artifacts\retail-shaders\shaders\{pixel,vertex,compute,geometry,raytracing}\{dx_5_0,dx_6_0}\*.bls` |
| Disassembly, one file per permutation | `X:\AzerothCore\ACWorldEdit\ac-world-edit\artifacts\retail-shaders\asm\{pixel,vertex,compute,geometry}\<name>_<permutation>.txt` (up to 40 permutations per shader) |
| Per-shader summary (permutations, size) | `X:\AzerothCore\ACWorldEdit\ac-world-edit\artifacts\retail-shaders\summary.json` |
| Extraction scripts | `X:\AzerothCore\ACWorldEdit\ac-world-edit\artifacts\retail-shaders\tools\casc_extract.py` and `dxall2.py` |
| File names to FileDataIDs | `%APPDATA%\Godot\app_userdata\ACWorldEdit\community-listfile.csv` (rows under `shaders/`) |

Facts about the data:

- Only `dx_5_0` (DXBC, readable) and `dx_6_0` (DXIL mirror, skipped) hold bytes. Every other profile folder reads 0 bytes.
- 424 unique `dx_5_0` shaders; 420 disassemble (not readable: pixel `trianglearea61`, compute `guidedfilter*`). 9,616 permutations were found.
- Container: `HSXG` header, block count and first-block offset at 0x18, then 16 KB zlib blocks. A permutation can span several blocks, so concatenate the blocks and scan for `DXBC`.
- Disassemble with `D3DDisassemble` from `d3dcompiler_47.dll`, cutting each blob to the size at DXBC offset 24.
- Constant buffer and texture names are stripped. Meanings are inferred from use (`CB1` per material, `CB5[173]` the light array, `CB6` screen and fog values, `t16` the 3D volume-fog texture).

## Progress

Status: **done**, **partial**, **todo**.

| # | Group | Shaders | Status | Notes |
|---|---|---|---|---|
| 0 | Water | `mediumwater` | **done** | Ripple normal map, fresnel sky reflection, sun glint (`water_retail`). Used when a water type names a ripple map, which only retail data does; 3.3.5a data keeps its own water (`water_wrath`) because its MPQs do not ship the map. Scroll rates, UV scales and specular exponent are estimates. |
| 0 | Sky dome | `dnsky` | **done** | Six ring colors, sunrise/sunset highlight, sun glow. Glow constants are estimates. Dither replaced by Godot's debanding (project setting). |
| 1 | Fog | `volumefog*` (compute and composite), `liquidfog`, `ffxfogcombine`, `ffxfogseed`, `ffxpropagatefog`; LightData `FogHeightColor`, `EndFogColor`, `SunFogColor` | **partial** | Done: retail exponential extinction over eye distance (`FogDensity`, estimated normalization over start..end, 2500 yd view distance), end-fog colour blend, fog and sky colours converted to linear. Height fog is in (cubic in `FogHeightCoefficients` picks the layer or main fog; six-step integration in the shaders; checked with a synthetic layer, because the sampled Outland lights have none). Sun scattering is in (Henyey-Greenstein phase toward the sun disc, checked on a Kalimdor light that also has a real height layer). Not done: `MainFog*` fields (zero in the data seen), the froxel volume itself. Constant mapping is inferred (no way to read the client's fog code here). Froxel findings: the global fog compute (`volumefogglobalfog`) is what the analytic fog already evaluates, minus per-froxel jitter. What is left of the froxel system: placed fog volumes (`volumefogmaterialinjection`: oriented boxes with noise-edged density, stored in each map's `<map>_fogs.wdt` `VFOG` chunk as 104-byte entries, 92 WDTs have them, toggled by `VolumeFogCondition`), local light in-scatter (`volumefoglightscatter`) and temporal blend. The `VFOG` layout is the wiki's (confirmed against the data: the quaternion is unit length, entry ids match the `VFEX` ids, positions fall in the right regions) and placed volumes are in (`FogVolumes`, `client_fog_volume_layer`: ray-volume extinction for the six nearest, box or ellipsoid, with a bottom-dense height profile, procedural drifting noise and `VolumeFogCondition` presence, colored and lit; the meaning of the intensity, radius and flag fields is inferred from the injection shader, and the thickness scale, box height, falloff, noise cell sizes and speeds are estimates). Later, once lighting is in: volume light in-scatter (`volumefoglightscatter`: needs a list of the scene's point lights, such as doodad and spell lights) and sun shadows inside volumes and for the shafts (the shadowed injection permutation: needs the shadow-map decision from row 6); temporal blend is not worth doing. `LightDataGlobalVolumeFog` (28 unnamed fields per `LightData` row) is also unread. |
| 2 | Celestial | `dnplanet` (sun and moon discs), `dnclouds`, `glare`, star model, `sunshafts*` | **partial** | Sun, moon and second moon discs are in (`dnplanet`: texture times sun color, horizon fade; textures read from the client's `Textures/` folder for both clients). Clouds are in (`dnclouds`: the client's animated noise cloud layer evaluated per pixel, density-thresholded, baked shading and sun bump light, rim light; shapes differ from the client's because the noise table is random there). The discs now add to the sky (the client draws planets under the additive dome). Glare is in (`glare`: additive sun and moon quads dimmed by cloud and depth-buffer occlusion; no fade-in/out rates) and the star model is in (`stars.m2` as a skybox-style model faded by the day). Clouds now draw as their own shell after the stars. Sun shafts are in (`sunshafts`: sky-mask gather toward the sun in one pass, from the LightShaft rows; strength and falloff estimated). Not done: storm blend. |
| 3 | Liquid effects | `magma`, `waterfall`, `fel`, `leyline`, `mercury`, `azerite`, `anima`, `proc*` variants, `mediumwaterbelow` | **partial** | Magma (material 2: lava, slime, green lava) is done: frame scroll from the type's first two floats. Swamp (material 14) surface port is in (`swamp.gdshader`): depth layers plus animated BC5 normals, scene-depth refraction and intersection fade, shore-texture overlay, squared Fresnel reflection and sun highlights, composed in one pass. Checked on all four reported Azeroth swamp tiles. Fidelity deviations: DB2 constant mapping inferred, floats 7/16 unknown, UV matrix constants estimated; jittered screen reflections from the shared `liquid_reflection.gdshaderinc` (32 samples, first depth crossing, binary refinement, distance-scaled blur, retail's radial map window and a 12-40 yard fade to fog) replace the separate reflection render; dynamic interaction buffers are absent. Fel/venom (13) and ley line/related liquids (12) have partial core paths; unresolved assets retain their fallback. Advanced procedural variants remain open. Mercury (5) has a dedicated surface path with screen-reflection substitutions; other variants remain open. Fog liquids (10) have a partial basic-sheet path; depth-aware variants remain open. Procedural water (3) now has a dedicated surface path (see the material 3 note below). Azerite (18) now ports `azerite_0`, `azerite_1` and the body half of `procazerite` in `azerite.gdshader`: a three-stop depth ramp (gold `azeritedeep` marble, yellow `azeritetranscolor` band, blue `azeritecentercolor` zero-depth edge), `azeriteblend` flecks, and the 30-frame `azeriteanim` normal sequence driving UV warp, sun shading, screen reflection and a self-lit term. The slot-3 `azerite_basenormal` is a flat placeholder in the shipped data. The per-layer UV matrices and the per-light medium loop remain unported. `anima` is not a liquid at all. The underwater side is in for both water models: `mediumwaterbelow` inside `liquid.gdshader` and `procwaterbelow` inside `procwater.gdshader`, selected per fragment by the eye's side of the surface, with no reflection input. `waterfall` is not a liquid shader (see the execution queue). Not ported: the ley-line underside (`mediumleylinebelow`/`procleylinebelow`), swamp/mercury/fel/fog undersides (the client ships none, so they keep their single path). |
| 4 | Terrain | `terrain` (40 permutations), `terrainlow`, `terrainblendbake`, `terraintexture`, `terrainlightmap*` | **partial** | The layer and height blend already matched. Lighting is in (`client_surface_light`: ambient + diffuse N.L in gamma space, layer-alpha-masked Blinn-Phong exponent 20, baked shadow 30 %, doubled vertex tint). Not done: permutations 36 to 39 (per-layer color LUTs by distance, hemisphere ambient), point-light buffer, emissive and scrolling layers, `terrainlow` and the blend bake. Highlight strength is an estimate. Permutations 36 to 39 blend each layer toward a 32x32x32 color LUT by log2 eye distance (curve = `TerrainColorGradingRamp` Min/Max, textures 0 and 1), masked by the layer height map's green channel. Their data link is not in the ADT: no `MTCG` chunk exists in the `tex0` files of any retail map checked (Khaz Algar, Dragon Isles, Outland, Kul Tiras, Zandalar), and the only DB2 that references a ramp is `GroundEffectDoodad`, so the ramp likely follows each layer's ground-effect doodad. Which layers use it is decided by CPU constants, so it is left unported. |
| 5 | Model and WMO materials | `combiners_*`, `uber`, `skin`, `decal`, `illum`, `litsphere`, `proj_*`, `diffuse_*` and `color_*` vertex shaders | **partial** | Lighting is in (`client_model_light`: ambient with wrap + diffuse N.L in gamma space, shared by M2 and WMO batches; Godot lights and shadows no longer apply). Not done: hemisphere ambient colors, the light buffer's point lights, `skin`, `uber`, `litsphere`, `proj_*`, the combiner math itself (still the wowdev combiner table). |
| 6 | Lighting and shadows (sun direction, tonemap, terrain, model and WMO lighting done; point lights and shadows remain, and they unlock volume fog light in-scatter and shadowed volumes and shafts, see row 1) | `lightbufferomni`, spot lights, `shadowmask`, `shadowmapsl`, `terrainlightmapomni/spot`, AO passes | todo | Shadows: dynamic cascades versus the client's approach is still undecided. |
| 7 | Particles and weather | `particle_*`, `ribbon`, `gpuribbon`, rain, snow, sand, lightning | todo | |
| 8 | Post effects | `ffx*` glow, color grading, depth of field, spectral sight, death, underwater | todo | Needs a post chain the editor can host. |
| - | Skipped | AA (`fxaa`, `cmaa2`, `fsr`), `clustered*`, particle compute, UI, debug draws | skip | Retail's render pipeline, not materials. |

## Known approximations

- Surface lighting uses the client gamma-space band values and converts the finished result to linear; fog and sky colors are also converted to linear.
- Retail constant buffers are stripped; estimated constants are marked in the shader source and in `docs/architecture/viewer-runtime.md`.
- Editor shader globals: types and neutral defaults now live in `project.godot`, so opening a shader in the editor does not depend on running the viewer first. Runtime initialization only allocates the fog-volume texture.
- The water ripple UVs and the sky glow constants cannot be read from the install (the water vertex shader is not shipped).

## Commits on `render-upgrade`

- `aaf7f04` load the client's fog and water light data
- `9daa75c` port the client's fog and water shaders (3.3.5a formulas)
- `bae8cea` add the retail ripple water shader
- `f9c0ab8` merge `master`
- `9298b3f` port the client's sky dome shader
- `3efe23f` retail exponential fog, linear fog and sky colors, `--client-shot-map` and extended `--client-shot-sky` flags
- `99081f4` name the 3.3.5a and retail water models
- `c590952` liquid reflection banding fix (fine steps, any depth crossing, binary refinement)
- `54c347b` shared, shortened liquid reflection (one include, 32 samples, distance fade)
- `aa9b522` client-style radial reflection window
- `1bf5795` retail liquid undersides (`mediumwaterbelow`, `procwaterbelow`) and the shared ripple include
- `5f8e8d0` retail azerite liquid (material 18) core
- `582af5d` azerite body layers at the native liquid UV (drops the inferred 20x tiling)
- `b9fc5e8` azerite layers bound from their own textures (`deep_tex`/`center_tex`), unused atlas/uv_scale dropped

## Per-material core notes

### Material 13 core (fel and venom)
`LiquidRendering` selects `fel.gdshader` when both base layers resolve. This ports
`fel_0`: deep/shallow RGB blend by `saturate(1 - nativeDepth * coefficient)`, opaque,
unlit, then client fog. LiquidType floats 0..2 and 3..5 are interpreted as each
layer's scale, angle in degrees and scroll speed; float 14 is the inferred depth
coefficient. CPU scroll direction is estimated along rotated U. These bindings
are inferred, not recovered CPU code. The `procfel` animated normals, masked
overlay, lighting and shore/specular variants remain unported.

The core was inspected in a windowed Godot depth-gradient fixture; corpus checks
resolve real material-13 type 898. Venom types 1233/1234 on Azeroth 52,22 retain
the generic fallback because their deep/shallow texture names are absent from
both the installed and freshly downloaded community listfiles.

### Material 12 ley-line core
`LiquidRendering` selects `leyline.gdshader` when both detail layers resolve.
The `leyline_0` formula uses P = first.alpha * second.alpha, RGB =
2*P*first.rgb*second.rgb + (1-P)*baseColor and alpha =
(2*P+0.5)*vertexOpacity, followed by client fog. LiquidType floats 0..2 and
3..5 are inferred scale/angle/scroll triples, and Color[0] is interpreted as
packed RGB for the base color. CPU UV direction (rotated U), vertex opacity
(native depth), and atlas period (1250ms) are approximations. Both texture
sequences use the bounded shared atlas decoder with per-frame UV insets.
Real Ley Line (868) and Suramar Illuminated Water (890) assets resolve in corpus
tests; the ley textures were inspected in a windowed Godot plane fixture.
No retail parity or complete material-12 port is claimed: the `procleyline`
normal, refraction, reflection, diffuse and shore passes remain unimplemented.

### Material 5 mercury surface
Material 5 selects `mercury.gdshader`, independent of sound bank. It uses the
native `mercurybump.blp`, four scrolling RG samples, the `procmercury_0` distance
blend (.2 + eyeDistance*.001), far amplitude 1.4, squared then fourth-power
Fresnel, two desaturation lobes (luminance weights .3/.6/.1), fixed .2 pedestal,
and the reflection-dependent highlight. Composition stays in gamma space until
one final conversion, then client fog applies. The surface is opaque. Missing
bump data produces a flat normal while retaining the mercury material.

### Material 10 fog-liquid sheets
`LiquidRendering` selects `liquidfog.gdshader` for `LiquidStyle.Fog`. The basic
`liquidfog_0` composition samples the same density atlas twice, computes
RGB = (first.rgb*second.rgb + second.rgb)*tint, and alpha =
(.3 + saturate(first.a*second.a + second.a)*opacityScale)*.5. The result uses
client gamma conversion and scene fog, with ordinary alpha blending and no depth
writes. It does not use water ripples, sunlight, highlights or native liquid-depth
opacity. Atlas sampling clamps within frames. The second UV has the vertex
shader's .5 offset. CPU texture transforms are unavailable: unit UV scales and
orthogonal .01 cycles/second motion are estimates.

Color[0] RGB and Float[1] -> opacityScale are inferred DB2 bindings. Packed color
alpha and the remaining floats are not bound by this basic permutation. Types
750/751 share the native density pixels while preserving separate tint/material
keys in corpus tests.

### Material 4 procedural magma
Material 4 is procedural magma, not procedural water (material 3).
`LiquidRendering` selects `procmagma.gdshader` when all three layers resolve.
`procmagma_0` blends baseColor -> texture0 by noise.R, then -> texture1 by
noise.G*texture1.alpha, then -> texture2 by noise.B*texture2.alpha. The surface
is unlit and opaque, converted once from gamma space and fogged by client fog.
The vertex shader's three half-weighted sin/cos harmonic pairs and *.15 + .5
noise interpolation factor are retained. Texture atlases clamp within frames.

Inferred bindings: floats 0/1 scroll, 2..4 layer scales, 9 noise animation speed,
Int[2] packed RGB base color. CPU UV transforms and noise coordinates remain
estimates. The unidentified client 3D noise resource is replaced with a bounded
analytic three-channel value field (six value-noise evaluations per fragment).

### Material 3 procedural water
Material 3 is procedural water, not procedural magma (material 4).
`LiquidRendering` selects `procwater.gdshader` for `LiquidStyle.ProceduralWater`.
The surface uses `procwaterabove_0`-derived near and far scrolling normals from the
native bump map (far amplitude 1.4, distance blend .2 + eyeDistance*.001), the depth
gradient colors, foreground-rejected screen refraction, squared Fresnel, a
reflection drawn 60% toward lit water and highlights. Composition stays in gamma
space until one final conversion, then client fog applies.

Inferred bindings: the depth gradient comes from texture slot 1 (not the SoundBank),
the depth table from Int[0], and floats 2 (depth scale), 0 (wave scale), 4 (normal
strength), 3 (refraction) and 11 (specular exponent, floored at 1). An intentionally
empty frame slot returns a shared white 1x1 placeholder instead of decoding ordinary
ocean fallback frames.

''',<rfl:parameter name="path">
X:\AzerothCore\ACWorldEdit\ac-world-edit\temp_lost.md
## Guessed/inferred inputs needing RenderDoc capture

The following shaders use inputs that were inferred from the DB2 tables or estimated
because the client's vertex shader / CPU code is stripped. Each entry lists what is
guessed, the disassembly file, and a frame-capture location (map / tile / type /
server XYZ) where the shader is visible so a RenderDoc capture can recover the
real values.

### Azerite (material 18) - `azerite.gdshader`
- **Ported:** `azerite_0` three-stop depth ramp (deep gold marble / transcolor yellow /
  centre blue), `azerite_1`'s `azeriteblend` mask flecks, and `procazerite`'s animated
  normal crossfade + UV warp + sun shading + screen reflection + self-lit term. The
  flow uses the slot-5 `azeriteanim_%d.blp` sequence (30 real normal maps); the slot-3
  `azerite_basenormal.blp` is a flat placeholder in the shipped data, so it is unused.
- **Guessed:** per-layer UV transforms (scale/rotation/scroll) are CPU texture matrices
  not in the table - all layers share one world-space UV at ~52 yards per tile plus float
  scroll. `depth_coefficient` (`CB1[12].z`) tuned to 4.0: native depth is an 8-bit MH2O
  vertex value, and reading it at 0..1 painted whole triangles blue, so it is scaled to
  keep blue only on the true zero-depth edge. The client's depth scale is unknown, so the
  two cannot be separated. Normal fps, warp strength, emission and reflection strength are
  estimates. Floats 0/1 scroll split unverifiable (shipped rows identical).
  Per-light medium loop and 3D fog LUT (`t16`) unported.
- **Disassembly:** `azerite_0.txt`, `azerite_1.txt`, `procazerite_0.txt` (pixel)
- **Capture location:** Zandalar / 1642, tile 27,29, type 940, server `1425.0, 2516.67, 146.7`
- **What to read:** `v2.xy` and `v2.zw` (per-layer UVs), `CB1[12].z` (depth coeff),
  `CB1[9].y`/`CB1[9].w` (flags), `CB1[8].x` (light count), `CB5`/`CB6` (medium loop).

### Fel (material 13) — `fel.gdshader`
- **Guessed:** floats 0..2 / 3..5 as scale/angle/scroll per layer (inferred, not CPU code).
  Float 14 as depth coefficient (inferred). CPU scroll direction estimated along rotated U.
- **Disassembly:** `fel_0.txt` (pixel)
- **Capture location:** Khaz Algar / 2601, tile 36,25, type 1177, server `3498.61, -2447.22, 421.47`
- **What to read:** `v2.xy`/`v2.zw` (per-layer UVs), `CB1[14]` (depth coeff), `CB1[9]` flags.

### Ley-line (material 12) — `leyline.gdshader`
- **Guessed:** floats 0..2 / 3..5 as scale/angle/scroll triples. Color[0] as packed RGB
  base color. CPU UV direction (rotated U), vertex opacity (native depth), atlas period
  (1250ms) are approximations.
- **Disassembly:** `leyline_0.txt`, `mediumleyline_0.txt`, `procleylineabove_0.txt` (pixel)
- **Capture location:** Suramar / 1662, tile 23,29, type 890, server `1402.78, 4306.94, 122.24`
- **What to read:** `v2.xy`/`v2.zw` (per-layer UVs), `CB1` constants, `CB5`/`CB6` (medium loop),
  `t16` (3D fog LUT). Void/bioluminescent variants: Zandalar types 920/924/927 (no verified
  inspection location yet).

### Mercury (material 5) — `mercury.gdshader`
- **Guessed:** CPU ripple UV matrices, normal-strength/slope multipliers (1), highlight
  exponent (16) estimated.
- **Disassembly:** `procmercury_0.txt` (pixel)
- **Capture location:** Deepholm / 646, tile 28,28, type 350, server `1873.61, 1669.44, 184.92`
- **What to read:** `v2.xy` (ripple UVs), `CB1` constants for normal strength/slope/exponent.

### Fog-liquid (material 10) — `liquidfog.gdshader`
- **Guessed:** Color[0] RGB and Float[1] -> opacityScale inferred. CPU texture transforms:
  unit UV scales, orthogonal 0.01 cycles/second motion estimated.
- **Disassembly:** `liquidfog_0.txt` (pixel)
- **Capture location:** Ardenweald / 2371, tile 29,46, type 1147, server `-7859.72, 1356.94, 5525.15`
- **What to read:** `v2.xy`/`v2.zw` (UVs), `CB1` constants for opacity scale and motion.

### Procedural magma (material 4) — `procmagma.gdshader`
- **Guessed:** floats 0/1 scroll, 2..4 layer scales, 9 noise animation speed, Int[2] packed
  RGB base color (inferred). CPU UV transforms and noise coordinates estimated. 3D noise
  resource replaced with analytic (hash, offsets, scale estimated).
- **Disassembly:** `procmagma_0.txt` (pixel)
- **Capture location:** FW Horde Garrison / 1154, tile 23,21, type 302, server `5373.61, 4477.78, 161.66`
- **What to read:** `v2.xy`/`v2.zw` (UVs), `CB1` constants for scales/noise speed/base color,
  `CB1[9]` (noise params).

### Procedural water (material 3) — `procwater.gdshader`
- **Guessed:** depth gradient from texture slot 1, depth table from Int[0], floats 2/0/4/3/11
  (depth scale/wave scale/normal strength/refraction/specular exponent, floored at 1).
  CPU UV matrices, scroll rates estimated.
- **Disassembly:** `procwaterabove_0.txt`, `procwaterbelow_0.txt` (pixel)
- **Capture location:** Den of Nalorakk / 2825, tile 40,21, type 100, server `5386.11, -4697.22, 59.65`
- **What to read:** `v2.xy`/`v2.zw` (UVs), `CB1` constants for depth/wave/normal/refraction/spec.

### Swamp (material 14) — `swamp.gdshader`
- **Guessed:** DB2 constant mapping inferred (floats 0..3 depth thresholds, 4 normal strength,
  5 specular exponent, 6 refraction, 8 reflection, 9 shore width, 10 animation rate, 11 ripple
  scale, 12 opacity, 13..15 layer scales, 17 highlight strength). Floats 7 and 16 unknown.
  Coarse UV ratio and scroll directions/speed estimated (vertex shader texture matrices).
- **Disassembly:** `swamp_0.txt` (pixel)
- **Capture location:** Azeroth / 0, tile 46,23, type 1225, server `4786.11, -7609.72, 13.44`
- **What to read:** `v2.xy`/`v2.zw` (UVs), `CB1` constants for all float bindings.

### Water (material 1, retail model) — `liquid.gdshader` (retail path)
- **Guessed:** scroll rates, UV scales, specular exponent estimated. Water vertex shader not
  shipped so ripple UV matrices cannot be read from install.
- **Disassembly:** `mediumwater_0.txt` (pixel)
- **Capture location:** (no dedicated water entry in shader-inspection-locations.md; use any
  retail water type, e.g. Azeroth tile 46,23 has water surfaces alongside the swamp)
- **What to read:** `v2.xy` (ripple UVs), `CB1` constants for scale/specular.

### Terrain — `terrain.gdshader`
- **Guessed:** highlight strength estimated. Permutations 36-39 (per-layer color LUTs by distance,
  hemisphere ambient) unported — data link not in ADT (no MTCG chunk).
- **Disassembly:** `terrain_*.txt` (pixel, 40 permutations)
- **Capture location:** any retail map with varied terrain (e.g. Khaz Algar tile 33,29)
- **What to read:** `CB1` constants, per-layer LUT bindings, hemisphere ambient values.

### Model/WMO materials — `model.gdshader`
- **Guessed:** hemisphere ambient colors, point-light buffer, skin/uber/litsphere/proj combiner
  math (still wowdev combiner table). Lighting uses `client_model_light` (ambient wrap +
  diffuse N.L, gamma space).
- **Disassembly:** `combiners_*`, `uber`, `skin`, etc. (pixel)
- **Capture location:** any model-heavy area (e.g. Stormwind or Orgrimmar)
- **What to read:** `CB1`/`CB5` constants for hemisphere/point-light data, combiner inputs.

### Sky dome — `sky.gdshader` / `dnclouds` / `dnplanet`
- **Guessed:** glow constants estimated. Cloud shapes differ (noise table is random in client).
- **Disassembly:** `dnsky_*.txt`, `dnclouds_*.txt`, `dnplanet_*.txt` (pixel)
- **Capture location:** any outdoor area at various times of day
- **What to read:** `CB1` constants for glow, cloud density, sun position.

### Notes on capture

- Use RenderDoc (inject into running process or global hook) per the private-server
  launcher workflow. Press F12 in-game to capture.
- Filter draw calls by texture name (e.g. "azeritedeep", "leydetail01") to find the
  right surface.
- Read `v2.xy` / `v2.zw` from the vertex shader outputs for per-layer UV transforms.
- Read `CB1` constants for per-type values; `CB5`/`CB6` for per-light and fog data.
- The vertex shaders are not shipped (only pixel shaders disassemble), so UV matrices
  must be recovered from vertex outputs, not from vertex shader bytecode.
