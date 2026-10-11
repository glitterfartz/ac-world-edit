# Working on ACWorldEdit

Read PLAN.md and PROGRESS.md, inspect current code and Git status, then implement the next
requested milestone. Research was deliberately removed; do not recreate it or use old phase
documents as prerequisites. Current user instructions take precedence.

## Scope

Use the existing standalone Godot C# viewer. Current work previews native creatures and animation
clips, ADT objects/buildings and WMO-only interiors. Selected creatures move in durable local drafts.
Prioritize client visual fidelity and practical rendering performance. Keep unsupported effects
explicit; do not substitute fabricated animation. Defer source writes/SQL export, GameObjects,
new spawns, script/quest editing and shell comparisons unless the user requests them.

## Engineering and data

- Keep records and server coordinates independent of Godot nodes. Reuse the coordinate boundary.
- ADT/WDT placement facing differs from creature headings. Keep the conversion explicit and check
  projected WMO geometry against MODF bounds. Do not change creature heading mapping to fix objects.
- Share native mesh/material/texture resources, batch repeated models, decode on workers and bound
  main-thread uploads. Validate actual animation data and graphical output; record measured limits.
- Use asynchronous parameterized queries, bounded results, cancellation and stale-result checks.
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
- Do not commit credentials, client assets, caches, build outputs or raw operational logs.

## Progress and saving work

Maintain only PROGRESS.md for active tracking: completed work, validation, limits, blockers and next
steps. Update it at meaningful milestones and before handoff. Commit coherent completed milestones
regularly; stage explicit paths, inspect diffs and validate first. Record prior commit IDs in the
next checkpoint; don't create recursive commit-only updates.

Build/check what changed. For creature overlays, verify real read-only queries, viewport/list
selection, region/map changes, cancellation/stale results, draft/gizmo history and terrain streaming.
For native models, run model corpus checks and a graphical visual smoke including interiors. Headless or
synthetic checks do not prove in-game visual parity. Documentation changes need link/diff checks
