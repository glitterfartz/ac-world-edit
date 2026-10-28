---
name: audit
description: Read-only audit of the whole codebase (or named areas) written to artifacts/audit.md - module map, duplication, inconsistencies, dead code, test coverage and a risk-ranked list of fixes, each routed to the skill that makes it (refactor, consolidate-tests, sync-docs, optimize). Explores each area with parallel subagents, then combines and verifies their findings.
argument-hint: "[area ...]"
disable-model-invocation: true
---

# Audit

Writes `artifacts/audit.md` (git-ignored), a read-only audit of the repo. It is the survey step: it finds and ranks work, and the editing skills do it, one area per run (`/refactor`, `/consolidate-tests`, `/sync-docs`, `/optimize`; they read this file as a starting point). The only file this skill writes is `artifacts/audit.md`: change no code, tests, config or docs, even when a finding has an obvious fix.

Scope: `$ARGUMENTS`. If it names areas from the split below, audit only those (and skip subagents if one or two areas are small enough to read directly). Otherwise audit everything. The shared rules in `.claude/skills/_shared/working-rules.md` (Subagents, Boundaries) apply.

## 1. Ask before starting subagents

The user's global instructions say to ask before starting subagents. Before you start any, tell the user:

- why you're using subagents: parallel reads keep raw file contents out of the main context, which leaves room to combine and check the findings;
- which model this session is running on;
- the split below, with file and line counts (recount them; the code changes), and the model you recommend for each agent.

Recommend **Sonnet** for the explorers: the work is reading and citing, and Haiku is more likely to miss subtle duplication or report guesses as facts. Combining and checking the findings happens in the main session. Wait for a yes. If the user says no, do the audit inline, one area at a time, and say coverage per area will be thinner.

## 2. Split

Use one read-only agent (`Explore`, or `general-purpose` told not to edit) per area. Recount and rebalance if an area has grown far past the others:

| Agent | Area |
|---|---|
| 1 | `Foundation/` |
| 2 | `Scripts/Assets/` |
| 3 | `Scripts/Data/` (AzerothCore `Ac*` and TrinityCore `Tc*` sources side by side) |
| 4 | `Scripts/Viewer/` + `Scripts/Rendering/` |
| 5 | `Scripts/Overlays/` |
| 6 | `Scripts/Ui/` |
| 7 | `Scripts/Diagnostics/`, `Tools/`, `launch_viewer.py`, `.claude/skills/*/driver.py` |
| 8 | `Tests/` (both projects), the `*.runsettings` files, `NEEDED-TESTS.md` |

Skip `artifacts/`, `obj/`, `bin/`, `native/`, `.build-env/`, `Experiments/` and the `Data/WoWDBDefs` submodule. Never touch the `wow-data` or `azerothcore-wotlk` paths: a hook blocks any command that names them.

Start all the agents in one turn so they run in parallel.

## 3. Explorer prompt

Give each agent this prompt with its area filled in:

```
Read-only audit of <AREA> in ac-world-edit
(Godot 4.7 C# world editor for AzerothCore and TrinityCore; read CLAUDE.md first for the
architecture). Do not edit, create or delete any file. Do not run builds.

Report, citing path:line for every claim:
1. Module map: each file or subfolder's job, its entry points (public types,
   Godot node scripts, CLI flags), and which other areas it uses (namespaces,
   types) and which use it (grep the rest of Scripts/ and Foundation/).
2. Duplication: functions, helpers, types or patterns written more than
   once, inside this area or matching code you saw elsewhere. List every copy.
3. Inconsistencies: the same kind of job done different ways (error
   handling, logging/status reporting, DB or file reads, state, config,
   null handling, async).
4. Dead or likely-unused code: types or members with no references (grep
   for the name across the repo, including Tests/ and .tscn files, before
   saying so), commented-out code, abstractions with only one implementation
   or one caller, TODO/FIXME/half-finished paths.
5. Tests: which test files cover this area, and the notable untested parts.

Mark anything you did not confirm by reading or grepping as "unverified".
Keep it to findings, no preamble. Up to about 1500 words.
```

For agent 8, replace item 5 with: map each test class to the production code it covers, list its category (unit, Corpus, Db, KnownBug) and the environment variables it needs, list the production areas with little or no coverage, and check `NEEDED-TESTS.md` against the tests that exist.

## 4. Combine and check

When all the agents are done:

1. **Check the claims that matter.** Before writing it into the audit, check every duplication, dead-code and cross-area claim with Grep or Read. A "no references" claim needs a repo-wide search for the name, including `Tests/`, `.tscn` and `.tres` files and reflection- or string-based uses such as smoke flag names and Godot signal names. If you can't confirm a claim, keep it marked "unverified" or drop it.
2. **Find what no single agent could see.** Look for duplication and inconsistencies across areas, for example the same parsing helper in `Scripts/Assets` and `Foundation`, the same status-reporting pattern done differently in `Overlays` and `Ui`, or `Ac*`/`Tc*` sources that differ in more than the schema.
3. **Build the dependency picture** from the agents' uses and used-by lists, and check it against the assembly split in `CLAUDE.md`: Foundation, Assets and Data must stay Godot-free.

## 5. Write artifacts/audit.md

Use this structure. Cite `path:line` (or just `path` for whole-file claims) on every claim:

```markdown
# Codebase audit

<date>, commit <short hash>, branch <branch>. Read-only. Claims marked
*unverified* were not confirmed against the code.

## 1. Module map
One subsection per area: purpose, entry points, depends on, used by.
End with a dependency diagram (Mermaid or text).

## 2. Duplication
A table per theme: what is duplicated | every instance (path:line) | notes.

## 3. Inconsistencies
Group by kind of task (error handling, status reporting, data access,
state, config, ...): the ways it is done, where, which one looks canonical.

## 4. Dead code and half-finished abstractions
Table: item | location | evidence (the search that found no uses) | confidence.

## 5. Test coverage
How to run each suite (commands from CLAUDE.md), environment variables,
a covered-vs-uncovered table by area, NEEDED-TESTS.md status.

## 6. Simplification opportunities
A numbered list, highest value-to-risk first. Each entry gives: the change,
the files involved, its risk (low/med/high) and why, the tests to have
in place before making it (existing ones, or what to write), and the skill
and argument that would make it (e.g. `/refactor Scripts/Data/Quests`,
`/consolidate-tests Tests/ACWorldEdit.Tests/Data`, `/sync-docs viewer`,
`/optimize creature overlay`).
```

Rate risk by what can break: **low** for local changes with existing tests, or dead code with no uses; **med** for changes across several files, or code covered only by Corpus/Db tests or smokes; **high** for changes to shared rendering/parsing paths, the draft/SQL export model, or anything with no test and only visual checks.

## 6. Report back

Give the user the path, a few lines on the top findings with the skill invocation for each, and how many claims are still unverified. Don't paste the whole audit into chat. Open work worth keeping beyond this run belongs in `docs/tc-status.md`; offer to add the top items there rather than adding them unasked.
