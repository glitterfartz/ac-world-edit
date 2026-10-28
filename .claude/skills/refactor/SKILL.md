---
name: refactor
description: Behavior-preserving refactor of one ACWorldEdit feature area, or of recent work - removes redundancy, shares code between the AzerothCore and TrinityCore implementations, extracts interfaces where two dialects or call sites need one, retires legacy forms and fixes Ac/Tc naming and layout. Commits each step.
argument-hint: "<feature area> | recent [commits]"
disable-model-invocation: true
---

# Refactor

Simplify the production code in one area without changing behavior. Scope: `$ARGUMENTS`. If it is empty, pick the area with the most AC/TC duplication from `docs/tc-status.md` ("Coexisting forms"). If it says `recent` (or names commits), limit yourself to the code changed in the working tree or those commits (`git diff`, `git show`) and the code those changes duplicate.

Test code is out of scope except to keep it compiling and covering what it covered; test-suite cleanup is the `consolidate-tests` skill.

Read `.claude/skills/_shared/working-rules.md` first and follow it. Also read `docs/DB-DIFFERENCES.md`.

Favor readable, explicit code over compact code. The goal is code that is easier to read, debug and extend, not fewer lines.

## Baseline

1. Map the area: list the `Ac*` and `Tc*` classes, shared descriptors and interfaces, and their callers. Grep for references before judging anything unused or duplicated; cite `file:line`.
2. Run the default suite and record pass/skip counts. If it already fails, stop and report.

## What to look for

- **Dialect duplication.** An `Ac*` and a `Tc*` class that differ only in table or column names, or in a few rules, become one shared class driven by a descriptor (`IWorldSchema`, `TableSchema`, `*Spec`) or by a small interface with the two implementations. Differences that affect the editors go in `docs/DB-DIFFERENCES.md`.
- **Missing interfaces.** Code that branches on dialect (`if dialect == ...`, type tests, parallel switch statements) calls an interface method instead. The interface lives in the common directory, unprefixed.
- **Legacy forms.** Uses of the forms listed under "Coexisting forms" (`InteractionTables`, `QuestModel`, `NpcFlags`, and whatever the page lists now). Moving a caller to the target form is in scope when the area reaches it. Never add a new use of a legacy form.
- **Naming and layout.** Dialect-specific classes carry the `Ac`/`Tc` prefix, including the file name, and live in `AzerothCore/`/`TrinityCore/` under their feature or layer. Shared interfaces, descriptors, context and renderers stay unprefixed in the common directory. Rename and move with the `.cs.uid` companion (`git mv` both).
- **Dead and redundant code.** Unused members, one-implementation abstractions with no second caller, copy-pasted helpers. Remove only after grepping the whole repo, including `Tests/`, `.tscn`/`.tres` files and the smoke code in `Scripts/Diagnostics` (smoke flags and signal names are matched by string).
- **File count.** Merge tiny single-purpose classes into their closest related file, fold one-use helpers into their only caller, and delete empty wrappers. Never merge `Ac*`/`Tc*` files into one another. Don't build files so large they are hard to navigate. Delete the `.cs.uid` of every removed file and fix scene or resource references.
- **Merging.** Two classes with one responsibility become one; one class doing two jobs splits only if that removes a dependency or a dialect branch.
- **Clarity inside a method.** Flatten deep nesting with early returns and guard clauses. Replace nested ternaries with `switch` expressions or `if`/`else` chains. Rename variables and members whose names don't say what they hold. Drop comments that restate the code; keep ones that explain *why* (format quirks, client-version differences, schema oddities) and match the comment density of the surrounding file.

## Keep the balance

Leave code alone when the result would be:

- harder to read than the duplication, for example a merge that needs flags, mode parameters or dialect branches;
- clever rather than clear: dense one-liners, long LINQ chains that hide a simple loop, nested ternaries;
- one method or class doing several jobs that were separate;
- missing an abstraction that organizes the code (a descriptor, an interface with two real implementations, a named helper used for readability);
- harder to debug, such as removing intermediate variables a reader or breakpoint needs.

## Safety rule

Behavior stays the same: no new features, no changed SQL output, no changed UI text, no changed draft or settings file formats. If a change would alter behavior, stop and ask. Don't construct `Ac*Source`/`Tc*Source` outside `Scripts/Data`. Suite counts must match the baseline after each step; tests are updated with the code they cover and never lose a dialect.

Commit type: `refactor(scope): summary`.

## Report

Besides the shared report items: before/after suite counts, classes renamed, merged or removed, and duplication left in place with the reason. Skip local variable renames and formatting.
