# Agent guidance for ACWorldEdit

## Project and scope

ACWorldEdit is currently a C# Godot terrain viewer for the World of Warcraft 3.3.5a client.
The intended direction is an integrated AzerothCore world/content editor; most proposed editor features are not implemented.
Follow the user's current task and keep changes focused. Research recommendations are reference material, not authorization
to implement the entire roadmap. The original research-only scope does not prohibit implementation explicitly requested later.

## Required research reference

Research lives outside this repository at `X:\AzerothCore\research`.
Before architecture, content editing, database, client-format, scripting, or substantial rendering work:

1. Read [the research index](X:/AzerothCore/research/README.md) and the documents relevant to the task.
2. Use that research as the design and evidence reference; inspect current code/schema before relying on its findings.
3. Distinguish confirmed existing behavior from proposals and unresolved assumptions.
4. If new evidence changes a recommendation, explain the difference and update the relevant research document when in scope.

Key references:

- [Original research brief](X:/AzerothCore/research/RESEARCH.md).
- [Existing project assessment](X:/AzerothCore/research/02-existing-project-assessment.md).
- [AzerothCore architecture](X:/AzerothCore/research/03-azerothcore-architecture.md).
- [Client formats and assets](X:/AzerothCore/research/04-client-formats-and-assets.md).
- [Editor architecture](X:/AzerothCore/research/06-editor-architecture.md).
- [Database abstraction and safe exports](X:/AzerothCore/research/07-database-abstraction.md).
- [Scripting and IDE integration](X:/AzerothCore/research/08-scripting-and-ide.md).
- [Roadmap](X:/AzerothCore/research/11-development-roadmap.md),
  [MVP](X:/AzerothCore/research/12-mvp-specification.md), and
  [open questions](X:/AzerothCore/research/13-open-questions.md).
- [Source references](X:/AzerothCore/research/references/source-references.md) and
  [research notes](X:/AzerothCore/research/references/research-notes.md).

Keep research documents and supporting research artifacts in that external directory. Git commits in this repository do not
save changes to the external research directory; report external changes explicitly and preserve the files there.

## Repository map and toolchain

- `Scripts/TerrainViewer.cs`: current UI, camera, asset lifecycle, streaming scheduler and smoke checks.
- `Scripts/TerrainRenderer.cs`: Godot mesh/material/texture creation and release.
- `Scripts/TerrainMinimap.cs`: map availability and navigation display.
- `Scripts/Assets/`: engine-independent archive, terrain, map and texture readers.
- `Shaders/terrain.gdshader`: terrain layers, alpha blending, tint and shadows.
- `Tests/`: standalone asset checks; excluded from the main project's compilation.
- `Main.tscn`, `project.godot`: main scene and engine configuration.
- `Launch-Viewer.ps1`: build/run/check entry point; `README.md` documents current behavior.

The current project uses Godot .NET 4.7.1, .NET 8, SDK pinning in `global.json`, and SharpZipLib 1.4.2.
Confirm those files before changing dependencies; do not upgrade the engine/toolchain incidentally.
Respect `.editorconfig`, `.gitattributes` and nearby conventions. Use UTF-8 and avoid unrelated reformatting.

## Engineering practices

- Inspect before proposing or editing. Use `rg` for searches and make the smallest coherent change satisfying the task.
- Prefer reuse and incremental changes; introduce abstractions for an actual boundary rather than speculative future features.
- Keep persistent editor state, commands, validation and persistence adapters independent of Godot render nodes.
  Streaming/unloading must not discard selection identity, unsaved content or undo history.
- Keep Godot scene-tree/resource operations on the appropriate thread. Use immutable CPU results, cancellation/generation
  checks and bounded main-thread uploads for asynchronous asset work.
- Preserve format/build/locale/archive provenance. Unsupported data must produce actionable diagnostics rather than silent
  fallback or lossy rewriting. Rendering DTOs are not lossless client-file documents.
- Use one tested coordinate/rotation conversion boundary; preserve server coordinates in domain state.
- Distinguish server-content changes, client-asset changes, server code/build changes and derived geometry changes.
- Simulated script previews must identify approximations and unsupported actions; actual runtime fidelity requires the
  corresponding core/runtime. Do not promise arbitrary C++ simulation or deterministic replay without evidence.
- Keep diagnostics specific, retain unsupported fields, and update user documentation when observable behavior changes.

## Data and filesystem boundaries

- `wow-data` is a junction currently pointing to `X:\AzerothCore\OGClient\Data`. Verify its target before asset work.
  Do not write, delete or recursively clean through the junction.
- Client files and MPQs are read-only sources unless the user explicitly authorizes a particular modification.
  Future asset authoring should use project-owned overlays and staged patch outputs.
- AzerothCore reference source is at `X:\AzerothCore\azerothcore-wotlk`. Read that repository's `AGENTS.md` and relevant
  subsystem instructions before working there. Do not build or modify it as an incidental viewer task.
- Use the read-only test MySQL connector for schema/content research. Do not infer permission to connect to live databases
  or apply SQL. Editing workflows should stage changes and generate bounded, reviewable exports unless applying changes is
  explicitly part of the task.
- Preserve user changes. Do not reset, clean, overwrite or discard unrelated work. Resolve absolute target paths before
  destructive filesystem operations and account for junctions/reparse points.
- Never commit client assets, credentials, connection secrets, logs, generated caches or build outputs. Respect `.gitignore`.

## Validation

Run checks appropriate to the change; avoid expensive unrelated builds. Useful existing entry points from the repository root:

```powershell
./Launch-Viewer.ps1 -CheckAssets
./Launch-Viewer.ps1 -Smoke
./Launch-Viewer.ps1
```

`-CheckAssets` builds/runs parser checks. `-Smoke` builds/runs headless streaming checks. The normal launch is for visual review.
The launcher creates local build-environment files; smoke checks create artifacts, and graphical smoke can save a preview.
For strict read-only research, use an existing check binary only if suitable and disclose that it was not freshly built.

For parser changes, verify representative formats and relevant malformed inputs. For rendering/streaming changes, verify
navigation, map switching, cancellation/unloading and visual results. For future persistence changes, verify preservation,
bounded changes, conflicts and reversibility against fixtures or an explicitly authorized isolated test target.
Documentation-only changes need content/link/diff verification, not a project rebuild.
Report what passed, what failed, and any checks not run; do not call historical logs or unexecuted acceptance criteria new tests.

## Git and saving progress

- Check `git status --short` before editing and before each commit; distinguish existing user work from your changes.
- **Commit regularly to save progress:** make small, coherent commits after meaningful milestones and at the end of a completed
  task. For longer work, do not wait until the entire feature is finished to save completed, reviewable steps.
- Validate and review each milestone before committing. If an interrupted task needs a checkpoint, explicitly label it `WIP`
  and describe unfinished work; do not present a checkpoint as a passing finished feature.
- Stage explicit task-related paths and review the staged diff. Do not use blanket staging that captures unrelated user work.
- Use concise commit messages explaining the change. Do not amend, rewrite history or push unless the task authorizes it.
- If a commit fails because of identity, hooks or repository configuration, report the blocker and preserve the files.
  Do not invent an identity, bypass hooks or silently initialize/change repositories to force a commit.
- External research files require their own explicit version-control/backup workflow; do not create a research repository or
  relocate those files into this repository without instruction.

## Completion reporting

Summarize the result, relevant file paths, validation and commit IDs. Identify material limits or unresolved blockers.
Keep progress updates concise and continue authorized work without unnecessary confirmation.
