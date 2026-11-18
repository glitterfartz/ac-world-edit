# Visual scripting

The visual scripting editor authors `.acwscript` graph documents and compiles them for a host
runtime: a semantic graph, a host profile, a host- and language-agnostic IR, and an emitter. The
first target is Lua for the AzerothCore ALE/mod-ale runtime. Milestone 1 lands the Foundation
document model, catalog, validator, compiler and the Godot-free persistence/export services;
the Godot editor is Milestone 2. The full design is
[plans/scripting-plan.md](../plans/scripting-plan.md).

## Pipeline

```text
.acwscript (semantic IDs and typed values only)
        │
        ▼
ScriptValidator ── HostProfile (merged catalog + bindings)
        │
        ▼
IrBuilder ─────────▶ IrModule (no host names, numbers or syntax)
        │
        ▼
LuaEmitter ────────▶ generated .lua (retail/TC packs and other emitters later)
```

The graph never contains Lua text, function names, event numbers or file paths. Host packs own
those bindings, so switching host re-validates a document without rewriting it.

## Milestone 1 implementation

### Document model

`Foundation/Scripting/Model` holds the Godot-free document: `ScriptDocument` (format, id, name,
description, host, runtime mode, variables, functions, graph), `ScriptGraph`, `ScriptNode`,
`ScriptPin`, `ScriptEdge`, `ScriptVariable`, `ScriptFunction`, `ScriptComment`. JSON is camelCase,
indented on save, and `ScriptDocumentJson` produces a sorted-key, whitespace-free canonical form
whose SHA-256 is the dirty-state and compile-input hash (`CanonicalHash`). Every document, graph,
node, pin, edge and comment carries `[JsonExtensionData]`, so a newer writer's unknown fields
survive a load/save round trip; unknown node kinds and pins are left in the graph and reported by
the validator rather than discarded. `ScriptDocumentMigrations` accepts formats up to
`CurrentFormat = 1` and raises `ScriptFormatException` for a newer one.

### Type registry

`ScriptTypeRegistry.Default` maps each `ScriptValueType` to a unique GraphEdit port integer, a pin
colour and its assignability rules: object references follow the `Object → Unit/Player/Creature/
GameObject` hierarchy, `int` widens to `float`, typed IDs (`SpellId`, `ItemEntry`, `MapId`, …)
interconvert with `int`, and `Any` accepts everything. The registry also states which types may be
stored in a module variable (exec, `List` and `Table` may not).

### Catalog and seed profile

`Foundation/Scripting/Catalog` loads strict JSON (`UnmappedMemberHandling.Disallow`) from embedded
resources:

| File | Contents |
|---|---|
| `Profiles/core/capabilities.json` | 25 hand-authored semantic capabilities: object casts/GUID/entry/level/health, unit actions (say, yell, cast, auras, damage, heal), player actions, creature/gameobject respawn, world message and player lookup |
| `Profiles/core/enums.json` | host-neutral enums (`PowerType`, `QuestStatus`) |
| `Profiles/core/events.json` | 230 events generated from mod-ale's `Hooks.h`/`hooks/*.cpp` |
| `Profiles/ale-azerothcore/host.json` | host id/label, conventions and runtime caveats for ALE |
| `Profiles/ale-azerothcore/capability-bindings.json` | ALE call name, receiver style, argument order/defaults and return shape per capability |
| `Profiles/ale-azerothcore/event-bindings.json` | registrar, event number and scope arguments per event |

`Tools/generate_script_catalog.py` regenerates the event and event-binding files from a mod-ale
checkout (`--source`, `--output`); the capability lists stay curated. `ScriptCatalogLoader` rejects
duplicate ids, binding arguments without a matching parameter, missing required scope arguments,
malformed return shapes, implemented capabilities without a binding, and events without a
registration. `AcScriptHosts.Id` is `ale-azerothcore`; the profile is built lazily and hashed by
`ScriptCatalogHash` for exported provenance.

Language-level nodes are not host operations and live in `ScriptBuiltinCatalog` instead:
`flow.branch`, `flow.sequence`, variable get/set, user-function call/return, comparisons
(`flow.equal`, `not_equal`, `less`, `less_equal`, `greater`, `greater_equal`, `and`, `or`, `not`)
and the numeric operators (`math.add`, `subtract`, `multiply`, …). The validator, palette and IR
builder share that one list, so a host pack cannot replace them.

### Validation

`ScriptValidator` checks variables, functions and each event graph and returns stable diagnostic
codes (`ScriptDiagnosticCodes`):

