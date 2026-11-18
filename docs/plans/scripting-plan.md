# Visual Scripting (Blueprint-style) for ACWorldEdit

## Context

ACWorldEdit can view and edit world-database content, but scripting is still authored as
SmartAI rows or C++ `ScriptMgr` code. The goal is a UE5-Blueprint-style node-graph editor inside
ACWorldEdit that authors **spell, quest, dungeon and world/creature scripts**, compiles them to
**Lua for the AzerothCore ALE/mod-ale runtime**, and can later target **TrinityCore Eluna, C++ or
other languages** without changing the graphs.

Why: ALE exposes most of the hooks a C++ script uses (player/creature/gameobject/item/spell/map/
instance/server events) and an object API rich enough to prototype or replace C++ scripts. Authoring
those in Lua by hand is error-prone and inaccessible to non-programmers; a graph editor makes the
hooks, types and entity references visible and validates them before export.

Decisions taken with the user:

| Decision | Choice |
|---|---|
| Node coverage | **Curated core**: every event registration + ~150 high-value capabilities across the four domains, expandable via a catalog tool |
| Artifacts | **Workspace + explicit export**: `.acwscript` JSON documents in `user://scripts` (configurable); export writes `.lua` into the ALE `lua_scripts` folder with a manifest |
| Canvas | **Godot `GraphEdit`/`GraphNode`** with typed slots, isolated behind a controller |
| First vertical slice | **World/creature event** (Creature On Death → message / spell / item / spawn) |

### Approaches considered

- **Visual graph vs. a sentence/rule editor vs. a text Lua editor.** A SmartAI-style rule editor is
  easier but cannot express the object/event API or reusable functions; a text editor is not visual.
  The graph is the requested shape and the only one that makes typed pins and entity references
  visible.
- **Godot `GraphEdit` vs. a custom canvas.** `GraphEdit` gives pan/zoom/selection/wiring for free and
  supports typed slots and hover validation in C#; a custom canvas buys exact UE styling at weeks of
  extra cost. `GraphEdit` wins for v1, isolated behind a controller.
- **Workspace + explicit export vs. authoring directly in `lua_scripts` vs. DB-backed documents.**
  A workspace keeps graphs reviewable and the server folder clean; direct authoring mixes generated
  and hand-written Lua; DB storage adds schema work for artifacts that the runtime reads as files.
- **Per-host catalogs/bindings vs. one Lua-target compiler.** ALE and upstream Eluna are explicitly
  incompatible, so a single Lua emitter with hard-coded functions would need a fork per host. The
  semantic graph → host profile → IR → emitter split costs a little upfront and makes TC/C++ add-ons
  data/emitter work rather than a rewrite.

### Goals

- Visual graph authoring with exec wires, typed data pins, variables, user functions, comments and
  typed entity references (creature/item/spell/quest/map IDs with pickers).
- Compile to readable, deterministic, Lua 5.1/LuaJIT-compatible Lua that ALE loads from `lua_scripts`.
- One semantic graph works across hosts: ALE first, TrinityCore Eluna/C++ later by adding a host pack
  and/or emitter.
- Validate before export: unknown capabilities, wrong pin types, missing entries, unavailable host
  features, unreachable nodes, invalid returns.
- Templates for the four domains so a user can start from a working graph.

### Non-goals (v1)

- No Lua interpreter/execution inside the editor, no breakpoints or live debugging.
- No raw Lua text node in v1: host extension operations (reviewed catalog entries) are the escape
  hatch, so host switching and validation stay honest.
- No C++ emitter or automatic C++→graph conversion (the IR/emitter boundary is designed for it).
- No DB change-log integration for scripts; scripts are file artifacts, not `RowEdit`s.
- No collaboration, version-control UI, or cloud workspace.
- No claim of full C++ `ScriptMgr` parity: only hooks ALE exposes can be represented; the gaps are
  documented in a parity matrix.
- No exhaustive ALE API coverage in v1 (curated core; expansion is a catalog task, not a compiler task).

### Success criteria

1. A Creature On Death graph compiles, exports to a real ALE `lua_scripts` folder, survives
   `.reload ale`, and runs in game.
2. Spell, quest, dungeon and world templates compile without errors and cover their core flows.
3. The same abstract graph compiles under a second host profile in tests, proving the translation
   layer is not Lua/ALE-specific.
4. `dotnet test Tests/ACWorldEdit.Tests` and `Tests/ACWorldEdit.GdTests` are green; docs updated.

---

## Research findings

### ALE / mod-ale runtime (constrains the generated code)

- **Loading**: scripts are discovered recursively under `lua_scripts` (`ALE.ScriptPath`); hidden
  files/directories are skipped and extensions are case-sensitive. Within a scan, `.ext` files load
  first and each group is sorted lexicographically, but **base filenames must still be unique across
  all subdirectories** (the second duplicate is skipped). All scripts share one Lua global table.
  `.reload ale` (console/SEC_ADMINISTRATOR), `ReloadALE()` and the optional AutoReload watcher rebuild
  the entire Lua state. Generated files must be self-contained and must not depend on load order.
- **Registration** (global functions, all return a cancel closure): `RegisterServerEvent(event, fn)`,
  `RegisterPlayerEvent(event, fn)`, `RegisterGuildEvent`, `RegisterGroupEvent`, `RegisterBGEvent`,
  `RegisterTicketEvent`, `RegisterAllCreatureEvent(event, fn)`; entry-scoped
  `RegisterCreatureEvent(entry, event, fn)`, `RegisterGameObjectEvent`, `RegisterItemEvent`,
  `RegisterSpellEvent`, the three gossip variants; two-keyed
  `RegisterUniqueCreatureEvent(guid, instanceId, event, fn)`; `RegisterMapEvent(mapId, event, fn)`,
  `RegisterInstanceEvent(instanceId, event, fn)`, `RegisterPacketEvent(opcode, event, fn)`, and
  `RegisterPlayerGossipEvent(menuId, event, fn)`. Optional trailing `shots` (0 = permanent) in all of
  them. Handlers receive `(event, ...eventArgs)`.
- **No dedicated registrars for quests, commands, vehicles or "world"**: quest hooks are player
  events (accept/complete/abandon/reward-item), creature/GO/item `ON_QUEST_ACCEPT`/`ON_QUEST_REWARD`
  and gossip; commands are `PLAYER_EVENT_ON_COMMAND` (42, return `false` to consume; `player` is nil
  from the console); vehicle events exist in C++ but have no Lua entry point and must not be offered.
  `RegisterAllCreatureEvent` uses the separate `AllCreatureEvents` enum (add/remove/select-level/
  damage modifiers), not the creature entry events.
- **Map lifecycle vs instance events**: `MAP_EVENT_ON_CREATE/DESTROY/PLAYER_ENTER/PLAYER_LEAVE/UPDATE`
  are **ServerEvents** (17–23). `RegisterMapEvent`/`RegisterInstanceEvent` use `InstanceEvents` 1–7
  and pass the `instance_data` table; they must be registered before the instance is created or ALE
  will not attach its instance AI.
- **Handler signatures come from `src/LuaEngine/hooks/*.cpp`, not the docs.** Known doc mismatches:
  player gossip select pushes the player twice and no menu id; gossip `code` is an optional trailing
  arg; `PLAYER_EVENT_ON_LOOT_ITEM` has an extra GUID; `WORLD_EVENT_ON_CONFIG_LOAD` has an extra
  `isBefore`; `PLAYER_EVENT_ON_PET_KILL` is `(player, killedCreature)`; some shipped doc examples use
  the wrong event IDs. The catalog generator must read the hook push order.
- **Userdata lifetime is the sharpest codegen constraint.** Core object userdata
  (`Player`/`Creature`/`GameObject`/`Item`) is invalidated when the outermost event handler returns.
  Module variables and timer/async continuations must **never** store or capture those references;
  store `GetGUID()`/`GetGUIDLow()` and re-resolve (`GetPlayerByGUID`, `Map:GetWorldObject`) instead.
  `ALEQuery`, `WorldPacket` and 64-bit values are GC-managed and safe. This restriction becomes a
  validator rule and shapes the timer/async nodes.
- **Registration is validated at load**: creature/GO/item/spell entries must exist or the Lua chunk
  aborts (earlier registrations in the same chunk stay active). Entry-based creature events also
  require their binding to exist when the creature's AI is created, i.e. register at file load.
  The editor should validate IDs against the live DB before export whenever a connection exists.
- **Timers**: `obj:RegisterEvent(fn, delay[, repeats])` and `CreateLuaEvent(fn, delay[, repeats])`;
  callbacks receive `(eventId, delay, repeats, objectOrNil)`; return a numeric ID; `repeats = 0` is
  infinite. Creature/GameObject timers tick only while the object is in world; timers die with the
  object or on reload. `RemoveEventById` is a **global function** (there is no `Global` table) and
  has an argument-index bug, so cancellable timers should be object-scoped; generated cancellation
  uses the object form.
