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
`flow.branch`, `flow.sequence`, `flow.for_range`, `flow.foreach`, `list.create`, variable get/set,
parameter get, user-function call/return, `timer.once`/`timer.repeat` with `timer.event_id`/
`timer.object`/`timer.repeats` in the continuation, comparisons (`flow.equal`, `not_equal`, `less`,
`less_equal`, `greater`, `greater_equal`, `and`, `or`, `not`) and the numeric/string operators
(`math.add`, `subtract`, `multiply`, `divide`, `modulo`, `min`, `max`, `string.concat`,
`string.length`). The validator,
palette and IR builder share that one list, so a host pack cannot replace them.

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
variables, `__fns` for user functions, one `__acw_<node>` function per event, and the
registrations (`RegisterCreatureEvent(entry, event, handler)` etc.). `flow.for_range` lowers to an
inclusive `for i = from, to do … end` and `flow.foreach` to `for _, item in ipairs(list) do … end`;
`timer.once`/`timer.repeat` emit a local callback function and `obj:RegisterEvent(fn, delayMs,
repeats)` with the continuation inside it (the callback parameters are `eventId`, `delay`,
`repeats`, `obj`); a function body is a set of roots (nodes with an unconnected
exec input) plus a Return node, and `flow.parameter_get` resolves to the function parameter (the
legacy expression-only Return shape still compiles). Output is deterministic: the same document
compiles byte-identically, including ordering and hashes. `ScriptCompiler.OutputName` is
`acw_<slug>_<shortid>.lua`.

### Templates

`ScriptTemplateLibrary` loads embedded `.acwscript.json` files (`blank`, `command`,
`creature-death`, `dungeon-encounter`, `quest-giver`, `spell-cast`, `world-event`) and `Create`
clones one with a fresh document id,
node/edge/comment ids. A loaded template is never mutated.

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

## Editor (Milestone 2)

`Scripts/Ui/Scripting` is the Godot editor, entered from **View → Script editor…**:

| File | Role |
|---|---|
| `ScriptWindowFrame.cs` | draggable/resizable client-styled frame, hidden on close; not `ContentEditorWindow` |
| `ScriptEditorWindow.cs` | toolbar, document list, palette, canvas, inspector, variables/functions, problems, Lua preview, folder settings, shortcuts |
| `ScriptGraphCanvas.cs` | GraphEdit signal handling, model mutations through the session, connection rules |
| `ScriptPortMap.cs` | pin → GraphNode slot and separate input/output port indices, with reverse lookups |
| `ScriptNodeView.cs` | one `GraphNode` per model node: title from the catalog, one slot row per pin, type colours |
| `Scripts/Viewer/Hud/ScriptingWiring.cs` | lazy window: built on first open, then hidden; per-profile folders |

- **Lazy lifetime.** The window is created on the first menu open and only hidden afterwards, so
  no scripting control tree enters the startup UI dump. Only the View menu item changes the golden
  (`Tools/state_golden.py --kind ui --update`).
- **Session edits.** `ScriptDocumentSession` keeps the document, its file path, a 100-entry
  undo/redo stack of canonical snapshots and dirty state by hash. Canvas operations call
  `Session.Edit`, so one user action is one undo entry; undo/redo rebuild the view tree.
- **Connections** are enforced in `ConnectPins` after GraphEdit's hover hints: an edge runs output →
  input, exec and data never mix, the target type must accept the source (object inheritance,
  int→float, typed IDs), a data input takes one connection and an exec output cannot fan out.
  `ConnectionRequest` calls the same method; `DisconnectionRequest` removes the matching edge.
- **Palette** lists implemented events, capabilities and language builtins, filtered by display
  name, kind or category; activating an entry adds it at the view centre and selects it.
- **Inspector** builds typed editors from the node's input pins: checkboxes for bools, number
  fields for ints/floats/typed IDs, text fields for strings, raw-JSON fields for lists/tables/other
  objects, plus event scope fields and a variable picker for get/set nodes. A connected input says
  so instead of offering a literal. Editing commits a new snapshot and recompiles.
- **Problems** list diagnostics with severity colours; selecting one selects and centres its node.
  The Lua preview keeps the last successful text when compilation fails, and the header shows the
  error count.
- **Folders…** edits the workspace and export directory and the auto-export flag; the viewer saves
  them through `UserSettings` in `[scripting]` / `[scripting.<profile>]`. Export reports the file
  count and the `.reload ale` hint; the editor never runs the console command itself.
- **Shortcuts** are handled only while the window is visible: Ctrl+S save, Ctrl+Z/Y undo/redo,
  Ctrl+Enter compile, Ctrl+Shift+E export, Ctrl+F palette search, Home frame all, Ctrl+0 zoom
  reset, Escape close (a dirty document asks before discarding).
- **Not yet in the MVP**: typed entity pickers (creature/item/spell lookups), copy/paste across
  documents, reroute nodes and timer/list nodes. These belong to the later milestones.

## Authoring depth (Milestone 3)

- **User functions.** The Functions panel creates, opens, edits and deletes functions; a function's
  name, parameters and returns are edited in a dialog, and its Return node gains the declared slots.
  Opening a function shows its graph on the same canvas (`ScriptGraphCanvas.Graph`), with
  `flow.parameter_get` nodes bound by parameter id. Event nodes are hidden from the palette while a
  function graph is open.
- **Loops.** `flow.for_range` lowers to an inclusive Lua numeric loop; the index pin is a local name
  inside the body. `flow.foreach` is deliberately absent until a list/table source exists (Milestone
  6), so the palette never offers a node that cannot produce a body.
- **Copy, paste, duplicate and multi-select.** GraphEdit's box selection drives the canvas; Ctrl+C/V
  copy a fragment (nodes plus internal edges), Ctrl+V and Ctrl+D remap node and edge ids and offset
  positions, and Delete removes the selection. The clipboard is per canvas (cross-document paste is
  deferred).
- **Comments.** Comment frames are `GraphFrame`s behind the nodes, stored as `ScriptComment` with
  position and size; dragging or resizing a frame updates the document on `EndNodeMove`.
- **Templates.** `spell-cast` and `quest-giver` join the template picker and have golden Lua.

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
