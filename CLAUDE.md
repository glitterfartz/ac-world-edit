@AGENTS.md

# Standing rules (moved from PROGRESS.md)

- PROGRESS.md is current state only: update or remove entries as things change instead of appending. Keep it
  under ~50 lines; completed-task history belongs in git.
- Verification gaps matter: the Godot MCP cannot inject input or take screenshots, so record anything only
  checkable by eye as "not verified" rather than claiming visual parity.
- If click-driven graphical smokes fail at their first synthetic click, check the desktop session (other window,
  locked/inactive display) before suspecting code. Use timeouts and rebuild C# first.
- Light.dbc position convention (1/36-yard scale, tile-grid-corner origin), the WMO interior MOCV 2x factor and
  the sun-curve are assumptions or approximations; do not present them as client-verified.
- Exported SQL is guarded (WHERE on expected values, preflight/post-check); never weaken the guards, and never
  run it against a database without explicit authorization.
- New record IDs (quests, action lists, menus) are claims until export; reserve from max(database, draft)+1.
- Testing (see TESTING.md): every feature or behavior change ships with tests in Tests/ACWorldEdit.Tests (Godot
  nodes: Tests/ACWorldEdit.GdTests). A bug fix starts with a failing regression test. Run the full suite
  (`dotnet test Tests/ACWorldEdit.Tests`, plus the corpus and GdUnit4 runs when their areas changed) before calling
  work done, and say what was skipped. Keep TESTING.md current. Put new logic that can be Godot-free in Foundation
  so it is testable. Tests never write to a database; DB tests use ReadOnlyGuard.
