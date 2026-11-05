# Rendering and the retail shader port

The renderer turns client data into Godot meshes and materials; the shaders in `Shaders/` are ports of
the retail client's shipped shader bytecode (12.1.5) with the 3.3.5a water path kept where retail data
does not apply. Constant buffer and texture names are stripped in the shipped bytecode, so bindings are
inferred from use; the inputs that remain guessed are listed under
[inferred inputs](#inferred-inputs-needing-a-renderdoc-capture). How the meshes, batches, materials and
lighting are built is in [viewer-runtime.md](viewer-runtime.md#rendering); where textures and liquid
styles come from is in [client-data.md](client-data.md). This page owns the shader port itself: what is
ported, how faithful it is, which inputs are inferred, and where to look at each material in the viewer.

## Port status

State: **done**, **partial**, **todo**, **skip**.

| # | Group | Client shaders | State |
|---|---|---|---|
| 0 | Water | `mediumwater` | **done** |
| 0 | Sky dome | `dnsky` | **done** |
| 0 | Celestial | `dnplanet`, `dnclouds`, `glare`, star model, `sunshafts*` | **partial** |
| 1 | Fog | `volumefog*`, `liquidfog`, `ffxfogcombine`, `ffxfogseed`, `ffxpropagatefog`; LightData `FogHeightColor`, `EndFogColor`, `SunFogColor` | **partial** |
| 2 | Liquid effects | `magma`, `fel`, `leyline`, `mercury`, `azerite`, `proc*`, `mediumwaterbelow` | **partial** |
| 3 | Terrain | `terrain` (40 permutations), `terrainlow`, `terrainblendbake`, `terraintexture`, `terrainlightmap*` | **partial** |
| 4 | Model and WMO materials | `combiners_*`, `uber`, `skin`, `decal`, `illum`, `litsphere`, `proj_*`, `diffuse_*`, `color_*` | **partial** |
| 5 | Lighting and shadows | `lightbufferomni`, spot lights, `shadowmask`, `shadowmapsl`, `terrainlightmapomni/spot`, AO passes | **todo** |
| 6 | Particles and weather | `particle_*`, `ribbon`, `gpuribbon`, rain, snow, sand, lightning | **todo** |
| 7 | Post effects | `ffx*` glow, color grading, depth of field, spectral sight, death, underwater | **todo** |
| — | Skipped | AA (`fxaa`, `cmaa2`, `fsr`), `clustered*`, particle compute, UI, debug draws | **skip** |

`waterfall` is not a liquid shader; `anima` is not a liquid at all. Both are execution-queue items, not
materials. Skipped groups are retail's render pipeline rather than materials the editor can host.

## Port notes by group

### Water (`mediumwater`)

Ripple normal map, fresnel sky reflection and sun glint (`water_retail`). A type uses the retail model
when its LiquidType names a ripple map (`LiquidType` texture 2, `newoceanbump.blp` on retail stock
types; `RetailLiquids.Bump`, carried as `LiquidStyle.Bump`), which only retail data does. 3.3.5a data
keeps its own water (`water_wrath`), because its MPQs do not ship the map. Scroll rates, UV scales and
the specular exponent are estimates (the retail vertex shader is not shipped). Both models share the
depth gradient, sun values and fog; the shipped underwater face is in
[Material 1](#material-1-water). Client bytecode input: `mediumwater_0.txt`.

### Sky dome (`dnsky`)

Six ring colors, sunrise/sunset highlight and sun glow. Glow constants are estimates. Dither is
replaced by Godot's debanding (a project setting).

### Celestial (`dnplanet`, `dnclouds`, `glare`, stars, `sunshafts`)

Sun, moon and second moon discs are in (`dnplanet`: texture times sun color, horizon fade; textures read
from the client's `Textures/` folder for both clients). Clouds are in (`dnclouds`: the client's animated
noise cloud layer evaluated per pixel, density-thresholded, baked shading and sun bump light, rim
light; shapes differ from the client's because the noise table is random there). The discs add to the
sky (the client draws planets under the additive dome). Glare is in (`glare`: additive sun and moon
quads dimmed by cloud and depth-buffer occlusion; no fade-in/out rates) and the star model is in
(`stars.m2` as a skybox-style model faded by the day). Clouds draw as their own shell after the stars.
Sun shafts are in (`sunshafts`: sky-mask gather toward the sun in one pass, from the LightShaft rows;
strength and falloff estimated). Not done: storm blend.

### Fog (`volumefog*`, `liquidfog`, `ffx*` fog)

Done: retail exponential extinction over eye distance (`FogDensity`, estimated normalization over
start..end, 2500 yd view distance), end-fog colour blend, and fog and sky colours converted to linear.
Height fog is in (cubic in `FogHeightCoefficients` picks the layer or main fog; six-step integration in
the shaders; checked with a synthetic layer, because the sampled Outland lights have none). Sun
scattering is in (Henyey-Greenstein phase toward the sun disc, checked on a Kalimdor light that also
has a real height layer). Not done: `MainFog*` fields (zero in the data seen) and the froxel volume
itself. Constant mapping is inferred (no way to read the client's fog code here).

Froxel findings: the global fog compute (`volumefogglobalfog`) is what the analytic fog already
evaluates, minus per-froxel jitter. What is left of the froxel system: placed fog volumes
(`volumefogmaterialinjection`: oriented boxes with noise-edged density, stored in each map's
`<map>_fogs.wdt` `VFOG` chunk as 104-byte entries, 92 WDTs have them, toggled by `VolumeFogCondition`),
local light in-scatter (`volumefoglightscatter`) and temporal blend. The `VFOG` layout is the wiki's
(confirmed against the data: the quaternion is unit length, entry ids match the `VFEX` ids, positions
fall in the right regions) and placed volumes are in (`FogVolumes`, `client_fog_volume_layer`: ray-volume
extinction for the six nearest, box or ellipsoid, with a bottom-dense height profile, procedural
drifting noise and `VolumeFogCondition` presence, colored and lit; the meaning of the intensity, radius
and flag fields is inferred from the injection shader, and the thickness scale, box height, falloff,
noise cell sizes and speeds are estimates). Later, once lighting is in: volume light in-scatter
(`volumefoglightscatter`: needs a list of the scene's point lights, such as doodad and spell lights) and
sun shadows inside volumes and for the shafts (the shadowed injection permutation: needs the shadow-map
decision from the lighting group); temporal blend is not worth doing. `LightDataGlobalVolumeFog` (28
unnamed fields per `LightData` row) is also unread. The fog-liquid sheet (material 10) is a surface, not
this volume: see [Material 10](#material-10-fog-liquid-sheets).

### Liquid effects

Magma (material 2: lava, slime, green lava) is done: frame scroll from the type's first two floats.
Swamp (material 14), fel/venom (13), ley line and related liquids (12), mercury (5), fog liquids (10),
procedural magma (4), procedural water (3) and azerite (18) are partial; each has its own section below
with its fidelity limits. Unresolved assets retain their fallback. The underwater side is in for both
water models: `mediumwaterbelow` inside `liquid.gdshader` and `procwaterbelow` inside
`procwater.gdshader`, selected per fragment by the eye's side of the surface, with no reflection input.
Not ported: the ley-line underside (`mediumleylinebelow`/`procleylinebelow`); swamp/mercury/fel/fog
undersides are not shipped by the client, so they keep their single path.

### Terrain (`terrain`)

The layer and height blend already matched. Lighting is in (`client_surface_light`: ambient + diffuse
N.L in gamma space, layer-alpha-masked Blinn-Phong exponent 20, baked shadow 30 %, doubled vertex tint).
Not done: permutations 36 to 39 (per-layer color LUTs by distance, hemisphere ambient), the point-light
buffer, emissive and scrolling layers, `terrainlow` and the blend bake. Highlight strength is an
estimate.

Permutations 36 to 39 blend each layer toward a 32x32x32 color LUT by log2 eye distance (curve =
`TerrainColorGradingRamp` Min/Max, textures 0 and 1), masked by the layer height map's green channel.
Their data link is not in the ADT: no `MTCG` chunk exists in the `tex0` files of any retail map checked
(Khaz Algar, Dragon Isles, Outland, Kul Tiras, Zandalar), and the only DB2 that references a ramp is
`GroundEffectDoodad`, so the ramp likely follows each layer's ground-effect doodad. Which layers use it
is decided by CPU constants, so it is left unported.

### Model and WMO materials (`combiners_*`, `uber`, `skin`, …)

Lighting is in (`client_model_light`: ambient with wrap + diffuse N.L in gamma space, shared by M2 and
WMO batches; Godot lights and shadows no longer apply). Not done: hemisphere ambient colors, the light
buffer's point lights, `skin`, `uber`, `litsphere`, `proj_*`, and the combiner math itself (still the
wowdev combiner table).

### Lighting and shadows

Todo. The prerequisites are in (sun direction, tonemap, terrain, model and WMO lighting), but point
lights and shadows remain. They unlock volume fog light in-scatter and shadowed volumes and shafts (see
[Fog](#fog-volumefog-liquidfog-ffx-fog)). Shadows: dynamic cascades versus the client's approach is
still undecided.

### Particles, weather and post effects

Todo. Particles and weather need the retail particle pipeline; post effects need a post chain the
editor can host. Neither is a material the viewer can draw today.

## Material routing

`LiquidRendering` picks the shader from `LiquidStyle`, which `LiquidTextures.Style` builds from the
type's rows (see [client-data.md](client-data.md#liquid-styles)). The stock water/ice types keep
`liquid.gdshader`; only the specialized materials below leave it. A missing required texture keeps the
generic fallback instead of drawing a half material.

| Material | Shader | Selected when |
|---|---|---|
| 1 stock water/ice | `liquid.gdshader` | always the base path |
| 2 magma/slime | `liquid.gdshader` (magma path) | lava/slime kind; frame scroll from the first two floats |
| 3 procedural water | `procwater.gdshader` | `LiquidStyle.ProceduralWater` |
| 4 procedural magma | `procmagma.gdshader` | `LiquidStyle.Magma` and all three layers resolve |
| 5 mercury | `mercury.gdshader` | `LiquidStyle.Mercury` |
| 10 fog liquid | `liquidfog.gdshader` | `LiquidStyle.Fog` |
| 12 ley line | `leyline.gdshader` | both detail layers resolve |
| 13 fel and venom | `fel.gdshader` | both base layers resolve |
| 14 swamp | `swamp.gdshader` | material 14 (and the variants `LiquidRendering.Configure` routes there) |
| 18 azerite | `azerite.gdshader` | both body layers resolve |

## Material 1 water

`liquid.gdshader` carries two models. `water_wrath` is the 3.3.5a client's `psLiquidWater`:
`(ambient + diffuse * N.L) * gradient + frame texture + (specular + 0.25) * frame alpha`, with Blinn-Phong exponent 6 and the sun band as colour.
`water_retail` is `mediumwater`: scrolling ripple normal map, fresnel mix toward the fog colour and a
sun glint, with alpha 1.25 times the gradient alpha. A type uses the retail model when its LiquidType
names a ripple normal map; retail data does, 3.3.5a data does not (its MPQs do not even contain the
file), so the model follows the data rather than a client check.

Both models share the depth gradient (the client's 64-row procedural depth texture computed in the
shader from the active light's four water colours and alphas, `client_water_*`, indexed by the depth
byte kept in the vertex alpha through the type's depth table), the sun values and the fog, and run on
raw gamma-space values with only the result converted to linear. Frames loop over the type's period
(`LiquidType` int 1); `LiquidStyle` carries the gradient, depth table, texture scale, rotation and depth
scale. The retail model bends the surface with two near and two far scrolling samples of the map,
blended by camera distance; the animated frames are not used. The retail vertex shader is not shipped,
so ripple UV scales, scroll rates and the specular exponent are estimates, and the sky reflection uses
the fog colour. The two retail water shaders share the ripple normal through
`Shaders/liquid/liquid_surface.gdshaderinc` (`liquid_ripple`).

The underside: `water_retail` draws `mediumwaterbelow` when the eye is under the surface
(`INV_VIEW_MATRIX[3].y < world_position.y`, per fragment). It turns the ripple normal toward the viewer,
lets the lit gradient take the surface in proportion to the gradient alpha (a tenth less at grazing
angles) instead of the sky, drops the glint's 0.2 floor, and adds 40 % of the above-water sun response
behind a fourth-power Fresnel with the gradient's own alpha. The 3.3.5a model has no such variant, so
its underside keeps the above-surface path.

## Material 2 magma and slime

Lava, slime and green lava scroll their frames at the type's first two floats in texture widths per
second (`Liquid::GetTexScrollMtx`, `LiquidStyle.ScrollU`/`ScrollV`, `RetailLiquids.Scroll`). They are
unlit, opaque and vertex colour times frame. Terrain liquid materials are shared per texture and style
(`LiquidStyle.CacheKey`), so magma and faster WMO magma do not share one.

## Material 14 swamp

`LiquidRendering.Configure` selects `Shaders/liquid/swamp.gdshader`, and its screen/depth inputs are isolated
from ordinary water and magma. It combines the three-texture core of `procswamp_0/6`, the surface math
of `procswamp_20` and the shore overlay of `procswampabovefog_6` in one pass. Native MH2O depth drives
the shallow/middle/deep smoothsteps (equal adjacent thresholds stay finite). Two near and two coarse RG
samples from the type's BC5 normal sequence bend the depth textures; adjacent reconstructed normals
interpolate over time. The near/coarse factors 2.0, 2.34 and 0.15 and the 1/32 UV factor come from the
bytecode. Scene depth drives the cubic 25-per-yard intersection fade, foreground-rejected refraction
and the view-angle-weighted shore texture overlay. Squared Fresnel blends toward reflection;
Blinn-Phong sunlight and the shader's small normal-up lobe add highlights. The captured scene already
has lighting and fog, so the single-pass composite applies client fog to local material contributions
only. Missing normals leave a flat surface; a missing shore texture omits that overlay; missing depth
textures retain the generic base fallback. Textures and all surface settings participate in the
material cache key.

Fidelity limits: the DB2-to-buffer mapping is inferred, not recovered CPU code (floats 0..3 depth
thresholds, 4 normal strength, 5 specular exponent, 6 refraction, 8 reflection, 9 shore width, 10
animation rate, 11 ripple scale, 12 opacity, 13..15 layer scales, 17 highlight strength). Floats 7 and
16 are not bound: their CPU role remains unknown and captured scene lighting is preserved. The coarse
UV ratio and scroll directions/speed are estimates because the vertex shader's texture matrices are
populated by stripped CPU code. Retail's separate reflection render is replaced by the shared
32-sample screen trace described under [shared pieces](#shared-pieces). Checked in Godot on Azeroth
46,22 / 46,23 / 47,23 / 45,23; corpus coverage decodes the 30-frame ripple sequence and shoreline
texture for these tiles. Not ported: WMO water depth (WMO water draws at full depth) and the other
retail per-type floats (retail otherwise uses the stock river or ocean style by sound bank).

## Material 13 fel and venom

`LiquidRendering` selects `fel.gdshader` when both base layers resolve. It ports `fel_0`: deep/shallow
RGB blend by `saturate(1 - nativeDepth * coefficient)`, opaque, unlit, then client fog. LiquidType
floats 0..2 and 3..5 are interpreted as each layer's scale, angle in degrees and scroll speed; float 14
is the inferred depth coefficient. CPU scroll direction is estimated along rotated U. These bindings
are inferred, not recovered CPU code. The `procfel` animated normals, masked overlay, lighting and
shore/specular variants remain unported.

The core was inspected in a windowed Godot depth-gradient fixture; corpus checks resolve real
material-13 type 898. Venom types 1233/1234 on Azeroth 52,22 retain the generic fallback because their
deep/shallow texture names are absent from both the installed and freshly downloaded community
listfiles. This is not a completed venom port or a retail visual-parity check.

Venom input follow-up (deferred at the user's request): the listfile lookup gap does not establish
missing game assets. Current listfiles resolve shore/specular to 7568587/7568588, both readable from
CASC, but do not name deep/shallow/emissive. Direct name lookup also fails for the known readable files,
so it is not a valid absence check. DB2 frame counts for the three unresolved slots are zero.
Investigate generated/substituted inputs, stale names and unnamed FileDataIDs before treating this as an
asset blocker. Do not substitute guessed FileDataIDs.

## Material 12 ley-line

`LiquidRendering` selects `leyline.gdshader` when both detail layers resolve. The `leyline_0` formula
uses P = first.alpha * second.alpha, RGB = 2*P*first.rgb*second.rgb + (1-P)*baseColor and alpha =
(2*P+0.5)*vertexOpacity, followed by client fog. LiquidType floats 0..2 and 3..5 are inferred
scale/angle/scroll triples, and Color[0] is interpreted as packed RGB for the base colour. CPU UV
direction (rotated U), vertex opacity (native depth) and atlas period (1250 ms) are approximations. Both
texture sequences use the bounded shared atlas decoder with per-frame UV insets. Real Ley Line (868)
and Suramar Illuminated Water (890) assets resolve in corpus tests; the ley textures were inspected in
a windowed Godot plane fixture. No retail parity or complete material-12 port is claimed: the
`procleyline` normal, refraction, reflection, diffuse and shore passes remain unimplemented, as does the
ley-line underside.

## Material 18 azerite

`LiquidRendering` selects `azerite.gdshader` when `LiquidStyle.Azerite` resolves. The port follows
`azerite_0`, `azerite_1` and the body half of `procazerite`, drawn fully opaque (`ALPHA = 1`) and
unshaded with client fog. Both azerite types (940 and 951) share one material because they name the same
six textures and floats.

The body is a three-stop depth ramp. `azeritedeep` (slot 0) is the thick gold marble with cream ridges
and iridescent flecks and supplies the body; `azeritetranscolor` (slot 4) is the flat yellow transition
band; and `azeritecentercolor` (slot 1) is the blue speckle that belongs at the shallow edge. The vertex
colour alpha carries native depth for terrain liquids.

Flow comes from the slot-5 animation, not from the shipped normal slot: `azeriteanim_%d.blp` is a
30-frame sequence of real normal maps, decoded into a horizontal atlas and crossfaded between adjacent
frames the way `procazerite` does, then used to offset every layer UV and to shade the body with the
client sun. The slot-3 `azerite_basenormal.blp` is a flat placeholder in the shipped data (every DXT
block is byte-identical), so binding it would add nothing. `azeriteblend` (slot 2) adds the sparse
cyan/white/pink flecks. A short screen-space reflection and a self-lit term approximate `procazerite`'s
environment sample and glow.

The render mode keeps the surface in the same transparent, no-depth-write pass as the other liquid
shaders, so an overlapping liquid can blend over it instead of being occluded by it; "opaque" here means
the alpha is fixed at one, not that the surface writes depth.

Bindings: each layer is uploaded from its own decoded texture (`deep_tex`, `center_tex`,
`transcolor_tex`, `blend_tex`, `normal_tex`), so no layer depends on the type's frame-atlas fallback.
The slot order is inferred from the texture names. Floats 0 and 1 are the layers' scroll speeds, read
the way the other materials read their first two floats; the shipped rows store the same value in both,
so the split is not confirmable from the data. Floats 2..5 are not applied: reading them as material
13's per-layer scale/angle/scroll triples tiled the centre layer about twenty times across the pool and
produced a fine diagonal weave that is not an authored look. The client's per-layer texture matrices are
CPU constants that are not in the table, so all layers share one world-space UV (a little larger than
the ~33-yard chunk scale so the swirls span several yards).

The depth coefficient is not in the table and is a fixed estimate. Native depth is an 8-bit MH2O vertex
value, so reading it at 0..1 painted whole triangles blue wherever it dipped, which read as blotches
rather than an edge. It is scaled by 4.0 first, which keeps the blue centre layer on the true
zero-depth edge and leaves a soft yellow band before the gold body. The coefficient is tuned to the
retail surface, not recovered; it and the client's unknown depth scale cannot be separated. Missing
either body layer keeps the previous generic lava/slime fallback instead of drawing a half material.

Fidelity limits: the per-layer UV matrices (`CB1[13..20]`) and the per-light medium accumulation are not
ported, and the flow's speeds, warp strength and emission are estimates. This is not a retail parity
claim. Real types 940/951 resolve the body layers in corpus checks; see
[shader inspection locations](../reference/shader-inspection-locations.md) for the Zandalar pool used
for the windowed check.

## Material 5 mercury

Material 5 selects `mercury.gdshader`, independent of sound bank. It uses the native `mercurybump.blp`,
four scrolling RG samples, the `procmercury_0` distance blend (.2 + eyeDistance*.001), far amplitude
1.4, squared then fourth-power Fresnel, two desaturation lobes (luminance weights .3/.6/.1), a fixed .2
pedestal, and the reflection-dependent highlight. Composition stays in gamma space until one final
conversion, then client fog applies. The surface is opaque. Missing bump data produces a flat normal
while retaining the mercury material.

Fidelity limits: one shared bounded screen trace (32 samples, distance fade) with a fog-colour fallback
replaces the two dedicated retail reflection inputs and their projection/edge blend. Offscreen geometry
and transparent surfaces cannot reflect. Captured scene fog can be attenuated again by the mercury
surface fog. CPU ripple UV matrices, normal-strength/slope multipliers (1) and the highlight exponent
(16) are estimated; additional permutations and interaction buffers are unported. This is not a retail
parity claim. Real exterior/interior types 350/412 resolve the same native bump map in corpus tests; a
windowed Godot fixture with that texture and opaque coloured boxes verifies surface rendering and screen
reflections.

## Material 10 fog-liquid sheets

`LiquidRendering` selects `liquidfog.gdshader` for `LiquidStyle.Fog`. The basic `liquidfog_0`
composition samples the same density atlas twice, computes RGB = (first.rgb*second.rgb + second.rgb)*tint,
and alpha = (.3 + saturate(first.a*second.a + second.a)*opacityScale)*.5. The result uses client gamma
conversion and scene fog, with ordinary alpha blending and no depth writes. It does not use water
ripples, sunlight, highlights or native liquid-depth opacity. Atlas sampling clamps within frames. The
second UV has the vertex shader's .5 offset. CPU texture transforms are unavailable: unit UV scales and
orthogonal .01 cycles/second motion are estimates.

Color[0] RGB and Float[1] -> opacityScale are inferred DB2 bindings. Packed color alpha and the remaining
floats are not bound by this basic permutation. Types 750/751 share the native density pixels while
preserving separate tint/material keys in corpus tests. A windowed Godot fixture using type 750's
texture and tint over a checker was inspected. This is a partial port, not retail parity: `liquidfog_1`
auxiliary masks and later depth-aware exponential extinction, height parameters and secondary output
targets remain unported. The basic sheet must not be described as a volumetric fog simulation.

## Material 4 procedural magma

Material 4 is procedural magma, not procedural water (material 3). `LiquidRendering` selects
`procmagma.gdshader` when all three layers resolve. `procmagma_0` blends baseColor -> texture0 by
noise.R, then -> texture1 by noise.G*texture1.alpha, then -> texture2 by noise.B*texture2.alpha. The
surface is unlit and opaque, converted once from gamma space and fogged by client fog. The vertex
shader's three half-weighted sin/cos harmonic pairs and the *.15 + .5 noise interpolation factor are
retained. Texture atlases clamp within frames.

Inferred bindings: floats 0/1 scroll, 2..4 layer scales, 9 noise animation speed, Int[2] packed RGB base
color. CPU UV transforms and noise coordinates remain estimates. The unidentified client 3D noise
resource is replaced with a bounded analytic three-channel value field (six value-noise evaluations per
fragment); its hash, channel offsets and unit coordinate scale are estimates, so noise patterns differ.
No fluid simulation is added. Mask/secondary-target variants and retail reference comparison remain
open. Real types 302/303 resolve all layers in corpus checks; a windowed Godot fixture using those
layers was inspected.

## Material 3 procedural water

Material 3 is procedural water, not procedural magma (material 4). `LiquidRendering` selects
`procwater.gdshader` for `LiquidStyle.ProceduralWater`. The surface uses `procwaterabove_0`-derived near
and far scrolling normals from the native bump map (far amplitude 1.4, distance blend .2 +
eyeDistance*.001), the depth gradient colours, foreground-rejected screen refraction, squared Fresnel, a
reflection drawn 60 % toward the lit depth colour, and sun highlights. Composition stays in gamma space
until one final conversion, then client fog applies.

The eye's side of the surface picks the shipped pair: `procwaterbelow_0` draws the underside when the eye
is under the water, in the same fragment shader because one material covers a whole type. The below path
folds both ripple pairs at full strength instead of blending them by distance, reads the stored depth
unperturbed, offsets refraction by the ripple alone, keeps no reflection or foam, weights the water body
by the gradient alpha (a tenth less at grazing angles), and adds the sun's underwater response at 40 %
with a fourth-power Fresnel. It writes an opaque, already-composited pixel, so there is no shore edge.
The eye test is `INV_VIEW_MATRIX[3].y < world_position.y` per fragment, which also handles surfaces at
different heights; no CPU camera-medium state is needed for the surface (it is still needed for
underwater post effects, which are not ported).

Inferred bindings: the depth gradient comes from texture slot 1 (not the SoundBank), the depth table
from Int[0], and floats 2 (depth scale), 0 (wave scale), 4 (normal strength), 3 (refraction) and 11
(specular exponent, floored at 1). An intentionally empty frame slot returns a shared white 1x1
placeholder instead of decoding ordinary ocean fallback frames. Missing bump data produces a flat normal
while retaining the material.

Fidelity limits: CPU UV matrices, scroll rates and the remaining floats are estimated; one shared
bounded screen trace with a fog-colour fallback replaces the retail reflection inputs; the final soft
alpha edge is a cubic scene-depth approximation; and captured scene fog can be attenuated again by the
surface fog. Foam and auxiliary lighting/masks remain unported. This is not a retail parity claim. Real
types 100/321 resolve the native bump map in corpus tests; a windowed Godot fixture with that texture
and a checkerboard ground was inspected, and the underside was checked under the Den of Nalorakk water.
See [shader inspection locations](../reference/shader-inspection-locations.md) for a map/tile reference
for each implemented liquid shader.

## Shared pieces

- **`Shaders/liquid/liquid_reflection.gdshaderinc`**: the shared 32-sample screen trace used by swamp, mercury
  and procedural water: per-pixel jitter, fine near steps, the first depth crossing, binary refinement,
  a distance-scaled cross blur and retail's radial map window (1−x²)·(1−y²), faded to the region
  fog/sky colour between 12 and 40 yards so far scenery reads as water rather than a mirror. Offscreen
  objects cannot reflect, and the client's 0.75−z² ray-elevation factor is not applied because on a trace
  it suppresses steep reflections of tall nearby objects. Local interaction-ripple buffers and the
  client's spatial water simulation are absent.
- **`Shaders/liquid/liquid_surface.gdshaderinc`**: the ripple normal shared by the two retail water models.
- **`Shaders/shared/client_lighting.gdshaderinc`**: client fog, height fog, placed fog volumes and the
  surface/model lighting terms; the shader globals are declared in `project.godot`.
- **Fallbacks**: a material whose required layers do not resolve keeps the generic base style rather
  than drawing a half material. Material tint, depth and texture identity participate in
  `LiquidStyle.CacheKey`, so two types never share a material when their surface settings differ.

## Known approximations

- Surface lighting uses the client gamma-space band values and converts the finished result to linear;
  fog and sky colors are also converted to linear.
- Retail constant buffers are stripped; estimated constants are marked in the shader source and listed
  per material above.
- Editor shader globals: types and neutral defaults live in `project.godot`, so opening a shader in the
  editor does not depend on running the viewer first. Runtime initialization only allocates the
  fog-volume texture.
- The water ripple UVs and the sky glow constants cannot be read from the client data (the water vertex
  shader is not shipped).

## Inferred inputs needing a RenderDoc capture

These shaders use inputs inferred from the DB2 tables or estimated because the client's vertex shader and
CPU code are stripped. Each entry lists what is guessed, a frame-capture location (map / tile / type /
server XYZ) where the shader is visible, and the outputs or constants to read while there.

### Azerite (material 18) — `azerite.gdshader`
- **Guessed:** per-layer UV transforms (scale/rotation/scroll) are CPU texture matrices not in the
  table; all layers share one world-space UV at ~52 yards per tile plus float scroll.
  `depth_coefficient` (`CB1[12].z`) is tuned to 4.0: native depth is an 8-bit MH2O vertex value, and
  reading it at 0..1 painted whole triangles blue, so it is scaled to keep blue only on the true
  zero-depth edge. Normal fps, warp strength, emission and reflection strength are estimates. The
  floats 0/1 scroll split is unverifiable (shipped rows are identical). The per-light medium loop and
  the 3D fog LUT (`t16`) are unported.
- **Capture location:** Zandalar / 1642, tile 27,29, type 940, server `1425.0, 2516.67, 146.7`.
- **What to read:** `v2.xy` and `v2.zw` (per-layer UVs), `CB1[12].z` (depth coefficient), `CB1[9].y` and
  `CB1[9].w` (flags), `CB1[8].x` (light count), `CB5`/`CB6` (medium loop).

### Fel (material 13) — `fel.gdshader`
- **Guessed:** floats 0..2 / 3..5 as scale/angle/scroll per layer (inferred, not CPU code); float 14 as
  the depth coefficient; CPU scroll direction estimated along rotated U.
- **Capture location:** Khaz Algar / 2601, tile 36,25, type 1177, server `3498.61, -2447.22, 421.47`.
- **What to read:** `v2.xy`/`v2.zw` (per-layer UVs), `CB1[14]` (depth coefficient), `CB1[9]` flags.

### Ley line (material 12) — `leyline.gdshader`
- **Guessed:** floats 0..2 / 3..5 as scale/angle/scroll triples; Color[0] as packed RGB base colour; CPU
  UV direction (rotated U), vertex opacity (native depth) and atlas period (1250 ms) are
  approximations.
- **Capture location:** Suramar / 1662, tile 23,29, type 890, server `1402.78, 4306.94, 122.24`.
- **What to read:** `v2.xy`/`v2.zw` (per-layer UVs), `CB1` constants, `CB5`/`CB6` (medium loop), `t16`
  (3D fog LUT). Void/bioluminescent variants: Zandalar types 920/924/927 (no verified inspection
  location yet).

### Mercury (material 5) — `mercury.gdshader`
- **Guessed:** CPU ripple UV matrices, normal-strength/slope multipliers (1) and highlight exponent (16).
- **Capture location:** Deepholm / 646, tile 28,28, type 350, server `1873.61, 1669.44, 184.92`.
- **What to read:** `v2.xy` (ripple UVs), `CB1` constants for normal strength/slope/exponent.

### Fog liquid (material 10) — `liquidfog.gdshader`
- **Guessed:** Color[0] RGB and Float[1] -> opacityScale; CPU texture transforms (unit UV scales,
  orthogonal 0.01 cycles/second motion) are estimated.
- **Capture location:** Ardenweald / 2371, tile 29,46, type 1147, server `-7859.72, 1356.94, 5525.15`.
- **What to read:** `v2.xy`/`v2.zw` (UVs), `CB1` constants for opacity scale and motion.

### Procedural magma (material 4) — `procmagma.gdshader`
- **Guessed:** floats 0/1 scroll, 2..4 layer scales, 9 noise animation speed, Int[2] packed RGB base
  colour; CPU UV transforms and noise coordinates are estimated. The 3D noise resource is replaced with
  analytic value noise (hash, offsets and scale estimated).
- **Capture location:** FW Horde Garrison / 1154, tile 23,21, type 302, server `5373.61, 4477.78, 161.66`.
- **What to read:** `v2.xy`/`v2.zw` (UVs), `CB1` constants for scales/noise speed/base colour, `CB1[9]`
  (noise params).

### Procedural water (material 3) — `procwater.gdshader`
- **Guessed:** depth gradient from texture slot 1, depth table from Int[0], floats 2/0/4/3/11 (depth
  scale/wave scale/normal strength/refraction/specular exponent, floored at 1); CPU UV matrices and
  scroll rates are estimated.
- **Capture location:** Den of Nalorakk / 2825, tile 40,21, type 100, server `5386.11, -4697.22, 59.65`.
- **What to read:** `v2.xy`/`v2.zw` (UVs), `CB1` constants for depth/wave/normal/refraction/specular.

### Swamp (material 14) — `swamp.gdshader`
- **Guessed:** DB2 constant mapping inferred (floats 0..3 depth thresholds, 4 normal strength, 5 specular
  exponent, 6 refraction, 8 reflection, 9 shore width, 10 animation rate, 11 ripple scale, 12 opacity,
  13..15 layer scales, 17 highlight strength). Floats 7 and 16 are unknown. Coarse UV ratio and scroll
  directions/speed are estimated (vertex shader texture matrices).
- **Capture location:** Azeroth / 0, tile 46,23, type 1225, server `4786.11, -7609.72, 13.44`.
- **What to read:** `v2.xy`/`v2.zw` (UVs), `CB1` constants for all float bindings.

### Water (material 1, retail model) — `liquid.gdshader`
- **Guessed:** scroll rates, UV scales and specular exponent are estimated. The water vertex shader is
  not shipped, so the ripple UV matrices cannot be read from the client data.
- **Capture location:** any retail water type, for example Azeroth tile 46,23 next to the swamp.
- **What to read:** `v2.xy` (ripple UVs), `CB1` constants for scale/specular.

### Terrain — `terrain.gdshader`
- **Guessed:** highlight strength is estimated. Permutations 36–39 (per-layer color LUTs by distance,
  hemisphere ambient) are unported; the data link is not in the ADT (no `MTCG` chunk).
- **Capture location:** any retail map with varied terrain (for example Khaz Algar tile 33,29).
- **What to read:** `CB1` constants, per-layer LUT bindings, hemisphere ambient values.

### Model and WMO materials — `model.gdshader`
- **Guessed:** hemisphere ambient colors, the point-light buffer, and the skin/uber/litsphere/proj
  combiner math (still the wowdev combiner table). Lighting uses `client_model_light` (ambient wrap +
  diffuse N.L, gamma space).
- **Capture location:** any model-heavy area (Stormwind or Orgrimmar).
- **What to read:** `CB1`/`CB5` constants for hemisphere/point-light data and combiner inputs.

### Sky dome — `sky.gdshader`, `dnclouds`, `dnplanet`
- **Guessed:** glow constants are estimated; cloud shapes differ because the client's noise table is
  random.
- **Capture location:** any outdoor area at several times of day.
- **What to read:** `CB1` constants for glow, cloud density and sun position.

### Notes on capture

- Filter draw calls by texture name (for example `azeritedeep` or `leydetail01`) to find the surface.
- Read `v2.xy` / `v2.zw` from the vertex shader outputs for per-layer UV transforms.
- Read `CB1` constants for per-type values; `CB5`/`CB6` for per-light and fog data.
- Vertex shaders are not shipped (only pixel shaders disassemble), so UV matrices must be recovered from
  vertex outputs, not from vertex shader bytecode.
## Where to inspect each material

[shader-inspection-locations.md](../reference/shader-inspection-locations.md) lists one map/tile/type/
XYZ per implemented liquid shader, read from real ADT geometry and checked for material routing. It also
records the reflection, underwater and azerite viewing notes that make a capture comparable.
