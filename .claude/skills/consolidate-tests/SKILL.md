---
name: consolidate-tests
description: Consolidate one area of ACWorldEdit's xUnit tests, gdUnit tests and Godot smoke workflows - merge near-duplicate tests (including AC/TC pairs) into theories or shared fixtures, drop tests and smoke checks that assert nothing the others don't, trim bloated setup - while proving coverage did not drop.
argument-hint: "<test folder, class or smoke>"
disable-model-invocation: true
---

# Consolidate tests

Reduce redundancy and bloat in the tests without losing coverage. Scope: `$ARGUMENTS`. If it is empty, pick the test folder with the most near-duplicate tests.

Coverage means all three kinds of test: xUnit (`Tests/ACWorldEdit.Tests`), gdUnit4 (`Tests/ACWorldEdit.GdTests`) and every `--*-smoke` workflow in `Scripts/Diagnostics` (listed in `docs/architecture/viewer-runtime.md`), not only xUnit line counts. Production code is out of scope: change it only to expose something a merged test needs, when there is no other way, and say so in the report. Production-code cleanup is the `refactor` skill.

Read `.claude/skills/_shared/working-rules.md` first and follow it. Also read `docs/architecture/testing-and-tooling.md`, `docs/NEEDED-TESTS.md` and the smoke section of `docs/architecture/viewer-runtime.md`.

## Baseline

Record each result, in this order:

1. Default suite: pass/skip counts, and which tests skip and why.
2. If the env vars exist (`ACWORLD_DB_INFO`, `ACWORLD_TC_DB_INFO`, `ACWORLD_DATA_DIR`, `ACWORLD_RETAIL_DIR`), the suite with the matching runsettings so Db and Corpus tests run. Tests that skip are not covered; don't consolidate what you can't run.
3. Line and branch coverage for the production files the area's tests exercise. The project has no coverage package: add `coverlet.collector` to the test csproj **temporarily** (never commit it), run `dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --collect:"XPlat Code Coverage" --results-directory <scratchpad>`, and parse the Cobertura XML with a small Python script kept in the scratchpad. If the package can't be restored, fall back to an assertion-by-assertion inventory and say coverage was not measured.
4. gdUnit, when the area includes `Tests/ACWorldEdit.GdTests` or the Godot code it exercises: run it with `GODOT_BIN` and record counts per suite. Without `GODOT_BIN`, don't change gdUnit tests and say so.
5. Smokes, when the area includes a smoke or the code a smoke drives: run each affected smoke in every dialect it supports (`--client-smoke` covers Wrath335/AzerothCore and Retail/TrinityCore). Record the exit code, every marker line (`*_PASSED`, `*_OK`) with its slash-separated check list (e.g. `CLIENT_SHOT_VENDOR_EDIT_OK search/add/validation/stock/undo/redo/shared`), and the screenshots written to `artifacts/`. If a smoke can't run (missing client data or database), don't change it or the code only it covers.
6. Optionally, if `dotnet-coverage` is installed, try `dotnet-coverage collect -f cobertura -o <scratchpad>/smoke.xml -- <godot command>` for smoke line coverage. If the report is empty, drop it and rely on the marker inventory.

Then inventory the area: for each test, the production behavior it asserts (class/method, input class, expected outcome); for each smoke stage, the actions it drives and every check it makes (each `Fail`/error path and each token in its marker), cited as `file:line`. Duplicates assert the same behavior on the same code path. AC/TC pairs that differ only in dialect, table or column names are theory candidates, not duplicates to delete.

## What to do

- **Theories over copies.** Several `[Fact]`s varying only in inputs become one `[Theory]`. AC/TC twins become a theory over the dialect or a shared base/fixture, and both dialects still run.
- **Shared setup.** Repeated builders, fake `IDBSource`/`IClientLookup`, row construction and draft setup go into `Tests/ACWorldEdit.Tests/Support/`, replacing the old `Check` helpers when you touch them.
- **Delete only proven redundancy.** A test goes when another test asserts everything it does, or it asserts only a getter/setter or a constant. Never delete a regression test for a fixed bug, a `KnownBug` record, a Db or Corpus test, or a test listed in `docs/NEEDED-TESTS.md`.
- **Smokes.** Each stage keeps its distinct assertions. Before cutting a check another smoke seems to repeat, confirm the other smoke runs it in the same dialect. Merge stages only when they open the same page and repeat the same loading; keep every output marker the docs or tools rely on, or update both. Don't move smoke checks into xUnit unless the xUnit test really exercises the same code.
- **Names and layout.** Test classes mirror the layer folders. Dialect-specific test classes use the `Ac`/`Tc` prefix in the matching folder; move gdUnit `.cs.uid` companions with their scripts.
- **Bloat.** Remove commented-out tests, unused usings and helpers, repeated identical assertions, and sleeps or timeouts longer than the test needs.

## Coverage rule

After each step, all of these must hold against the baseline, or the step is undone:

- **xUnit:** the default suite passes and per-file line and branch coverage of the production files under test does not drop measurably. Test counts are expected to fall.
- **gdUnit:** if touched, it passes and every behavior it asserted is still asserted.
- **Smokes:** if touched, every affected smoke exits 0 in each baseline dialect, prints every baseline marker with the same check tokens (or more) and writes the same screenshots. A renamed or merged marker counts only if the docs and anything that greps for it change in the same commit.
- **Moved checks:** a check moved between kinds counts only if the new test drives the same production code path, shown by coverage of that file or a `file:line` trace. A test of a fake or a copy doesn't count.

Docs to keep in step: `docs/architecture/testing-and-tooling.md` (folder contents, categories, helper names), `docs/NEEDED-TESTS.md`, the viewer-runtime smoke section, `CLAUDE.md` if a command or marker changes, and `docs/tc-status.md` where it cites test names or counts.

Commit type: `test(scope): summary`; put removed-versus-kept reasoning in the body only when it isn't obvious.

Before the last commit, revert the temporary `coverlet.collector` reference and confirm `git status` shows no csproj change. If you can't tell whether two tests are redundant, keep both and list the pair.

## Report

Besides the shared report items: before/after counts (default and env-var suites, gdUnit), before/after coverage for the area's files, each merged or removed test or smoke check with the `file:line` where its assertion now lives, before/after smoke results per dialect, suites or smokes that couldn't run and were left alone, and pairs kept despite looking redundant.
