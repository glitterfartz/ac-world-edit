"""Compare a rendered editor target dump with a committed golden.

Usage:
    python Tools/ui_golden.py --target behavior:295 --profile wrath
    python Tools/ui_golden.py --target creature:15192,gossip --profile wrath
    python Tools/ui_golden.py --target behavior:295 --update   # human only

The target dump opens one editor, waits for its rows to load, and records the built control tree.
It needs a world database (``ACWORLD_DB_INFO`` or ``ACWORLD_TC_DB_INFO``) and client data, so it is
an opt-in gate step beside the smokes, never part of ``--fast``.
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import goldens

BEGIN, END = "TARGET_DUMP_BEGIN", "TARGET_DUMP_END"
# Godot numbers auto-named nodes from a process-wide counter; a lazily opened window starts at a
# different number each run. The type is the contract, the counter is not.
AUTO = re.compile(r"@[A-Za-z0-9_]+@\d+")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target", required=True, help="behavior:entry[,guid], creature:entry[,section] or quest:id[,tab]")
    parser.add_argument("--profile", choices=["wrath", "retail"], default="wrath")
    parser.add_argument("--update", action="store_true", help="rewrite the golden instead of checking it")
    args = parser.parse_args()

    code, output = goldens.run_viewer([f"--dump-target={args.target}"], args.profile, 600)
    if code != 0:
        print(output[-4000:], file=sys.stderr)
        print(f"viewer exited {code}", file=sys.stderr)
        return 1

    payload = AUTO.sub("@<auto>", goldens.extract_json(output, BEGIN, END))
    text = goldens.canonical_json(payload)
    name = args.target.replace(":", "-").replace(",", "-")
    path = goldens.GOLDEN / f"ui-{name}-{args.profile}.json"
    ok = goldens.check_or_update(path, text, args.update)
    if args.update:
        goldens.write_manifest()
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
