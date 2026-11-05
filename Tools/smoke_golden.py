"""Compare a headless viewer smoke's stdout with a committed golden.

Usage:
    python Tools/smoke_golden.py                       # terrain smoke, wrath
    python Tools/smoke_golden.py --smoke client-smoke --profile retail
    python Tools/smoke_golden.py --update              # human only

The golden keeps only the smoke's own TAG_LINES plus EXIT <code>, so Godot warnings,
backtraces and driver-specific noise cannot make an unchanged run fail.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import goldens

SMOKES = {
    "terrain-smoke": ("--terrain-smoke", 240, False),
    "client-smoke": ("--client-smoke", 300, False),
    "spawn-authoring-smoke": ("--spawn-authoring-smoke", 420, True),
    "creature-smoke": ("--creature-smoke", 300, True),
    "gameobject-smoke": ("--gameobject-smoke", 300, True),
    "graveyard-smoke": ("--graveyard-smoke", 300, True),
    "waypoint-smoke": ("--waypoint-smoke", 300, True),
    "areatrigger-smoke": ("--areatrigger-smoke", 300, True),
    "gizmo-smoke": ("--gizmo-smoke", 420, True),
}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--smoke", choices=sorted(SMOKES), default="terrain-smoke")
    parser.add_argument("--profile", choices=["wrath", "retail"], default="wrath")
    parser.add_argument("--update", action="store_true", help="rewrite the golden instead of checking it")
    args = parser.parse_args()

    flag, timeout, overlay = SMOKES[args.smoke]
    code, output = goldens.run_viewer([flag], args.profile, timeout)
    text = goldens.normalize_overlay(output, code) if overlay else goldens.normalize_text(output, code)
    path = goldens.GOLDEN / f"{args.smoke}-{args.profile}.txt"
    ok = goldens.check_or_update(path, text, args.update)
    if args.update:
        goldens.write_manifest()
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
