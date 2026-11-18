# Visual scripting

The visual scripting editor authors `.acwscript` graph documents and compiles them for a host
runtime: a semantic graph, a host profile, a host- and language-agnostic IR, and an emitter. The
first target is Lua for the AzerothCore ALE/mod-ale runtime. The current implementation is the
Milestone 0 research spike; the editor itself, the catalog and the compiler land in later milestones.
The full design is [plans/scripting-plan.md](../plans/scripting-plan.md).

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
