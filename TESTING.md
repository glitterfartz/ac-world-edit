# Testing

Two test projects live under `Tests/` (the game project excludes `Tests/**`, and `Tests/.gdignore` hides them from Godot):

| Project | Runs in | Covers |
|---|---|---|
| `Tests/ACWorldEdit.Tests` | plain .NET 8 / xUnit, no Godot | Foundation logic, drafts, SQL export, behavior-editor row edits, asset decoders, native asset corpus, read-only database tests |
| `Tests/ACWorldEdit.GdTests` | inside Godot 4.7.1 via GdUnit4 | Godot `Control`s and Godot-bound code (e.g. `ConnectionForm`, `UserSettings`) |

The graphical smokes in `Scripts/Diagnostics` (`-- --behavior-smoke`, `--content-smoke`, ...) are unchanged; see README.

## Commands

```powershell
# Default run: unit, draft, SQL export, behavior row edits. Excludes Corpus and KnownBug (default.runsettings).
dotnet test Tests/ACWorldEdit.Tests

# Slow native asset corpus (needs wow-data, or ACWORLD_DATA_DIR): models, liquids, WDT/ADT/BLP.
dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/corpus.runsettings

# Everything, including corpus (known-bug tests stay skipped, see below).
dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/all.runsettings

# Known-bug tests only, as listed (skipped), or actually executed so they fail:
dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/knownbugs.runsettings
$env:ACWE_RUN_KNOWN_BUGS = '1'; dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/knownbugs.runsettings

# One test or class (a --filter on top of a settings file replaces nothing; pass the matching settings file):
dotnet test Tests/ACWorldEdit.Tests --settings Tests/ACWorldEdit.Tests/all.runsettings --filter "FullyQualifiedName~SmartRuleEditsTests"

# GdUnit4 (Godot nodes). GODOT_BIN must point at the Godot 4.7.1 .NET executable; nothing is hardcoded.
$env:GODOT_BIN = 'X:\Godot-4.7\Godot_v4.7.1-stable_mono_win64.exe'
dotnet build Tests/ACWorldEdit.GdTests
dotnet test Tests/ACWorldEdit.GdTests --no-build --settings Tests/ACWorldEdit.GdTests/gdunit.runsettings

# Formatting (AGENTS.md): run per touched project, without --verify-no-changes to apply.
dotnet format whitespace ACWorldEdit.csproj --no-restore --verify-no-changes   # also Foundation, Tests/ACWorldEdit.Tests, and Tests/ACWorldEdit.GdTests (add --exclude Tests/ACWorldEdit.GdTests/gdunit4_testadapter_v5, a generated file)
```

A command-line `--filter` is combined (AND) with the settings file's filter, so pick the settings file for the category you want,
then narrow with `--filter`. Categories (xUnit traits): `Corpus`, `Db`, `KnownBug`.

## Optional inputs (tests skip with a message when absent, never fail)

- **Database**: `ACWORLD_DB_INFO=host;port;user;password;database`. Use a read-only MySQL user. `[DbFact]` tests skip with
  "ACWORLD_DB_INFO not set; skipping read-only database test" (or "... is invalid ...") otherwise. Nothing is committed;
  the variable is the same one the app honors.
- **Client data**: `wow-data` (nearest ancestor of the test binaries) or `ACWORLD_DATA_DIR`. `[ClientDataFact]` tests skip otherwise.
- **StormLib byte differential**: `./Tests/Run-StormLibChecks.ps1` downloads the pinned DLL, sets `STORMLIB_DLL` and runs
  `WorldCorpusTests`, comparing every asset read with StormLib. Passed after the port (6 tests, ~48 s).

## Database safety

The app and tests are SELECT-only inside read-only transactions (`WorldDatabase`). Database tests send queries through
`ReadOnlyGuard` (`session.ReadOnly(sql)`), which accepts one SELECT/SHOW/DESCRIBE statement and rejects writes, stacked
statements, `INTO`, locking reads, `WITH`/`EXPLAIN`. The guard itself is covered by pure text unit tests; no write is ever sent
to a live database to prove it. Applying exported SQL needs a disposable database and explicit authorization (AGENTS.md); no
test does that.

