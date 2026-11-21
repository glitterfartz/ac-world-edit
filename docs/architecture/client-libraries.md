# Client libraries

The client-side world stack is three class libraries under `src/`. `ACWorldEdit.csproj` references all
three, and a game client built on Godot 4.x .NET can reference the same projects.

```
src/
  ACWorldEdit.Core/          plain net8.0   WorldCoordinates, Point3, WorldPose, PoseText
  ACWorldEdit.ClientData/    plain net8.0   Assets/** (archives, formats, models, terrain, catalogs)
                                            + ClientServices (client open); references Foundation
  ACWorldEdit.ClientRender/  net8.0 + GodotSharp 4.7.2
                                            Rendering, Streaming, Lighting, WorldView facade
```

Dependency direction: `ClientRender -> ClientData -> Core`, with `ClientData -> Foundation` for the
editor's shared value types (Foundation is Godot- and MySQL-free); the app references all three;
`Scripts/Data` uses ClientData types. `ClientRender` declares **no Godot script classes** (nothing
derives from `Node`/`Resource`), so it builds as a normal class library against the `GodotSharp`
package rather than the Godot project SDK. The coordinate and client-data projects are Godot-free; the
xUnit project references ClientData (and Core transitively).

`Shaders/` stays at the repository root: Godot shader `#include` directives are literal
`res://Shaders/...` paths, so the folder is game content shared by the editor and any consumer, not
assembly content.

## Using it from a game client

```csharp
// Open the 3.3.5a MPQ set: maps, lighting, area names and the DBC catalogs.
var client = ClientServices.OpenWrath(@"C:\WoW\Data");

var camera = new Camera3D { Current = true, Far = 8000, Fov = 70 };
worldRoot.AddChild(camera);

// Builds the renderer, model library, object scene, sky, shadows and tile streamer, then loads the
// client's models on it.
var view = await WorldView.CreateAsync(worldRoot, camera, client);
// The map id selects the lighting table's regions; it is resolved from the client's map catalog when omitted.
view.OpenMap(new WorldMap("Azeroth", client.Files.Read("World/Maps/Azeroth/Azeroth.wdt")), mapId: 0);

// per frame
view.Update(delta);   // stream -> models -> sky -> sun shadows

// a creature from a server packet
var appearance = client.Creatures!.Resolve(displayId);
var handle = view.Models.Attach(creatureRoot, ModelKey.ForCreature(appearance, animation: 0),
                                transforms: null, ShadowCasterCategory.Creature);
handle.SetPlacement(0, transform);

// on shutdown: waits for streaming and model workers, so close the client files afterwards
await view.DisposeAsync();
```

The editor keeps its own wiring order (`ViewerShell.Process`) and uses the same components through
`WorldView`. Database-backed layers are editor-only: the streamer takes an optional
`ITileRegionSink` and a game client passes nothing.

## Consumer checklist

- Copy `Shaders/` into the consuming Godot project so it resolves as `res://Shaders/`. The library's
  `ShaderPaths` constants and the shader `#include` lines both assume that path.
- Shader globals: `ClientShaderGlobals.Ensure` registers every `client_*`/`creature_clock` global the
  shaders declare, so the host project does not have to. If you open the shaders in the Godot shader
  editor, still merge the `[shader_globals]` block from this repo's `project.godot` (the editor
  compiles a shader only when its globals exist in project settings);
  `python Tools/shader_globals_check.py` verifies the block is complete.
- Retail (CASC/DB2) clients additionally need the native CascLib and the WoWDBDefs `definitions/`
  folder beside the assembly, as `ACWorldEdit.csproj` copies them. 3.3.5a needs neither.
- Reference `GodotSharp` at the same version as the consuming Godot editor's `Godot.NET.Sdk`
  (`GodotSharpVersion` in `ACWorldEdit.ClientRender.csproj`).

## What the libraries do not provide

Networking and game state, collision and movement (only terrain-height sampling and WMO render-triangle
floor probes exist), animation blending/attach points/equipment composition, per-instance animation
phase (instances of one model batch animate in lockstep), particles and weather, WMO portals and
occlusion culling, and any UI or audio. Those belong to the client.