- **Instance data**: `Map:GetInstanceData()` is a Lua table only when ALE owns the instance script
  (nil for C++-scripted instances); instance handlers receive `(event, instance_data, map, ...)`;
  in-memory changes are immediate, `Map:SaveInstanceData()` persists them to `instance.data`. ALE
  alters that column to `TEXT` on startup. Instance event nodes expose the handler's `instance_data`
  as a typed `Table` pin — graphs should use that pin, not a re-fetched table, inside instance
  handlers. On reload the table is restored from the **last saved** marshalled data, so unsaved
  in-memory changes are lost; the dungeon template saves after meaningful checkpoints.
- **Runtime modes**: the verified clone is single-state — `IsCompatibilityMode()` always returns true
  and `GetStateMapId()` always returns `-1`. The multistate PR is not merged here. Generated code
  defaults to compatibility mode; keep the document `runtimeMode` field but treat multistate as
  experimental and warn when a server build does not support the guards.
- **Lua versions**: Lua 5.2.4 default, LuaJIT recommended, `lua51` also buildable; CI builds 5.1/5.2/
  5.3/5.4/LuaJIT. Generated output targets **Lua 5.1/LuaJIT syntax**: no `goto`, `//`, bitwise
  operators or `table.unpack`; bitwise operations use ALE's `bit_*` globals.
- **Object model**: `Object → WorldObject → Unit → Creature/Player`, plus GameObject, Item, Spell,
  SpellInfo, Aura, Quest, Map, Group, Guild, WorldPacket, ALEQuery, ChatHandler. **There is no
  `Instance` type** — instance scripting is `Map` plus the `instance_data` table. Optional trailing
  event args and nullable object args are part of the catalog.
- **ALE is not upstream Eluna**: mod-ale explicitly diverged and scripts are not interchangeable.
  This is why host profiles/bindings are mandatory rather than a single "Eluna" target.
- **Verified local clone** (2026-10-10, commit `84b85cc`): `Hooks.h` is authoritative for enum IDs
  (Packet, Server, Player, Guild, Group, Vehicle, Creature, GameObject, Item, Gossip, BG, Instance,
  Ticket, Spell, AllCreature); `LuaFunctions.cpp` for method names; `methods/*.h` for prototypes and
  defaults; `hooks/*.cpp` for actual parameter order. No example `lua_scripts` ship; the extension
  sources under `src/LuaEngine/extensions/` are the only samples. Scripts are unsandboxed (full
  `io`/`os`/`package`), so only trusted documents should be exported.

### ACWorldEdit constraints (verified in the repo)

- `Foundation/` is a plain `net8.0` library, Godot-free; the xUnit project references it. Graph model,
  catalog, validation, IR and Lua emitter belong there. `Scripts/Ui/` is the Godot layer.
- There is no graph/canvas/code-editor code today; SmartAI (`Foundation/Smart`, `Scripts/Ui/Behavior`)
  is the closest rule editor. Catalog generators (`Tools/generate_smart_catalog.py`,
  `generate_condition_catalog.py`) are the precedent for a generated ALE catalog.
- UI is built in C#. Floating windows derive from `ContentEditorWindow` and are configured before
  `AddChild` in `Scripts/Viewer/Hud/EditorsWiring.cs`; menus are in `ViewerShell.MenuBar.cs`.
  Status goes to `StatusLog`/`StatusBar`, never the Inspector.
- No project/workspace file exists; settings are `user://settings.cfg` via `UserSettings`; Foundation
  receives absolute paths. No window-layout persistence.
- The existing draft/change-log/SQL export path is row-specific. Script graphs are a documented
  exception: they save as files and export as Lua, bypassing `InteractionDraft`/`SqlExport`.
- `Foundation` owns the model, catalog, validation, IR and string serialization, but **not user file
  I/O**: `Scripts/Data/Scripting` (Godot-free, xUnit-covered) owns workspace scan/load/save and
  export, and the UI resolves `user://`/absolute paths and passes them down. This respects
  Foundation's "no files beyond draft storage" rule. The catalog loader reads only embedded assembly
  resources.
- Budgets: Foundation files ≤400 lines/methods ≤60; UI files ≤396; split into partials. UI control
  creation order is pinned by `Tests/golden/ui-*.json`.
- Tests: fast xUnit in `Tests/ACWorldEdit.Tests`; UI interaction tests in `Tests/ACWorldEdit.GdTests`
  with `[RequireGodotRuntime]`; one new file per feature. Live DB tests are `DbFact`-gated.

---

## Architecture

### The core-agnostic pipeline

```
 .acwscript (semantic graph)
        │  capability IDs, abstract event IDs, typed pins, literal values
        ▼
 ScriptValidator ── uses ── HostProfile (merged catalog + bindings)
        │
        ▼
 IrBuilder  ───────────────▶ IrModule (host- and language-agnostic)
        │
        ▼
 ScriptEmitter (LuaEmitter) ─ uses ─ HostProfile bindings
        │
        ▼
 generated .lua (ALE)          later: TC Eluna pack, C++ emitter, …
```

Rules that keep the boundary honest:

1. Graph JSON contains **no Lua text, function names, event numbers or host syntax** — only abstract
   IDs and typed values.
2. Capabilities and events are defined once in a shared base catalog (semantics + types + docs) and
   bound per host in a host pack (function names, arg order, event numbers, enum values, availability).
3. The IR knows only `Call(capabilityId, …)`, `Register(eventId, …)`, literals, variables, control
   flow and returns. Host-specific syntax lives only in emitters/bindings.
4. A test compiles the same document against two host profiles and asserts identical IR and
   host-appropriate text.
5. Switching host never rewrites the document; unsupported nodes become diagnostics.

**Design principles** (apply to every milestone):

1. The graph document is the source of truth; generated Lua is disposable and marked do-not-edit.
2. No host syntax, function name, event number or file path lives in the graph — only abstract IDs.
3. The UI never assembles Lua text; it mutates the model, and the compiler is the only writer.
4. Every graph is validated before export; errors block export, warnings ask.
5. Scripts are files, not database rows; they do not enter the draft/change-log/SQL-apply path.
6. Core object references live only for the handler call; anything longer is a GUID or a value.
7. Templates are ordinary graph documents and are compiled/tested exactly like user graphs.

**Value types**: `exec`, `bool`, `int`, `float`, `string`, typed IDs (`CreatureEntry`, `ItemEntry`,
`SpellId`, `QuestId`, `MapId`, …), object references (`Player`, `Creature`, `Unit`, `WorldObject`,
`GameObject`, `Item`, `Spell`, `SpellInfo`, `Aura`, `Quest`, `Map`, `Group`, `Guild`, `WorldPacket`,
`ALEQuery`, `ChatHandler`) that are nullable and **valid only inside the handler call**, `ObjectGuid`
and `GuidLow`, `List<T>` for query/range results, `Table` for instance data, and `Any` for
escape-hatch parameters. Inheritance follows ALE (`Creature → Unit → WorldObject → Object`), int
widens to float, and every nullable object pin displays its nullability. **Module variables may hold
only value types (bool/int/float/string/IDs/GUIDs/lists/tables), never object userdata**, because
core objects are invalidated when the handler returns. A single `ScriptTypeRegistry` maps every
semantic type to a stable GraphEdit port integer, pin color and assignability row, so the canvas and
the validator can never disagree.

### Layer/file layout

