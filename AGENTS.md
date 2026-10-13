# Working on ACWorldEdit

Read PLAN.md and PROGRESS.md, inspect current code and Git status, then implement the next
requested milestone. Research was deliberately removed; do not recreate it or use old phase
documents as prerequisites. Current user instructions take precedence.

## Scope

Use the existing standalone Godot C# viewer. Current work previews native creatures and animation
clips, read-only GameObjects, ADT objects/buildings, native liquids and WMO-only interiors.
Selected creatures move in durable local drafts; graveyards (game_graveyard/graveyard_zone) are
edited in their own local drafts; creature interactions (gossip, quest links, vendor, trainer,
spell click, npcflag) are previewed in a client-style window and edited in a local interaction
draft. The quest editor is a read-only skeleton. The Scene explorer controls layers and SQL visibility.
Prioritize client visual fidelity and practical rendering performance. Keep unsupported effects
explicit; do not substitute fabricated animation. Defer source writes/SQL export, GameObject editing,
new spawns, script/quest editing and shell comparisons unless the user requests them.

## Tools and references

- AzerothCore server source: `X:\AzerothCore\azerothcore-wotlk` (official git clone). Read and cite it
  for server behavior: movement generators, spawn/phase/event/pool loading, coordinate/heading
  handling and `data/sql/base/db_world` schemas. Prefer it over guessing. It is read-only reference:
  never edit, build, run or pull it.
- MySQL MCP (`mcp_server_mysql`): read-only world-database inspection for checking real rows and
  schemas. SELECT only; never write rows, change schemas or privileges, and never echo credentials.
- Godot MCP: `run_project`/`get_debug_output`/`stop_project` give live runtime and log checks. It has
  no input injection or screenshots, so it cannot replace the graphical smoke checks; record what it
  could not verify.
- MCP servers are configured outside the repo and may be unavailable; say so rather than assuming
  results.

## Engineering and data

- Keep records and server coordinates independent of Godot nodes. Reuse the coordinate boundary.
- ADT/WDT placement facing differs from creature headings. Keep the conversion explicit and check
  projected WMO geometry against MODF bounds. Do not change creature heading mapping to fix objects.
- Share native mesh/material/texture resources, batch repeated models, decode on workers and bound
  main-thread uploads. Validate actual animation data and graphical output; record measured limits.
- Use asynchronous parameterized queries, bounded results, cancellation and stale-result checks.
  Apply SQL visibility/search before LIMIT. Default to phase 1/normal and scheduled event preview,
  excluding helpers, pools, SQL conditions and initially despawned objects. Never remove spatial,
  row or native-instance budgets in broad preview. Missing required metadata must fail closed.
  Bindings alone do not prove invisibility: include script-bound spawns by default and explain that
  SQL cannot evaluate C++/SmartAI runtime, live pools or player state. Hidden layers must stop work.
  Create Godot resources on the main thread. Unloading proxies never changes source data.
- Read credentials only from external user configuration; never copy them into repo, logs or UI.
  Show safe errors without connection strings, passwords or full provider exceptions.
- Current database access is SELECT-only inside read-only transactions. Do not change privileges,
  schemas or rows, apply SQL, modify server configuration or launch/build the server.
- Client MPQs and wow-data junction remain read-only; verify its target before asset work.
  Never recursively delete/move through the junction.
- Preserve user changes. No reset/clean/amend/push without instruction.
- Keep Godot .NET 4.7.1/.NET 8 and pinned SDK; respect UTF-8 and existing conventions.
- Prefer focused changes and reuse over speculative frameworks.
- Follow .editorconfig: UTF-8/LF, four-space C# indentation, explicit control-flow braces and
  one statement per line. Keep dependency versions and source formats stable during cleanup.
- Document binary layouts, coordinate conventions, resource ownership, worker/main-thread
  boundaries and failure handling. Explain intent/invariants rather than narrating obvious syntax.
- Keep opt-in smoke workflows in Scripts/Diagnostics and viewer UI in its partial source file.
  Do not restore retired Phase 0 shells, plugin trials, duplicate draft/export code or research gates.
- Do not commit credentials, client assets, caches, build outputs or raw operational logs.

## Progress and saving work

Maintain only PROGRESS.md for active tracking: completed work, validation, limits, blockers and next
steps. Update it at meaningful milestones and before handoff. Commit coherent completed milestones
regularly; stage explicit paths, inspect diffs and validate first. Record prior commit IDs in the
next checkpoint; don't create recursive commit-only updates.

Build/check what changed. For creature overlays, verify real read-only queries, viewport/list
selection, region/map changes, cancellation/stale results, draft/gizmo history and terrain streaming.
For native models, run model corpus checks and a graphical visual smoke including interiors. Headless or
synthetic checks do not prove in-game visual parity. For lifecycle changes, run --reload-smoke to
reload archives during a partial GPU upload. Check formatting with dotnet format whitespace
ACWorldEdit.csproj --no-restore --verify-no-changes. Documentation changes need link/diff checks.
For visibility/GameObjects, run the read-only --spawns and native --gameobjects checks plus graphical
--visibility-smoke: real model/list selection, layers, event overrides, masks and broad-preview caps.
