---
name: run-ac-world-edit
description: Build, run, start, test and screenshot the ACWorldEdit Godot viewer (render a WoW 3.3.5 map tile to a PNG, run headless smoke checks). Use when asked to run the viewer, take a screenshot, or confirm a rendering/loading change works in the real app.
---

# Run ACWorldEdit

Godot 4.7 .NET 3D viewer. Verified on Windows 11 (PowerShell/Git Bash), not Linux. Paths are relative to the repo root. The agent path is `driver.py`, which builds, runs one of the app's own `--*-smoke` workflows, and prints only the result lines.

## Prerequisites

- `GODOT_BIN` pointing at the Godot .NET exe (e.g. `X:\Godot-4.7\Godot_v4.7.1-stable_mono_win64.exe`); the driver switches to the `_console.exe` sibling so output is captured.
- `ACWORLD_DATA_DIR` = the 3.3.5 client `Data` folder (e.g. `X:/AzerothCore/OGClient/Data`).
- `ACWORLD_DB_INFO` (`host;port;user;password;database`) is optional for these flows; the smokes ran with it set (`database=True`).

## Run (agent path)

```bash
export ACWORLD_DATA_DIR="X:/AzerothCore/OGClient/Data"
python .claude/skills/run-ac-world-edit/driver.py client        # headless: client opens, first tile streams
python .claude/skills/run-ac-world-edit/driver.py shot 33,25    # windowed: saves artifacts/client-shot.png
python .claude/skills/run-ac-world-edit/driver.py shot          # same, default tile
python .claude/skills/run-ac-world-edit/driver.py terrain       # any --<name>-smoke: terrain, water, creature, ...
```

Success is a `*_SMOKE_OK` / `TERRAIN_SMOKE_PASSED` line and exit 0. `shot` opens a real window briefly (not headless: the screenshot is read from the viewport), hides the UI, frames the largest nearby building, then prints `screenshot: <path>`. Read the PNG to check it: non-black terrain, textured models. `shot X,Y` takes ADT tile coordinates; `--water` frames the largest liquid.

## Tests

```bash
dotnet test Tests/ACWorldEdit.Tests -p:NuGetAudit=false   # Db tests skip without ACWORLD_DB_INFO / ACWORLD_TC_DB_INFO
```

## Run (human path)

`python launch_viewer.py` builds and opens the full editor window; useless headless.

## Gotchas

- `launch_viewer.py` redirects `APPDATA`/`LOCALAPPDATA` into `.build-env/`, so Godot's saved settings (client folder, DB login) are not found and the viewer reports "Choose your client folder". The driver does not redirect, so the real saved settings apply; the env vars above override them.
- The map loaded varied between my runs (Northrend with plain `--client-shot`, Azeroth in the `client` and `shot 33,25` runs); the log line `map=...` says which.
- Noisy `WARNING: ... Multitexture M2 batch` / `WMO shader N uses base-texture preview` lines are normal; the driver filters them.
- Running the bash tool: a command mentioning the `wow-data` path is blocked by `.claude/hooks/block_readonly.py` even for read-only `ls`/`grep`.
- `--client-shot` fails with `CLIENT_SMOKE_NO_CLIENT` if no client folder resolves, after about 5 s.

## Troubleshooting

- `CLIENT_SMOKE_NO_CLIENT Choose your client folder` → export `ACWORLD_DATA_DIR`, or run without the launcher's APPDATA redirect.
- `CLIENT_SMOKE_TIMEOUT` (180 s) → the tile/model load stalled; the message shows `loaded=N/M modelsBusy=...`.