```
Foundation/                                  (Godot-free, xUnit-covered, NO user file I/O)
  Scripting/
    Model/
      ScriptDocument.cs            document, nodes, edges, comments, settings
      ScriptNode.cs                id, kind, position, properties, cached pin snapshot
      ScriptPin.cs                 semantic pin id, direction, type (snapshot for unknown kinds)
      ScriptEdge.cs                pin-to-pin connection
      ScriptVariable.cs            module-scope variable declaration
      ScriptFunction.cs            user function (params, returns, subgraph)
      ScriptValueType.cs           value-type identities
      ScriptTypeRegistry.cs        assignability, conversions, GraphEdit int mapping, pin colors
      ScriptDocumentJson.cs        System.Text.Json read/write, format version, unknown-field preservation
      ScriptDocumentMigrations.cs  v1→v2 hooks; reject newer formats safely
    Catalog/
      ScriptCapability.cs          semantic capability: effect (pure/read/write), tier, availability, performance
      ScriptEventDefinition.cs     semantic event: scope, key kind/type, params, return slots, performance
      ScriptHostProfile.cs         merged catalog for one host + conventions + return defaults
      ScriptCatalogLoader.cs       embedded JSON load + strict validation (unknown fields rejected)
      ScriptHostRegistry.cs        names every host (Ac/Tc ids live in their folders)
      Profiles/core/*.json         semantic capabilities/events/enums/tiers + docs links
      Profiles/ale-azerothcore/    host.json, event bindings, capability bindings, enums
      Profiles/eluna-trinitycore/  (milestone 5 proof pack)
    Validation/
      ScriptDiagnostic.cs          code, severity, message, graph/node/pin location
      ScriptValidator.cs           structural + semantic rules, dominance, registration keys
      ScriptTypeChecker.cs         pin/type compatibility + coercions
    Compilation/
      ScriptIr.cs                  IrModule/IrFunction/IrStmt/IrExpr incl. IrLatent/IrMultiLocal
      ScriptIrBuilder.cs           graph → IR (event roots, pure/read/write, contexts)
      ScriptCompiler.cs            validate → IR → emit → result (LuaText null on errors)
      ScriptCompilation.cs         text + diagnostics + source/catalog hashes
      IrMaterializer.cs            multi-return and write-expression hoisting
      IrOrder.cs                   deterministic function/registration ordering (no forward decls)
    Emission/
      ScriptEmitter.cs             emitter interface (Lua now, C++ later)
      LuaEmitter.cs                IR → Lua statements/expressions
      LuaWriter.cs                 indentation, comments, line endings
      LuaLiteral.cs                invariant number/string escaping, identifier sanitizing
    Templates/
      blank.acwscript.json
      creature-death.acwscript.json
      spell-hook.acwscript.json
      quest-giver.acwscript.json
      dungeon-encounter.acwscript.json
      command.acwscript.json
      world-event.acwscript.json
    ScriptTemplateLibrary.cs       embedded templates → new document (new id, fresh ids)
  AzerothCore/
    AcScriptHosts.cs               ALE host id/label + ALE profile wiring (layer-root dialect folder)
  TrinityCore/
    TcScriptHosts.cs               TC Eluna host id/label (milestone 5; layer-root dialect folder)

Scripts/Data/Scripting/                      (Godot-free, xUnit-covered, ALL user file I/O)
  ScriptWorkspace.cs               scan/load/save/delete .acwscript, atomic writes, stamps, duplicate ids
  ScriptManifest.cs                export-directory manifest: ownership, hashes, orphan reporting
  ScriptExportService.cs           compile+write .lua, refuse foreign/hash-changed files, all-or-nothing
  ScriptReferenceResolver.cs       optional live DB lookups for typed IDs (names/existence)
Scripts/Data/AzerothCore/                     (existing dialect folder)
  AcScriptReferences.cs            AzerothCore lookups (creature/item/quest/spell/map)
Scripts/Data/TrinityCore/                     (existing dialect folder)
  TcScriptReferences.cs            TrinityCore lookups (later; names/icons differ)

Scripts/Ui/Scripting/                        (Godot)
  ScriptWindowFrame.cs             draggable, resizable frame (not ContentEditorWindow)
  ScriptEditorWindow.cs            document lifecycle, status, panel wiring
  ScriptEditorWindow.Layout.cs     toolbar, split layout, panels
  ScriptEditorWindow.Documents.cs  new/open/save/close via ScriptWorkspace, dirty prompts
  ScriptEditorWindow.Compile.cs    debounce, compile, diagnostics, preview
  ScriptEditorWindow.Export.cs     export current/all via ScriptExportService
  ScriptGraphCanvas.cs             GraphEdit subclass: signals, zoom, context menu
  ScriptPortMap.cs                 pinId → slot index + input/output port index
  ScriptGraphCanvas.Nodes.cs       GraphNode creation/update from catalog
  ScriptGraphCanvas.Wires.cs       typed connection validation, disconnect, reroute
  ScriptNodeView.cs                one node view: pins, badges, tooltips
  ScriptNodeFactory.cs             catalog entry → node view
  NodePalette.cs                   searchable categorized palette, tier filter
  ScriptInspector.cs               selected-node properties + typed ID pickers
  ScriptVariablesPanel.cs          variables add/edit/remove
  ScriptFunctionsPanel.cs          functions add/edit/open
  ScriptProblemsPanel.cs           diagnostics list, click-to-focus
  LuaPreviewPanel.cs               read-only CodeEdit preview
  ScriptEditorController.cs        document mutations, selection, undo/redo
  ScriptUndoStack.cs               snapshot stack (bounded)
  ScriptClipboard.cs               copy/paste/duplicate with ID remap
  ScriptEditorSettings.cs          workspace/export dir/auto-export + settings UI hooks
  ScriptTemplatePicker.cs          new-document template dialog

Scripts/Viewer/Hud/                            (existing shell partials)
  ScriptingWiring.cs               ViewerShell partial: lazy window creation + View menu hook

Tools/
  generate_script_catalog.py       mod-ale source/docs → semantic catalog + host bindings + overrides
  script_catalog_overrides.json    curated display names, types, optional params, docs, tiers
```

### Document model and JSON (v1)

```jsonc
{
  "format": 1,
  "id": "5f0c…",                       // stable; generated names derive from it
  "name": "Deadmines Boss",
  "description": "",
  "host": "ale-azerothcore",
  "runtimeMode": "compatibility",       // compatibility | multistate (experimental) | auto
  "variables": [
    { "id": "v1", "name": "phase", "type": "int", "default": 0 }
  ],
  "functions": [
    {
      "id": "f1", "name": "SpawnWave",
      "parameters": [ { "id": "p1", "name": "wave", "type": "int" } ],
      "returns": [ { "name": "ok", "type": "bool" } ],
      "graph": { "nodes": [], "edges": [], "comments": [] }
    }
  ],
  "graph": {
    "nodes": [
      {
        "id": "n1",
        "kind": "event.creature.on_died",
        "position": { "x": 0, "y": 0 },
        "properties": { "entry": 1234, "shots": 0 },
        "pins": [
          { "id": "exec", "direction": "out", "type": "exec" },
          { "id": "creature", "direction": "out", "type": "Creature" },
          { "id": "killer", "direction": "out", "type": "Unit" }
        ]
      },
      {
        "id": "n2",
        "kind": "unit.cast_spell",
        "position": { "x": 320, "y": 0 },
        "properties": {},
        "pins": [
          { "id": "exec", "direction": "in", "type": "exec" },
          { "id": "target", "direction": "in", "type": "Unit" },
          { "id": "spell", "direction": "in", "type": "SpellId" },
          { "id": "triggered", "direction": "in", "type": "bool" }
        ],
        "literals": { "spell": 133, "triggered": false }
      }
    ],
    "edges": [
      { "id": "e1", "from": { "node": "n1", "pin": "exec" },
                    "to":   { "node": "n2", "pin": "exec" } },
      { "id": "e2", "from": { "node": "n1", "pin": "creature" },
                    "to":   { "node": "n2", "pin": "target" } }
    ],
    "comments": [
      { "id": "c1", "text": "Boss death", "position": { "x": 0, "y": -80 },
        "size": { "x": 320, "y": 120 }, "color": 0 }
    ]
  }
}
```

- Pins are **semantic IDs with a cached snapshot on every node** (`id`, `direction`, `type`). The
  catalog refreshes the snapshot when the node kind is known; an unknown or removed kind keeps the
  snapshot so its edges still render as a disabled placeholder. Edges reference pin IDs, never
  GraphEdit slot/port indices.
- Literal values live in `literals`; a connection overrides a literal. One data connection per input.
- Unknown node kinds and unknown JSON fields are preserved on save (forward compatibility); an
  unknown node renders as a disabled placeholder with an error diagnostic.
- `format` gates migrations; a newer format is refused with a clear message, never rewritten.
- Event nodes with return slots (e.g. On Chat → `suppress`, `newMessage`) expose those slots as
  optional inputs on the event node and compile to `return suppress, newMessage`; unconnected slots
  emit `nil` (never a synthesized default). Early exits use a context-aware `Return` node.

### Host profile / catalog JSON

Shared base definition (one file per domain):

```jsonc
{
  "id": "unit.cast_spell",
  "display": "Cast Spell",
  "category": "Unit",
  "receiver": ["Unit"],
  "params": [
    { "name": "spell", "type": "SpellId" },
    { "name": "target", "type": "Unit", "optional": true },
    { "name": "triggered", "type": "bool", "optional": true, "default": false }
  ],
  "returns": [],
  "effect": "write",                 // pure | read | write (write requires exec in/out)
  "tier": "core",                    // core | standard | advanced | hidden
  "availability": "implemented",
  "performance": "normal",           // normal | high-frequency | blocking
  "doc": "https://www.azerothcore.org/eluna/Unit/CastSpell.html"
}
```

Shared event definition:

```jsonc
{
  "id": "event.creature.on_died",
  "display": "Creature · On Death",
  "category": "Creature",
  "scopeParams": [ { "name": "entry", "type": "CreatureEntry" } ],
  "params": [
    { "name": "creature", "type": "Creature" },
    { "name": "killer", "type": "Unit", "nullable": true }
  ],
  "returns": [],
  "availability": "implemented",
  "performance": "normal",
  "doc": "https://www.azerothcore.org/eluna/Global/RegisterCreatureEvent.html"
}
```

Host binding pack:

