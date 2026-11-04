# ACWorldEdit documentation

Design, reference and status pages for ACWorldEdit. The root [README](../README.md) is the user-facing
introduction; this folder is for people changing the code.

## Page map

| Page | Covers | Update it when |
|---|---|---|
| [architecture/overview.md](architecture/overview.md) | Layers, dependency rules, threading, repository layout, glossary | A layer, folder or thread model changes |
| [architecture/client-data.md](architecture/client-data.md) | `Scripts/Assets`: MPQ/CASC, formats, terrain, models, retail tables, navigation readers | A parser, format or client-side catalog changes |
| [architecture/viewer-runtime.md](architecture/viewer-runtime.md) | `Scripts/Viewer`, `Rendering`, `Overlays`, `Ui`, `Diagnostics`, smoke flags | A runtime system, window or smoke workflow changes |
| [architecture/rendering.md](architecture/rendering.md) | The retail shader port: progress, per-material notes, inferred inputs | A shader, material route or lighting pass changes |
| [architecture/editing-and-export.md](architecture/editing-and-export.md) | `Scripts/Data` and `Foundation`: drafts, rules, SQL export | A table read, draft type, rule or export statement changes |
| [architecture/testing-and-tooling.md](architecture/testing-and-tooling.md) | Test projects and commands, environment variables, tooling, CI, commit rules | A test project, tool, setting or workflow changes |
| [reference/database-differences.md](reference/database-differences.md) | AzerothCore vs TrinityCore schema differences that affect the editors | A table or column difference is found |
| [reference/shader-inspection-locations.md](reference/shader-inspection-locations.md) | Map/tile/XYZ locations for inspecting each implemented liquid shader | A material is ported or a better location is found |
| [status/open-work.md](status/open-work.md) | What is built, what is partial and what is open, per area and per plan phase | Work lands, a gap is found, or a phase starts |
| [status/performance.md](status/performance.md) | How to measure, the current frame budget and open performance work | A measurement changes the baseline or a fix lands |
| [status/performance-history.md](status/performance-history.md) | Older measured runs and tried-and-reverted experiments | A new experiment is measured or reverted |
| [status/needed-tests.md](status/needed-tests.md) | Tests deferred until their feature works | A test is deferred or written |
| [plans/editor-gap-plan.md](plans/editor-gap-plan.md) | The design of editor phases 10 onward, with per-phase status | A phase is designed, started or finished |
| [plans/sniff-import-proposal.md](plans/sniff-import-proposal.md) | Research proposal for importing packet sniffs (not a plan of record) | Only when explicitly asked to work on it |

`img/` holds the screenshots the root README embeds; a `.gdignore` keeps Godot from importing them.

## Reading paths

- **Change an asset parser or a client table**: `architecture/client-data.md`, then the matching
  `status/open-work.md` rows.
- **Change the viewer, an overlay or an editor window**: `architecture/viewer-runtime.md` and
  `architecture/editing-and-export.md`.
- **Change a shader or lighting**: `architecture/rendering.md` and
  `reference/shader-inspection-locations.md`.
- **Add or change a database read, rule or export**: `architecture/editing-and-export.md`, plus
  `reference/database-differences.md` for the two dialects.
- **Start the next editor phase**: `plans/editor-gap-plan.md` (design + status) and
  `status/open-work.md` (the detailed gaps).
- **Commit**: `architecture/testing-and-tooling.md` has the commands, the environment variables and
  the commit message rules.

## Conventions

Four kinds of page, kept separate so a reader knows what they are holding:

- **Architecture** pages describe how the code works now. No history, no plans; if something is
  unfinished, say so in one sentence and link the status or plan page.
- **Reference** pages are lookup tables (schema differences, inspection coordinates). Keep them
  factual and dated when the facts can drift.
- **Status** pages say what is done, partial or missing, with the test or smoke that proves it.
  Finished work is described in the architecture pages, not here.
- **Plans** pages are design documents. Each phase has a `Status` line and the page links to
  `status/open-work.md` for the live gap list. A plan is not an instruction to implement it; the
  sniff proposal is research only.

One page owns each fact. When the same fact appears in two places, move it to its owner and link from
the other. Code names in backticks must match the code: rename them in the same change as the code
(see the rules in [`CLAUDE.md`](../CLAUDE.md)).

Links are relative and point at the new filenames; don't reintroduce the old `ARCHITECTURE.md`,
`tc-status.md`, `DB-DIFFERENCES.md`, `NEEDED-TESTS.md`, `gap-plan.md`, `sniff-proposal.md`,
`shaders.md` or `SKILLS.md` names.

## Agent tooling

The only project skill is `run-ac-world-edit` (`.claude/skills/run-ac-world-edit/SKILL.md`): it
builds and runs the viewer through `driver.py`, runs smoke workflows and takes screenshots. It is
invoked automatically when a task matches, or by name.