| Range | Checks |
|---|---|
| `SC01xx` | unknown/duplicate node, pin or event, unavailable capability, duplicate node id |
| `SC02xx` | missing input, wrong type, multiple data inputs on one pin, exec fan-out, exec/data mixing, exec/data cycles, bad edge |
| `SC03xx` | missing event scope, duplicate registration, merged event chains, return context, unknown variable/function, variable default/storage, write dominance, unreachable node, unconnected write exec, high-frequency event, recursion, call arity, duplicate name |
| `SC04xx` | too many locals, integer literal above 2^53 (Lua precision) |
| `SC05xx` | export collision, foreign file, corrupt manifest, export-all blocked |
| `SC06xx` | missing host binding/entry, catalog profile mismatch |

Errors block compilation (`ScriptCompilation.LuaText` is null); warnings and infos stay in
`Diagnostics`. The compiler is the only writer of Lua text; Foundation never opens a file.

### IR and Lua emission

`ScriptIrBuilder` lowers each event's exec chain into `IrModule` statements (`IrCallStmt`,
`IrVarSet`, `IrLocal`, `IrMultiLocal`, `IrIf`, `IrWhile`, `IrForRange`, `IrForEach`, `IrLatent`,
`IrReturn`). Data reads resolve into expressions; an output read more than once materialises a
local; omitted optional arguments take the host binding's `defaultLiteral` (then the capability's
semantic `default`) and only trailing arguments with no default at all are dropped. Event scope
parameters become function parameters named after their semantic pins.

`LuaEmitter` writes Lua 5.1-compatible text: a generated-file header, `local __vars` for module
variables, `local __fns` for user functions, one `__acw_<node>` function per event, and the
registrations (`RegisterCreatureEvent(entry, event, handler)` etc.). Output is deterministic: the
same document compiles byte-identically, including ordering and hashes. `ScriptCompiler.OutputName`
is `acw_<slug>_<shortid>.lua`.

### Templates

`ScriptTemplateLibrary` loads embedded `.acwscript.json` files (`blank`, `creature-death`,
`command`) and `Create` clones one with a fresh document id, node/edge/comment ids. A loaded
template is never mutated.

### Workspace, manifest and export

All user file I/O lives in `Scripts/Data/Scripting`; the UI passes absolute paths.

- **`ScriptWorkspace`** scans a directory for `*.acwscript`, loads and saves atomically (sibling
  `.tmp`, flush, move), and deletes. A document keeps its existing file by id; a new document gets
  `<slug>.acwscript`, with `-<shortid>` (and a counter) when the slug is taken by another document.
  `Scan` reports malformed files and duplicate document ids as issues. A `ScriptFileStamp`
  (SHA-256, length, last-write time) lets save/delete refuse an externally modified file.
- **`ScriptManifest`** is `<export_dir>/.acworld-scripts.json`: format, then document id →
  `{outputName, hash, hostId, sourceHash, catalogHash}`. Export may overwrite a file only when the
  manifest owns it, the output name matches, and the file's hash still matches the recorded one.
  Untracked files are never deleted; `Orphans` reports entries whose file disappeared without
  removing them.
- **`ScriptExportService`** compiles first, then checks duplicate ids and output-name collisions,
  manifest ownership and file hashes, and only then writes each Lua file and the manifest. A failed
  run writes nothing; success reports each file's hash. Export errors carry `SC05xx` codes.

Scripts deliberately bypass the database draft/change-log path: a graph is a file, its generated
Lua is a file, and nothing reaches the world database. The export directory is the ALE
`lua_scripts` folder (or a subfolder such as `lua_scripts/acworld`); after export the user reloads
with `.reload ale` unless `ALE.AutoReload` is on.

## Confirmed canvas contract (Godot 4.7.2)

Verified by the Milestone 0 gdUnit4 spike (`artifacts/graph-spike/`, git-ignored):

- **Slots are child rows.** A `GraphNode` row exists only when the node has a child control for it;
  `SetSlot(rowIndex, …)` has no effect on a node with no children.
- **Port indices are not row indices.** Inputs and outputs are numbered separately, and
  `GraphNode.GetInputPortSlot(port)` / `GetOutputPortSlot(port)` map a port back to its row.
  `ScriptPortMap` must store `pinId → row slot` and the separate input/output port index.
- **Connections** live in `GraphEdit.Connections` (`Array<Dictionary>` with `from_node`,
  `from_port`, `to_node`, `to_port`); there is no `GetConnectionList`. Use `ConnectNode(from,
  fromPort, to, toPort)` and `DisconnectNode(...)`.
- **Wiring is request-driven.** `ConnectionRequest(StringName fromNode, long fromPort, StringName
  toNode, long toPort)` fires first; the controller calls `ConnectNode` only when it accepts, which
  is how exec fan-out and type mismatches are rejected. `_IsNodeHoverValid` is a hover hint, not the
  enforcement point.