```jsonc
{
  "host": "ale-azerothcore",
  "capabilities": {
    "unit.cast_spell": {
      "call": "CastSpell",
      "receiver": "colon",                    // colon | dot | none | global
      "args": [                               // host order; structured, never a Lua template
        { "param": "target", "defaultLiteral": "nil" },
        { "param": "spell" },
        { "param": "triggered", "defaultLiteral": "false" }
      ],
      "returns": "none"                       // none | single | tuple
    }
  },
  "events": {
    "event.creature.on_died": {
      "register": "RegisterCreatureEvent",
      "scopeArgs": ["entry"],       // abstract scope values inserted before the event ID
      "eventId": 4,
      "eventName": "CREATURE_EVENT_ON_DIED",
      "handlerEventParam": "event",
      "returnDefaults": []          // emitted as nil slots for unconnected returns
    },
    "event.allcreature.on_add": {
      "register": "RegisterAllCreatureEvent",
      "scopeArgs": [],
      "eventId": 1,
      "eventName": "ALL_CREATURE_EVENT_ON_ADD"
    },
    "event.instance.on_initialize": {
      "register": "RegisterMapEvent",
      "scopeArgs": ["mapId"],
      "eventId": 1,
      "eventName": "INSTANCE_EVENT_ON_INITIALIZE"
    }
  },
  "enums": {
    "PowerType": [ { "name": "Mana", "value": 0 }, { "name": "Rage", "value": 1 } ],
    "QuestStatus": [
      { "name": "None", "value": 0 }, { "name": "Complete", "value": 1 },
      { "name": "Incomplete", "value": 3 }, { "name": "Failed", "value": 5 },
      { "name": "Rewarded", "value": 6 }
    ]
  },
  "conventions": {
    "luaVersion": "5.1-compatible",
    "objectTimer": { "register": "RegisterEvent", "callbackArgs": ["eventId","delay","repeats","object"] },
    "globalTimer": { "register": "CreateLuaEvent", "callbackArgs": ["eventId","delay","repeats"] },
    "asyncQuery": { "call": "WorldDBQueryAsync", "callbackArg": "query" },
    "caveats": [
      "Creature/GameObject timers tick only while in world; timers die with the object.",
      "Map:GetInstanceData returns nil for C++-scripted instances.",
      "Instance data restores from the last save on reload; save at checkpoints.",
      "Core object userdata is invalid after the handler returns; store GUIDs.",
      "RemoveEventById is a global function (no Global table) with an argument bug; prefer object-scoped timers."
    ]
  }
}
```

- Abstract events declare their scope parameters (`entry`, `mapId`, `instanceId`, `opcode`, `menuId`,
  or two keys for unique creatures `guid` + `instanceId`) and their typed handler parameters/return
  slots; host bindings supply the registration function, where the scope values go, the numeric
  event ID and whether the event is implemented. "Not Implemented" ALE events are catalogued with
  `availability: "unimplemented"` and shown disabled, not hidden.
- **Event identity is semantic** (`scope + event id`), never just the number: ALE reuses numeric
  values across scopes (InstanceEvents 1–7, ServerEvents 17–23 for map lifecycle, PacketEvents 5/7),
  and `RegisterUniqueCreatureEvent` takes `guid + instanceId`. Numeric IDs exist only in host packs.
- Host profiles contain **no Lua text, templates or snippets** — only structured bindings. A host
  that needs a special operation adds a reviewed emitter operation kind; an embedded snippet would be
  an untestable hidden dialect.
- Host packs may not redefine pins, parameter types or return slots; a mismatch is a load error.
  Profiles reject unknown fields; documents preserve them.
- Duplicate registrations are a **warning**, not an error (ALE allows composing handlers); exec
  fan-out, exec cycles and data cycles are errors.
- Base files define semantics/types/docs once; host packs only bind, including **argument reordering**:
  ALE's `Unit:CastSpell(target, spell, triggered)` puts the target first even if the abstract
  capability lists the spell first. Host packs may add host-specific capabilities (e.g. ALEQuery)
  that other hosts simply do not offer.
- Loader enforces: unique IDs, every binding argument references a declared parameter, every
  capability has a binding (or an explicit unavailable marker) for the selected host, every event has
  a numeric ID and a registration function, and no host pack redefines a pin or return.
- Typed IDs (`CreatureEntry`, `GameObjectEntry`, `ItemEntry`, `SpellId`, `QuestId`, `MapId`,
  `InstanceId`, `PhaseMask`, `FactionId`) drive pickers and validation but emit as numbers.
  Parameter metadata also carries **units** where ALE is inconsistent — `SpawnCreature` uses
  milliseconds, `SummonGameObject` uses seconds, `PerformIngameSpawn` mixes both by spawn type — so
  the inspector labels and validates them.

### Compiler pipeline and Lua emission

1. **Load + migrate** the document; validate structure.
2. **Resolve profile** for `document.host`; unknown capabilities/events become diagnostics.
3. **Type check** every connection and literal; check required inputs, exec wiring, returns.
4. **Build IR** (`IrCall` and `IrRegistration` carry only semantic IDs — no host names or numbers):
   - Each event node becomes an `IrFunction` (`event`, then event params in order) plus an
     `IrRegistration` (semantic scope/key/event, shots).
   - Exec chains become statement lists; `Branch` → `IrIf`; `Sequence` → ordered statements;
     `ForEach`/`ForRange` → loop statements; `Return` → `IrReturn` with one nullable expression per
     declared slot; a null slot emits `nil`.
   - `pure` nodes become inline expressions; `read` nodes are pure reads; `write` calls feeding data
     pins are **materialized** into locals at the right point to preserve single evaluation and order.
   - Multi-return calls are always `IrMultiLocal(names[], call)` — never a multi-return call placed
     directly in an argument list (Lua expands the last call for extra arguments).
   - Variables are `IrVariableGet`/`IrVariableSet`; a write's data output is only valid after its exec
     statement (dominance check).
   - `IrLatent` exists in M1: it carries the continuation block, callback parameter list and an
     optional timer-ID local. Timer UI lands in M6, but the IR shape is fixed now.
   - The continuation may use the **object timer's own callback object**, module value variables and
     GUIDs re-resolved inside the callback — never object userdata captured from the enclosing
     handler, which ALE invalidates when that handler returns. Async DB continuations follow the same
     rule.
5. **Emit Lua** with the host's bindings:
   - **Emission order** is fixed: `local __vars = { … }` (literals only) → `local __fns = { … }` →
     event handlers → registrations. Using tables instead of one local per variable/function keeps the
     chunk under Lua's 200-local limit and removes function forward-declaration problems.
   - Header: generator/version, document name, host, source file. No timestamps (determinism).
     Generated files are marked do-not-edit.
   - Event handlers emit `local function __acw_n1(event, creature, killer)`; unused args are named
     `_`. User functions are fields of `__fns`, called as `__fns.SpawnWave(wave)`.
   - Event registrations at the end, deterministically ordered, using numeric event IDs with a
     trailing enum-name comment (`RegisterCreatureEvent(1234, 4, fn) -- CREATURE_EVENT_ON_DIED`).
     ALE does not define Lua-side enum constants, so generated code never relies on globals.
   - **Every declared return slot is emitted in order**, with `nil` for unconnected slots; never
     synthesize `false`/`true`/defaults. ALE's `CallAllFunctionsBool` keeps the event default unless a
     handler returns the opposite boolean, so gossip templates must explicitly return `false` to get
     the default action.
   - Omitted optional arguments are filled up to the last provided argument with the host binding's
     `defaultLiteral`; a multi-return call in an argument list is hoisted first.
   - Strings: quote/backslash/newline/tab/control escapes; UTF-8 preserved. Numbers: invariant
     culture; integral floats get `.0`; NaN/inf rejected. Identifiers sanitized against Lua
     keywords and collisions.
   - Lua 5.1-compatible only; `ipairs` for arrays; no `goto`/bitwise operators/`//`/`table.unpack`.
     Bitwise nodes emit ALE's `bit_and`/`bit_or`/`bit_xor`/`bit_not`/`bit_lshift`/`bit_rshift`.
   - Validate estimated locals >180 and upvalues >55 per function/chunk before emitting.
   - No `pcall` wrapping: ALE already wraps handler calls and reports errors; wrapping would swallow
     event return values.
6. **Result** carries text (`LuaText` null on errors), diagnostics with stable codes, source/catalog
   hashes and the output identity for export tracking.

**Worked example** — Creature On Death (entry 1234) → say, then reward the killer:

```lua
-- Generated by ACWorldEdit. Do not edit.
-- Script: Deadmines Boss
-- Host: ALE (AzerothCore)
-- Source: deadmines-boss.acwscript

local function __acw_n1(_, creature, killer)
    creature:SendUnitSay("The boss falls!", 0)
    local player = killer and killer:ToPlayer() or nil
    if player ~= nil then
        player:SendBroadcastMessage("You defeated the boss!")
        player:AddItem(12345, 1)
    end
end

RegisterCreatureEvent(1234, 4, __acw_n1) -- CREATURE_EVENT_ON_DIED
```

