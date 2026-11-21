# Tests

Two projects. The xUnit suite is the fast default; the gdUnit4 suite runs inside Godot and covers UI flows.

- `ACWorldEdit.Tests` — xUnit, `net8.0`. References `src/ACWorldEdit.ClientData`, `src/ACWorldEdit.Core` and `Foundation`, and compiles `Scripts/Data/**` directly; folders mirror the layers (`Assets/`, `Data/`, `Drafts/`, `Sql/`, `Smart/`, `Db/`, `Corpus/`, `Support/`).
- `ACWorldEdit.GdTests` — gdUnit4, references `ACWorldEdit.csproj`, needs `GODOT_BIN`. Every suite that touches Godot natives carries `[RequireGodotRuntime]`; without it the host can crash instead of failing the test.

## Runs

| What | Command (from the repo root) |
|---|---|
| default suite | `dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false` |
| one class | `dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --filter "FullyQualifiedName~SplitTerrainTests"` |
| corpus | `dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --settings Tests/ACWorldEdit.Tests/corpus.runsettings` |
| known bugs | `dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --settings Tests/ACWorldEdit.Tests/knownbugs.runsettings` |
| everything | `dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false --settings Tests/ACWorldEdit.Tests/all.runsettings` |
| UI (Godot) | `dotnet test Tests/ACWorldEdit.GdTests -c Debug -p:NuGetAudit=false` with `GODOT_BIN` set |

`default.runsettings` is applied by the csproj and excludes `Corpus` and `KnownBug`. A command-line `--filter` is ANDed with it, so `--filter "Category=Corpus"` matches nothing — pass the matching `--settings` file as well.

## Skip attributes (`Support/Attributes.cs`)

| Attribute | Needs |
|---|---|
| `ClientDataFact`, `ClientDataTheory` | `ACWORLD_DATA_DIR` (3.3.5) or `ACWORLD_RETAIL_DIR` (retail) |
| `DbFact` | `ACWORLD_DB_INFO` (`host;port;user;password;database`) |
| `TcDbFact` | `ACWORLD_TC_DB_INFO` (same, plus optional `;hotfixes`) |
| `DbWriteFact` | the above, plus CREATE on the connection's user |
| `KnownBug` | `ACWE_RUN_KNOWN_BUGS=1` |

A skipped `Db` test proves nothing: run the suite against a real database before calling database work done. Corpus and live-database tests stay opt-in, but they must be green against their dependency when the feature is finished.

## Conventions

- One new file per feature for live-database and UI tests; never append new cases to an unrelated suite.
- UI tests are interaction tests (open the window, click, read the fields), not just construction checks.
- `Tests/golden/*` are pinned by `Tests/golden/.sha256`. `--update` on `Tools/smoke_golden.py`, `Tools/state_golden.py` or `Tools/ui_golden.py` is the only way to rewrite one, and it rewrites the manifest too.
- `Tests/run_stormlib_checks.py` needs `python Tools/setup.py --with-tests` (builds StormLib); it reads `--data-path` (default `ACWORLD_DATA_DIR`) and diffs every asset read against StormLib.