## Known bugs (skipped tests)

A found-not-fixed bug is a `[KnownBug("KB-nnn", "finding")]` test: skipped by default, `Category=KnownBug`, executed (and
failing) with `ACWE_RUN_KNOWN_BUGS=1`. When the bug is fixed, replace the attribute with `[Fact]`.

| ID | Test | Finding |
|---|---|---|
| KB-001 | `Smart/SmartCatalogTests.PercentParametersAreNotFollowedByLiteralPercent` | Percent parameters already render with `%`, so `SMART_EVENT_HEALTH_PCT` (event 2) and `SMART_EVENT_MANA_PCT` (event 3) sentences show `30%%`. |
| KB-002 | `Smart/SmartBehaviorTests.TimerRangesShareOneUnit` | Timer ranges render each endpoint with its unit (`Every 8 s–12 s in combat`); the `SmartCatalog.Timer` doc comment promises `8–12 s`. Either the sentence or the comment should change. |
| KB-003 | `Assets/LightBandTests.TicksBeforeTheFirstKeyframeBelongToTheWrapSegment` | A tick earlier than a band's first keyframe returns the last colour instead of blending through the wrap segment, so the colour jumps at the first keyframe. Bands that start at tick 0 are unaffected. |

## Writing tests

- New logic that does not need Godot belongs in `Foundation/` so it can be tested here; windows should only render and commit.
  `Foundation/SmartRuleEdits.cs` is the model: it was extracted from `BehaviorEditorWindow.Rules.cs` with bodies unchanged.
- Bug fix: write the failing regression test first.
- Drafts and exports use `Path.GetTempPath()`; never repo paths.
- Use `[Fact]` for pure tests, `[DbFact]` / `[DbFact(NeedsClientData = true)]` for database tests, `[ClientDataFact]` / `[ClientDataTheory]`
  for corpus tests (add `[Trait("Category", "Corpus")]` on the class if slow).

## Status

Done:
- [x] xUnit infrastructure, skip attributes, ReadOnlyGuard, KnownBug mechanism
- [x] Legacy AssetChecks harness ported (mapping in `Tests/PARITY.md`); draft/SQL export/placement/geometry/asset-decoder checks
- [x] Corpus: world (WDT/ADT/BLP), models, liquids
- [x] Database: spawns, game objects, area triggers, graveyards, interactions, content (against a live read-only DB)
- [x] SqlExport script text: empty diff, identifier quoting, FLOAT tolerance, text/NULL guards, symmetry, escaping (`SqlExportScriptTests`; no bugs found)
- [x] DatabaseSettings parse/validate/describe, ReadOnlyGuard
- [x] StormLib byte differential run once after the port
- [x] Behavior editor rule logic extracted to Foundation and tested (`SmartRuleEdits`)
- [x] SmartCatalog invariants; KB-001
- [x] GdUnit4 on Godot 4.7.1 (ConnectionForm, UserSettings env overrides)
- [x] DraftHistory, PendingChanges (creature, graveyard and row net-change collapse, discard, exported set)
- [x] WorldCoordinates round trips, tile/heading, ray hits; GroundProbe edge cases
- [x] QuestModel.Check and SmartCatalog.Check loader rules; SmartCatalog sentences, chains, timers
- [x] SpawnPreview filters; InteractionRows / NpcInteraction models and InteractionRules
- [x] Extracted and tested: `ContentDrafts`, `InteractionEdits`, `PoseText`, `SettingsResolver`, `AreaLookup`, `LightBand`
- [x] GdUnit4: PendingChangesWindow, PickerPopup

Not extracted (nothing static or pure to move without changing behavior; revisit if these windows are refactored):
- `QuestEditorWindow*`: only static lookup tables (`XpTiers`, `RacePresets`); validation already lives in `QuestModel`.
- `InteractionWindow*`: remaining code builds controls and commits through the window's own state.
- `CreatureOverlay.*` query/selection: depends on nodes, async region queries and the camera. Only pose text was extracted.
- Still no GdUnit4 coverage of the quest, interaction and behavior editor windows.