- Unused event arguments compile as `_`.
- A call on a **nullable** object expression (`killer`, which may be nil) emits a short-circuit
  guard (`killer and killer:ToPlayer()`), hoisted into a local when the result has multiple uses.
- `Unit:CastSpell(target, spell, triggered)` shows why host bindings reorder arguments: the abstract
  node can list the spell first while the ALE binding places the target first.

### Validation rules (severity in parentheses)

Diagnostics carry a stable code (`SC01xx` graph, `SC02xx` events, `SC03xx` variables/functions/
dominance, `SC04xx` Lua limits, `SC05xx` export safety, `SC06xx` lookup/profile), severity, message
and optional graph/node/pin location. Only errors block compilation/export.

- Unknown node kind, capability or event (error). Unknown nodes are preserved and render disabled.
- Capability/event not available on the selected host (error).
- Missing required input, wrong pin type, data input with multiple connections (error).
- Exec output → exec output / data input mismatch; exec fan-out (error).
- Exec cycle not passing through a loop/timer node; data cycle (error).
- Event node: missing scope values for entry/map/instance/packet/menu/unique registrations,
  out-of-range numeric ID (error); duplicate registration of the same scope key + event in one
  document (**warning** — ALE composes handlers).
- Cross-entry wiring: an exec edge joining two different event entry chains, or a Return node
  reachable from more than one event/function entry (error). Each event node is an independent exec
  root; event graphs may contain several events, but their chains never merge.
- Function: duplicate/ambiguous name, duplicate parameter, call arity/type mismatch, recursion without
  a supported pattern (error in v1).
- Return node used outside an event/function context or with mismatched return types (error); all
  declared return slots must be supplied (unconnected slots compile as `nil`).
- Undeclared variable or default type mismatch (error). Variable defaults are literals only, so a
  module variable can never depend on event-time state.
- **Object userdata stored in a module variable, or inside a list/table variable** (error): core
  object userdata is invalidated when the handler returns; store a GUID/low ID instead.
- **A timer or async-query continuation capturing an object pin from the enclosing handler** (error):
  the userdata is invalid by the time the callback runs. Continuations may use the object timer's
  own object argument, module value variables, or GUIDs re-resolved inside the callback.
- **Write-node dominance**: a write node's data output used on an exec path that does not execute the
  write statement, or a read/write node shared across two exec contexts (error).
- Empty event handler, unreachable node, unconnected exec output, unused variable/function; event
  performance warnings for high-frequency hooks (warning/info).
- Timer delay ≤ 0, invalid repeats, object timer without a receiver (error/warning); global timer
  cancellation that would hit ALE's `RemoveEventById` argument bug (warning, prefer object timers).
- Registration entry references a creature/GO/item/spell row that a live lookup confirms missing
  (error, blocks export); unchecked without a live session (warning "not checked").
- Estimated Lua locals >180 or upvalues >55, integer literals above 2^53 (error/warning).
- Multistate-incompatible world/map registration under `runtimeMode=multistate` (warning).
- Lua identifier collision after sanitizing (error).
- Export-only: generated filename collision, an existing file with no manifest entry or a different
  hash, a corrupt manifest, or Export All with any document containing errors (error). Orphaned
  manifest files are reported, never deleted automatically (info).

### UI composition

```
┌ Script editor ─────────────────────────────────────────────────────────────┐
│ [New ▾] [Save] [Compile] [Export ▾]   Host: ALE (AzerothCore) ▾   [Zoom ↺] │
├──────────────┬──────────────────────────────────────────┬──────────────────┤
│ Documents    │                                          │ Inspector        │
│ • Deadmines  │            Graph canvas                  │ (node props)     │
│ • Quest demo │      (GraphEdit, typed wires)            ├──────────────────┤
│ + New        │                                          │ Variables        │
│──────────────│                                          │ Functions        │
│ Palette      │                                          │                  │
│ [search…]    │                                          │                  │
├──────────────┴──────────────────────────────────────────┴──────────────────┤
│ Problems (0) │ Lua preview                                              │
└────────────────────────────────────────────────────────────────────────────┘
```

- **Window lifetime**: the window is created lazily on first `View → Script editor…` (a
  `ScriptingWiring.cs` ViewerShell partial keeps the layer and the optional window), then hidden on
  close rather than freed. No hidden control tree enters the startup UI dump; only the new menu item
  changes the golden, updated intentionally with
  `python Tools/state_golden.py --kind ui --profile wrath --update` (and `retail`).
  `ScriptWindowFrame` is a small draggable/resizable frame reusing `ClientStyle`, **not**
  `ContentEditorWindow` (which pins its `TargetSize` and is SQL-draft-specific).
- Theme: `ClientStyle.Theme()`/`Panel()` for the frame and panels; GraphEdit theme overrides for the
  grid, wire colours and node styles, matching the existing dark editor look.
- `ScriptGraphCanvas : GraphEdit` implements the signal contract: `ConnectionRequest`,
  `DisconnectionRequest`, `DeleteNodesRequest`, `CopyNodesRequest`, `PasteNodesRequest`,
  `DuplicateNodesRequest`, `NodeSelected`/`NodeDeselected`, `BeginNodeMove`/`EndNodeMove`,
  `PopupRequest` (add node/paste/comment/frame-all) and `ConnectionToEmpty` (open the palette filtered
  by the dragged pin type).
- **`ScriptPortMap`** owns the pin→slot/port mapping: `GraphNode.SetSlot` configures slots, but
  GraphEdit signals speak **port indices**, and input/output ports are numbered separately. Each node
  view stores `pinId → slot`, `pinId → inputPort?`, `pinId → outputPort?`; tests cross-check against
  the GraphNode slot getters. Node names are `Node_<id>`, row names `Pin_<direction>_<pinId>`.
- `AddValidConnectionType` is only a hover hint (global and directional); the controller enforces
  single input, no exec fan-out, type assignability (including inheritance/generics) and
  required-input rules in `_IsNodeHoverValid` and again in `ConnectionRequest` before the edge is
  added. `ConnectNode` is called inside `ConnectionRequest`, never from the model side.
- `ScriptNodeView : GraphNode`: header colour per category (events red, statements blue, read/pure
  green, control orange, variables purple); one slot row per pin; exec pins white/arrow, data pins
  coloured by type; title + subtitle (entry/event); badges for diagnostics; tooltip with docs link.
  Object pins carry a lifetime tooltip ("valid during this handler; store a GUID to keep it").
- Palette: searchable tree grouped by category with a **tier filter** (core/standard/advanced) and
  host filtering; add at view centre or at the mouse position; recent list.
- Inspector: parameter editors by type — bool toggle, number field, text field, enum dropdown, typed
  entity picker (reuse `RecordPicker` for creatures/items; add lookups for spell/quest/map), literal
  default, "connected" state. Validation badge and docs link.
- Variables panel: add/remove/rename variables; types and defaults; nodes reference variable IDs.
- Functions panel: create/rename/delete functions; edit parameters/returns; open a function graph on
  the canvas (breadcrumb back to the event graph). A function graph shows a **Function Entry** node
  with typed parameter outputs and a context **Return** node; event graphs use the event node's own
  return slots. Function Entry/Return anchors cannot be deleted; an event node may be deleted (its
  chain and registration disappear) after confirmation.
- Problems panel: severity/message/node; click selects and centres the node; compile runs debounced
  (≈300–400 ms) and on demand.
- Lua preview: read-only monospace `CodeEdit` with a lightweight Lua highlighter if practical;
  copy button; errors keep the previous good text visible with a banner.
- Undo/redo: bounded immutable-snapshot stack (100 entries), dirty state by canonical hash, one undo
  entry per node drag (coalesced on `EndNodeMove`); Ctrl+Z/Y and toolbar buttons. Copy/paste/duplicate
  remaps node IDs, rewires internal edges and offsets positions; cross-document paste is deferred.
- Comments: `GraphFrame` (confirmed in Godot 4.7) as a resizable titled box behind nodes, with
  attach/detach for grouping; reroute nodes in M3.
- Shortcuts are window-local and handled only while the window is focused/visible, so they do not
  fight the global `EditorMenu` accelerators: Delete, Ctrl+C/V/X/D, Ctrl+Z/Y, Ctrl+S, Ctrl+Enter
  compile, Ctrl+Shift+E export, Ctrl+F palette search, F/Home frame, Ctrl+0 zoom reset, Escape close.
  The menu only carries open/close and export commands.

### Persistence, export and settings

- **All user file I/O lives in `Scripts/Data/Scripting`** (`ScriptWorkspace`, `ScriptManifest`,
  `ScriptExportService`): scan/load/save/delete with atomic writes, duplicate-id detection, file
  stamps, manifest ownership, export. Foundation only serializes to/from strings and never resolves
  `user://` or opens a user file; the UI passes absolute paths.