- **Signal signatures:** `DisconnectionRequest` mirrors `ConnectionRequest`;
  `ConnectionToEmpty(StringName, long, Vector2)` takes three arguments; `DeleteNodesRequest(Array)`
  is untyped; `PopupRequest(Vector2)`, `NodeSelected(Node)`, `NodeDeselected(Node)`,
  `BeginNodeMove()` and `EndNodeMove()` carry the expected payloads.
- **Frames:** `GraphFrame` exists with `Title`, `TintColor` and `GetTitlebarHBox()`.
  `AttachGraphElementToFrame(node, frame)` / `DetachGraphElementFromFrame(node)` group nodes;
  `GetAttachedNodesOfFrame(frame)` lists them and `GetElementFrame(node)` returns the `GraphFrame`.
- **Graph space:** persist `PositionOffset`. The graph→screen mapping is
  `screen = scroll + (graph - scroll) * zoom`; the inverse places a node at the mouse. Changing
  `Zoom` re-centres `ScrollOffset` on the viewport centre, so read `ScrollOffset` after a zoom.
- **Shortcuts:** a focused `GraphEdit` receives keys in `_GuiInput`; calling `AcceptEvent()` there
  prevents `_UnhandledKeyInput` from firing, so window-local shortcuts do not leak to the global
  `EditorMenu`. An unfocused graph does not consume them.
- **Scale:** 200 nodes and 199 connections build in roughly 100 ms in the headless test process.

## Confirmed ALE runtime facts (pinned `84b85cc`)

Verified against the local mod-ale clone and a live worldserver:

- **Lua version:** the local build runs **Lua 5.2**. Generated code stays Lua 5.1/LuaJIT-compatible
  (no `goto`, `//`, bitwise operators or `table.unpack`), so it runs on the 5.1, 5.2 and LuaJIT
  builds ALE supports.
- **Runtime mode:** `IsCompatibilityMode()` returns `true` and `GetStateMapId()` returns `-1`. The
  document keeps `runtimeMode`, but multistate support is experimental.
- **Registrars:** all registration globals load (`RegisterCreatureEvent`, `RegisterMapEvent`,
  `RegisterInstanceEvent`, `RegisterAllCreatureEvent`, `RegisterUniqueCreatureEvent`,
  `RegisterPacketEvent`, `RegisterPlayerGossipEvent`, and the rest).
- **No `Global` table.** `RemoveEventById` and `RemoveEvents` are **global functions**. The
  `RemoveEventById` argument bug still applies, so generated timer cancellation uses the
  object-scoped form.
- **Timers:** `CreateLuaEvent(fn, delay, repeats)` returns a numeric id and calls back
  `(eventId, delay, repeats, obj)`; `repeats = 1` fires once, `repeats = 0` is infinite, and `obj`
  is `nil` for a global timer.
- **Registration validation aborts the chunk.** `RegisterCreatureEvent(999999, …)` prints
  `Couldn't find a creature with (ID: 999999)!` and stops that file at the registration; statements
  before it ran, statements after it did not, and other script files still loaded. This is why a live
  lookup that confirms a missing entry blocks export.
- **Instance data:** `Map:GetInstanceData()` returns `nil` for the open-world map (map 0); a table
  exists only when ALE owns the instance script. Instance attach/save is still source-verified only.
- **Reload:** `.reload ale` through the console rebuilds the Lua state and re-runs scripts. The
  `ALE.AutoReload` watcher did not fire in this environment even with the module config loaded, so
  auto-reload is unverified; the editor's reload hint stays `.reload ale` (console or
  `SEC_ADMINISTRATOR`).
- **Not yet live-checked:** object-timer ticks/cancellation, instance-data attach/save, and the
  player/creature handler signatures need a client or a dungeon instance. They remain
  source-verified against `hooks/*.cpp` and are checked during the editor milestone.

## Catalog extractor coverage

The Milestone 0 prototype (`artifacts/graph-spike/extract_ale_catalog.py`) reads `Hooks.h`,
`hooks/*.cpp`, `LuaFunctions.cpp` and `methods/*.h`:

- `Hooks.h`: 218 enum entries, 210 implemented (8 marked "Not Implemented").
- A macro-aware parser maps all **210 implemented events** to their `hooks/*.cpp` push list. The six
  initial misses were macro variants (`START_HOOK_SERVER`, `START_HOOK_PACKET`, and the gossip
  file's three-argument `START_HOOK`); the real tool must detect the event token in the macro
  arguments instead of assuming a position.
- `LuaFunctions.cpp`: 37 method tables and 1,713 entries, including the playerbot and spell tables
  that the curated core will hide behind tiers.
- `methods/*.h`: 1,336 documented function definitions across 30 files.

## See also

- [plans/scripting-plan.md](../plans/scripting-plan.md): the full design, milestones and tests.
- [architecture/overview.md](overview.md): layers and dependency rules.