- `UserSettings` gains a global `[scripting]` section (`target_host`, `auto_export`,
  `last_document`) and per-client-profile `[scripting.<profile>]` sections for `workspace`
  (default `user://scripts/wrath` or `.../retail`) and `export_dir`, mirroring the existing
  `[database.wrath]` / `[database.retail]` split. The scripting host is independent of the editor's
  client profile; entity pickers follow whichever DB/client session is connected. Workspace and
  export are per profile because each client profile can point at a different server.
- Workspace files: `<slug>.acwscript`; the document `id` is authoritative, the filename is cosmetic.
  Renames/saves are atomic (temp + move); external modification is detected by stamp/hash before
  overwriting, and the user chooses reload/overwrite/cancel.
- Export directory: the ALE `lua_scripts` folder (or a subfolder such as `lua_scripts/acworld`).
  Generated name `acw_<slug>_<shortid>.lua` guarantees ALE filename uniqueness; the slug is
  sanitized to ASCII and the short document id resolves collisions and renames.
- Export manifest lives **in the export directory** (`<export_dir>/.acworld-scripts.json`): document
  id → output path/hash/host. It owns the generated files: overwriting a file with no manifest entry
  or a different hash is refused; unmanaged files are never deleted; orphaned manifest entries are
  reported, not removed automatically. The manifest is a `.json` (ALE ignores it) and is never in a
  hidden directory ALE would skip if the user wants the exports visible.
- Export current/all; Export All is all-or-nothing. Export is blocked on compile errors; warnings
  require confirmation; auto-export on save runs only with zero errors and never overwrites a file
  whose hash changed. Deleting a document removes its workspace file and, with confirmation, its
  manifest-tracked export.
- Status reports the number of files and the reload hint (`.reload ale`, console or
  SEC_ADMINISTRATOR). The editor **never runs** the console command or `RunCommand`. If the server
  has `ALE.AutoReload` on, the export itself triggers a reload; otherwise the user reloads manually.
  A reload does not retrofit entry-event bindings onto already-spawned creatures — respawn/reload
  them to pick up a new script.
- Scripts deliberately bypass the DB draft/change-log path; document this exception in
  `docs/architecture/scripting.md` and `docs/plans/editor-plan.md`.
- External modification of an open document is detected by timestamp/hash before overwriting.

### Curated coverage target

| Domain | Event families | Representative capabilities | Target |
|---|---|---|---:|
| World / creature | Creature, AllCreature, Player, Server | say/yell/whisper, cast/aura, health/damage, combat, movement, threat, spawn/despawn, phase, world messages, commands | ~55 |
| Spell | Spell, Player/Creature spell hooks | CastSpell/CastCustomSpell/CastSpellAoF, Add/Remove/HasAura, DealDamage/DealHeal, dummy effect, SpellInfo checks | ~30 |
| Quest / gossip | Player/creature/GO/item quest events, gossip | gossip menu add/send/complete, quest status/add/complete/reward/abandon, items/money/reputation, quest template getters | ~40 |
| Dungeon | InstanceEvents, MAP server events | map info/players/creatures, instance-data get/set/save, spawn creature/GO, phases, weather | ~30 |
| Core flow | — | Branch, Sequence, ForEach, ForRange, comparisons, math, strings, variables, functions, lists, logging | ~30 |
| Utility | Server/global | DB query/execute/async, player lookup, timers, world messages, GUID helpers, bit helpers | ~20 |

The v1 target is ~150–200 capabilities plus every event registration; the extractor fills the long
tail later without compiler changes.

### Catalog bootstrap and growth

- Milestone 1 generates the **event catalog** from `Hooks.h` (enum IDs/names) and `hooks/*.cpp`
  (actual handler argument order), and hand-authors ~60 capabilities covering the world/creature
  slice and core flow.
- Milestone 4 extends `Tools/generate_script_catalog.py`, in the style of the SmartAI/condition
  generators. Source-of-truth order: `src/LuaEngine/Hooks.h` for enum IDs and names, `hooks/*.cpp`
  for the **actual** handler parameter push order (the doc comments are stale in several events),
  `LuaFunctions.cpp` for method names and registration keys, and `methods/*.h` for prototypes and
  defaults. The tool takes `--source` (or `ACWORLD_ALE_SOURCE`), merges `script_catalog_overrides.json`
  (curated display names, types, optionality, docs) and emits the checked-in base/host JSON.
  Generated JSON is marked do-not-edit; tests assert profile integrity and a stable catalog hash.
- mod-ale ships its own API-doc generator (`src/LuaEngine/docs/ALEDoc/parser.py`) that parses
  `@proto`/`@param`/`@return` annotations; the catalog tool can reuse its parsing rules (and its
  output) for method prototypes instead of writing a parser from scratch, while `hooks/*.cpp` stays
  authoritative for event argument order.
- Event scopes are inferred from the enum-name prefix (`PLAYER_EVENT_`, `CREATURE_EVENT_`, …) and
  cross-checked against the registration functions; parameter names come from the `(event, …)`
  comments, with types inferred from well-known names (`player`, `creature`, `go`, `map`, …) and
  corrected in the overrides file. Events with no registration function (or marked "Not
  Implemented" upstream) are catalogued as unavailable and shown disabled.
- Missing/ambiguous signatures are resolved manually in the overrides file; the tool reports
  coverage so gaps are visible.
- **Adding a capability** is a data change: add the abstract definition (types, docs, category),
  add the ALE binding (call name, receiver style, argument order, returns), add enum values if any,
  and run the catalog integrity tests. No compiler change is needed; the palette and type checker
  pick it up automatically.

### Entity references and live data

- Typed ID parameters use pickers. `RecordPicker` already accepts arbitrary async search functions
  and optional icons/tooltips (`PickerTab`, `RecordPicker.Show`), so creature/item/gameobject
  searches drop in; add search delegates for spell/quest/map as needed.
- `ScriptReferenceResolver` (Scripts/Data, Godot-free) optionally resolves IDs for names and
  existence warnings when a DB/client session is open. Compile never requires a connection; offline
  shows "not checked" rather than failing. A live lookup that **confirms a creature/GO/item/spell
  entry is missing** blocks export, because ALE aborts the chunk at registration.
- ALE has no creature/GO template lookup at runtime, so creature and GameObject references must be
  resolved by the editor's DB layer; spell/quest/item references can use client data.
- Client-data-backed names (spell/quest) use the existing Assets catalogs when available.

### C++ scripting parity

- `docs/reference/cpp-to-graph-parity.md` maps `ScriptMgr` hooks to ALE events:
  `PlayerScript` → player events, `CreatureScript` → creature events, `GameObjectScript` → GO events,
  `ItemScript` → item events, `SpellScript`/`AuraScript` → spell/aura events, `InstanceScript` →
  `RegisterMapEvent`/`InstanceEvents` with the `instance_data` table (map create/destroy/enter/leave
  are server events, not instance events), `WorldScript` → server events, gossip → gossip events,
  quest scripts → the player/creature/gameobject/item quest events (ALE has no quest-event
  registrar).
- It explicitly lists gaps: spell effect internals beyond `ON_DUMMY_EFFECT`, hooks ALE does not
  expose, per-core behaviour differences, and performance/threading notes. The graph editor is a
  prototyping/replacement tool for exposed hooks, not a guarantee of full C++ parity.

---

## Milestones

Each milestone ends with: build green, xUnit green, relevant gdUnit tests green, viewer inspected,
docs updated in the same change.

### Milestone 0 — Research spike (throwaway) — **hard gate for M2**

- Spike `GraphEdit` in C#: mixed input/output rows, `ScriptPortMap` slot↔port mapping,
  `ConnectNode` inside `ConnectionRequest`, exec fan-out rejection, coordinate conversion under
  zoom/scroll, `GraphFrame`, popup menu, node move/selection signals, shortcut propagation against
  `EditorMenu`, 200-node performance; confirm C# API names against Godot 4.7 docs.
- Verify ALE runtime against the local clone/live server: registration handler signatures/returns,
  timer ID semantics, instance-data attach/save, compatibility-mode stubs, gossip/command examples,
  Lua version built.
- Prototype the catalog extractor on the local mod-ale source (`Hooks.h`, `hooks/*.cpp`,
  `LuaFunctions.cpp`, `methods/*.h`); measure coverage.
- Exit criteria: a gdUnit4 spike proves slot/port mapping, connect/disconnect, zoom-independent node
  placement and shortcut propagation; ALE runtime facts confirmed; extractor lists the majority of
  methods. Spike code and probe scripts live under `artifacts/graph-spike/` (git-ignored) and are not
  committed; only the confirmed findings and design corrections land in the repo docs.
- **Do not start the M2 canvas until this gate passes.**

### Milestone 1 — Foundation core + persistence services

Order matters: schema before compiler.

- Document model + canonical JSON + versioning/unknown-field/pin-snapshot preservation.
- Type registry (semantic type → GraphEdit int, assignability/conversions, pin colors).
- Catalog schema + strict loader + semantic core + ALE seed profile (events generated from
  `Hooks.h`/`hooks/*.cpp`, ~60 hand-authored capabilities including Branch, Sequence, comparisons,
  object casts, world/creature actions). Capabilities carry `effect`, `tier`, `availability`,
  `performance`.
- Validator + type checker + stable diagnostic codes; IR (including `IrLatent`/`IrMultiLocal`/
  `IrVariableGet/Set` shapes fixed now); materializer; deterministic ordering; Lua emitter;
  templates `blank`, `creature-death`, `command`.
- `Scripts/Data/Scripting`: workspace scan/load/save/delete, export-directory manifest, export
  service with ownership/hash rules; **no file I/O in Foundation**.
- xUnit: round-trip/canonical hash, profile validation, type checker, every diagnostic rule, golden
  Lua, determinism, optional-arg defaults, all return slots, `__vars`/`__fns`, template compilation,
  unknown-node preservation, escaping, workspace/export safety.
- Docs: `docs/architecture/scripting.md` (initial), `docs/reference/scripting-capabilities.md`
  (initial seed), `docs/README.md`, `docs/architecture/overview.md` layer diagram, the Docs list in
  `AGENTS.md`, a detailed `docs/plans/scripting-plan.md` (this plan, committed), and a short **Phase
  25: Visual scripting** entry in `docs/plans/editor-plan.md` linking to it and to the architecture
  page.
- Acceptance: a golden Creature On Death graph compiles to expected Lua 5.1; same doc compiles twice
  byte-identically; malformed graphs produce the specified diagnostic codes; save/reload changes
  neither the canonical hash nor the Lua; export refuses foreign/hash-changed files.

### Milestone 2 — Editor MVP / world vertical slice

- Lazy `ScriptingWiring` (`InitScripting(layer)` in `BuildEditors`, window built on first open),
  `ScriptWindowFrame`, `ScriptEditorWindow` + layout; `ScriptGraphCanvas` + `ScriptPortMap` + node
  views + typed connections; palette; inspector; problems; Lua preview; save/load/export via the
  Data services.
- Settings for workspace/export dir; export current with manifest; reload hint status.
- Template picker with `creature-death`; View menu entry; intentional `state_golden.py --kind ui`
  update (the lazy window means only the menu item changes).
- gdUnit4 interaction tests: open from menu, new from template, add/connect/delete, port-map mapping,
  palette search, inspector edit, compile preview, problem click, save/load, export to temp,
  undo/redo, dirty prompts.
- Acceptance: a user can build Creature On Death (message + cast spell + add item), compile, export
  and `.reload ale` on a real server; no draft/change-log file is touched; tests green.

### Milestone 3 — Authoring depth

- ForEach, ForRange; extended math/string/boolean nodes; variables panel and get/set (dominance
  enforced); user functions (entry/return/call graphs); Return node; comments/reroute.
- Copy/paste/duplicate; multi-select; undo/redo polish; node search; function navigation.
- Templates: spell hook, quest giver (basic).
- Tests: control-flow golden files, variable dominance, function recursion diagnostics, UI flows.
- Acceptance: spell and quest starter graphs compile; complex exec/branch graphs round-trip.

### Milestone 4 — Domain packs, pickers, catalog tool

- Expand to ~150 curated capabilities across the four domains; all event registrations bound with
  tiers; duplicate registrations warned, not blocked.
- Typed ID pickers and `ScriptReferenceResolver` warnings; `RecordPicker` reuse.
- Templates: `quest-giver` (gossip menu + accept/reward; gossip callbacks explicitly return `false`
  where the default action is needed), `dungeon-encounter` (`RegisterMapEvent` `InstanceEvents` +
  `instance_data` + spawn skeleton), `world-event` (server events including the `MAP_EVENT_ON_*`
  lifecycle events, weather and game events).
- `Tools/generate_script_catalog.py` + `script_catalog_overrides.json` + integrity tests; gated
  `--check` against a local mod-ale source (`ACWORLD_ALE_SOURCE`).
- `docs/reference/cpp-to-graph-parity.md` first half.
- Acceptance: all templates compile for ALE; pickers resolve real IDs when connected and a
  confirmed-missing entry blocks export; catalog tool reproduces checked-in profiles
  deterministically; `--check` is clean against the pinned clone.

### Milestone 5 — Multi-host proof (TC Eluna)

Prove portability while the catalog is still small, before dungeon/instance complexity.

- Formalize `ScriptEmitter` + host registry; add a `eluna-trinitycore` proof profile (upstream Eluna
  headers; no live TC server required for compilation) and a test-only fake host; host switching
  re-validates.
- Cross-host golden tests: same abstract graph, different bindings; unsupported-node diagnostics.
- `docs/reference/host-capabilities.md` matrix; UI host selector and "experimental" badge.
- Acceptance: an ALE-authored graph compiles under the TC profile without graph changes; gaps are
  diagnostically clear; catalog `--check` gates both profiles.

### Milestone 6 — Dungeon, timers, lists, advanced runtime

- Map and instance scopes, instance-data get/set/index/save; conservative dungeon template;
  `RegisterMapEvent` attach-before-instance-creation documented.
- Timer/latent nodes (once/repeating, object-scoped cancel) with continuation lowering, callback
  parameters and warnings; continuations use the timer's object or GUID re-resolution only.
- `List<T>` + ForEach over query/range results; multi-return UI handling; logging/debug nodes.
- Host extension operations (reviewed emitter ops) instead of a raw Lua node; async DB continuations
  only if their callback shape is agreed, otherwise deferred; dynamic `RegisterUniqueCreatureEvent`
  advanced/deferred.
- Tests: instance data, timer continuation, list loops, unique-registration warnings, extension ops.
- Acceptance: a dungeon encounter with a repeating wave timer and saved instance data compiles and
  is documented as conservative; no raw Lua text can enter a document.

### Milestone 7 — Deployment polish and docs

- Export manifest orphan reporting/rename cleanup, export all, auto-export (zero errors only),
  overwrite refusal, reveal folder; optional `luac -p` syntax check when a Lua binary is available;
  SOAP live-reload research note (not implemented); no command execution from the editor.
- Large-graph performance pass; keyboard/tooltip polish; Lua highlighter/minimap polish; final docs
  (`completed-work.md`, `editor-plan.md`, `needed-tests.md`), parity matrix completion.
- Acceptance: end-to-end demo from blank graph to running server script for each domain; no orphan
  deletion; no foreign overwrite; correct reload instructions; all suites green; docs consistent.

---

## Test matrix

### xUnit — `Tests/ACWorldEdit.Tests/Scripting/`

| File | Covers |
|---|---|
| `ScriptDocumentTests.cs` | round-trip, canonical hash, version gate, unknown fields/node kinds/pins preserved, stable IDs |
| `ScriptTypeSystemTests.cs` | semantic → GraphEdit int mapping, assignability, object subtyping, typed ID → int, int → float, nil/nullability |
| `ScriptCatalogTests.cs` | unique IDs, binding arguments, docs/tiers, enum values, profile version/hash, host packs cannot redefine pins |
| `ScriptCatalogFidelityTests.cs` | event IDs/names and handler parameter counts match a checked-in fixture generated from the pinned ALE commit (`Hooks.h` + `hooks/*.cpp`); refresh is an intentional tool run |
| `ScriptRegistrationTests.cs` | scope args for entry/map/instance/packet/menu/unique registrations; map lifecycle vs instance events; duplicate warning |
| `ScriptLifetimeTests.cs` | object-typed variables and captured object pins are rejected; GUID re-resolution passes |
| `ScriptValidatorTests.cs` | one theory per diagnostic code and severity; graph/node/pin locations |
| `ScriptCompilerTests.cs` | evaluation order, read/write effects, multi-return materialization, optional-arg defaults, all return slots as `nil`, branch/sequence, variables/functions, escaping, determinism |
| `ScriptCompilerGoldenTests.cs` | template graphs versus committed expected Lua files (copied to test output) |
| `ScriptTemplateTests.cs` | every template loads, validates and compiles; template × host matrix |
| `ScriptCapabilitySmokeTests.cs` | generated theory: every capability in a one-node graph compiles without emitter failure |
| `ScriptWorkspaceTests.cs` | scan/load/save/delete, duplicate IDs, slug collisions, path traversal, stamps, atomic writes |
| `ScriptExportTests.cs` | manifest ownership/hash, foreign-file refusal, export-all abort, filename collisions, orphan reporting |
| `ScriptTimerTests.cs` | object/global timer callbacks, repeats, object-scoped cancellation, captured-object rejection |
| `ScriptInstanceDataTests.cs` | instance-data get/set/save emission and the `RegisterMapEvent` attach pattern |
| `ScriptHostProfileTests.cs` | same doc under two hosts; identical IR; host-specific text |
| `ScriptLuaLiteralTests.cs` | string escapes, UTF-8, numbers, identifiers, reserved words, locals/upvalues limits |
| `ScriptReferenceResolverTests.cs` | offline behaviour (no DB); `DbFact` live lookup one file per feature |
| `ScriptCatalogSourceTests.cs` | extractor `--check` against a local mod-ale source; event/signature drift (`AleSourceFact`, `ACWORLD_ALE_SOURCE`) |
| `ScriptLua51SyntaxTests.cs` | forbidden-token scan always; optional `luac5.1 -p`/`luajit` parse (`Lua51Fact`, `ACWORLD_LUA51`) |

Add `AleSourceFact` and `Lua51Fact` to `Tests/ACWorldEdit.Tests/Support/Attributes.cs`. Golden Lua
files live in `Tests/ACWorldEdit.Tests/Scripting/Golden/` and are copied to output by a `None` item
with `CopyToOutputDirectory=PreserveNewest`.

### gdUnit4 — `Tests/ACWorldEdit.GdTests/`

| File | Covers |
|---|---|
| `ScriptEditorWindowTests.cs` | open from menu (lazy construction), layout, document list, status, dirty prompts |
| `ScriptGraphCanvasTests.cs` | add node, `ScriptPortMap` slot/port mapping, typed connect/disconnect, invalid connection rejected, move persistence, delete, one input-driven drag smoke |
| `NodePaletteTests.cs` | search, category/tier filter, host filtering, add at position |
| `ScriptInspectorTests.cs` | property edits, enum options, entity picker, connected-state display |
| `ScriptEditorCompileTests.cs` | compile preview, diagnostics list, click-to-focus, stale-preview banner |
| `ScriptEditorPersistenceTests.cs` | save/load round-trip in a temp workspace, external-change prompt, export to temp dir |
| `ScriptVariablesFunctionsTests.cs` | variable/function creation, call node appears, rename updates |
| `ScriptEditorUndoTests.cs` | undo/redo across add/connect/move/delete, copy/paste/duplicate |
| `ScriptingSettingsTests.cs` | `[scripting]` defaults/round-trip, workspace/export path handling |

Every suite carries `[TestSuite]` and `[RequireGodotRuntime]`.

Commands:

```bash
dotnet build ACWorldEdit.csproj -p:NuGetAudit=false
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --filter "FullyQualifiedName~Scripting"
dotnet test Tests/ACWorldEdit.GdTests -c Debug -p:NuGetAudit=false   # GODOT_BIN set
ACWORLD_ALE_SOURCE=/path/to/mod-ale dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --filter "FullyQualifiedName~ScriptCatalogSourceTests"
ACWORLD_LUA51=/usr/bin/luac5.1 dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --filter "FullyQualifiedName~ScriptLua51SyntaxTests"
python launch_viewer.py
python launch_viewer.py --smoke
```

UI golden: `python Tools/state_golden.py --kind ui --profile wrath --update` and the same for
`retail`, only when the menu/control-tree change is intended. `ui_golden.py` target dumps are used
only if a scripting target dump is added.

---

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| GraphEdit slot/port/coordinate behaviour is subtle | M0 hard gate; `ScriptPortMap`; `ConnectNode` inside `ConnectionRequest`; controller isolation; gdUnit4 mapping tests |
| Lua codegen edge cases (evaluation order, multi-return, optional args, locals/upvalues, 5.1 syntax) | `IrMultiLocal`, effect-aware materialization, binding defaults, `__vars`/`__fns`, golden files, per-capability smoke, token scan + optional `luac5.1` |
| Catalog drifts from ALE | Generated profile + overrides + integrity tests; `--check` gate; docs links/tiers; version stamp |
| Event identity confusion from reused numeric IDs | Semantic `(scope, event id)` keys; numeric IDs only in host packs; registration tests per scope |
| A raw Lua escape hatch would break the abstraction | No Lua text in documents or profiles; host extension operations are reviewed emitter ops; unavailable elsewhere |
| Catalog scale/curation | Tiers (core/standard/advanced/hidden), extractor, coverage report, per-capability smoke compilation |
| ALE instance data/multistate/reload caveats surprise users | Conservative templates, tooltips/warnings, runtime-mode setting, parity/caveat docs |
| Object userdata stored or captured past the handler | Validator errors for object-typed variables and captured object pins; timer/async nodes use the timer object or GUID re-resolution; docs |
| A missing entry aborts the Lua chunk at registration | Live ID validation blocks export on confirmed-missing rows; deterministic registration order; documented offline risk |
| Upstream hooks drift from the docs | Catalog generated from `hooks/*.cpp`/`Hooks.h`/`LuaFunctions.cpp`, not docs; fidelity tests per event; snapshot hash |
| Export damages hand-written scripts | `acw_` prefix, manifest ownership, overwrite confirmation, never delete untracked files |
| Scope creep | Curated core, explicit non-goals, milestone gates |
| UI golden/budget churn | Partials per responsibility, intentional golden updates, budget checks |
| No live ALE server in CI | Compile/golden tests in xUnit; optional `luac -p`; manual in-game verification for milestone 2 |
| Entity pickers missing for spell/quest/map | Raw ID entry with validation warning; spell/quest use client data, creature/GO use the DB; extend lookups incrementally |
| ALE/Eluna incompatibility confuses host switching | Explicit host packs, experimental badge, capability matrix, diagnostics |

---

## Implementation decisions and spike checks

- `ScriptWindowFrame` is a small draggable/resizable frame reusing `ClientStyle`, **not**
  `ContentEditorWindow`: the base pins `TargetSize` every frame and is SQL-draft-specific. The window
  is constructed lazily on first open (`ScriptingWiring`) and hidden on close, so no hidden control
  tree enters the startup UI golden; only the View menu item changes.
- File I/O lives in `Scripts/Data/Scripting`, never in Foundation. Foundation serializes strings and
  embeds the catalog/templates; the UI resolves `user://`/absolute paths and passes them down.
- The Lua preview uses a read-only `CodeEdit` with a small custom `SyntaxHighlighter` (keywords,
  strings, numbers, comments); if that fights the layout, ship plain monospace first.
- Comments use `GraphFrame` (confirmed in Godot 4.7) with attach/detach grouping.
- Source-level ALE facts are confirmed for commit `84b85cc` (single-state, callback argument orders,
  registration signatures, userdata invalidation, instance-data attach rule).
- Milestone 0's GraphEdit and live-ALE findings are recorded in
  [`docs/architecture/scripting.md`](../architecture/scripting.md): the canvas/port contract, the
  graph↔screen conversion, `RemoveEventById` being a global function, Lua 5.2 on the local build,
  the missing-entry chunk abort, and extractor coverage. Instance-data attach/save, object-timer
  ticks and player/creature handler signatures remain source-verified (no client was attached).

---

## Suggested commit sequence

Milestone 1 (Foundation core + persistence services):

1. `feat(foundation): add script document model and JSON format`
2. `feat(foundation): add the script type registry and pin model`
3. `feat(foundation): add the scripting catalog and ALE host profile`
4. `feat(foundation): validate script graphs and pin types`
5. `feat(foundation): build host-agnostic IR from script graphs`
6. `feat(foundation): emit Lua 5.1 from the script IR`
7. `feat(data): store and export script documents`
8. `feat(foundation): add script templates and golden tests`
9. `docs(scripting): document the scripting subsystem and capability catalog`

Milestone 2 (editor MVP):

1. `feat(ui): add the lazy scripting window frame`
2. `feat(ui): render script graphs with typed ports`
3. `feat(ui): add the node palette and node inspector`
4. `feat(ui): compile graphs and show the Lua preview with problems`
5. `feat(ui): save, load and export script documents`
6. `test(ui): cover the script editor interaction flows`
7. `docs(scripting): document the script editor workflow and export`

Each commit keeps the build green and includes its tests; docs that describe the touched code land
in the same commit.

---

## Verification

1. Build: `dotnet build ACWorldEdit.csproj -p:NuGetAudit=false`.
2. xUnit: `dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false`.
3. UI tests: `dotnet test Tests/ACWorldEdit.GdTests -c Debug -p:NuGetAudit=false` with `GODOT_BIN`.
4. Catalog/source gates when available:
   `ACWORLD_ALE_SOURCE=<mod-ale path> dotnet test ... --filter ScriptCatalogSourceTests` and
   `ACWORLD_LUA51=<luac path> dotnet test ... --filter ScriptLua51SyntaxTests`.
5. Golden update only when intended:
   `python Tools/state_golden.py --kind ui --profile wrath --update` (and `retail`).
6. Launch: `python launch_viewer.py`, open View → Script editor, create a Creature On Death template,
   add a message/cast/item, compile, confirm the preview, export to a temp `lua_scripts` folder.
7. Real server: point export at the AzerothCore `lua_scripts` folder (the local dev clone's module
   lives at `.../azerothcore-wotlk/modules/mod-ale`), export, run `.reload ale` (or let
   `ALE.AutoReload` pick the file up), kill the creature, confirm the effect.
8. Repeat for spell, quest, dungeon and command templates as milestones land.
9. Update `docs/status/*` pages in the same change; run the existing smoke
   (`python launch_viewer.py --smoke`) before finishing.
