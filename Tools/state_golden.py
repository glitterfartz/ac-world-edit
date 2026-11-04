"""Compare a deterministic viewer state or UI dump with a committed golden.

Usage:
    python Tools/state_golden.py --kind state --profile wrath
    python Tools/state_golden.py --kind ui --profile retail
    python Tools/state_golden.py --update            # human only

State dumps pin terrain sets, lighting decisions, ground probes, picks and exported SQL text.
UI dumps pin the built control tree: every menu item, button, field and option, in creation order.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import goldens

MARKERS = {
    "state": ("STATE_DUMP_BEGIN", "STATE_DUMP_END", "--state-dump"),
    "ui": ("UI_DUMP_BEGIN", "UI_DUMP_END", "--ui-dump"),
}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--kind", choices=sorted(MARKERS), default="state")
    parser.add_argument("--profile", choices=["wrath", "retail"], default="wrath")
    parser.add_argument("--update", action="store_true", help="rewrite the golden instead of checking it")
    args = parser.parse_args()

    begin, end, flag = MARKERS[args.kind]
    code, output = goldens.run_viewer([flag], args.profile, 600)
    if code != 0:
        print(output[-4000:], file=sys.stderr)
        print(f"viewer exited {code}", file=sys.stderr)
        return 1
    text = goldens.canonical_json(goldens.extract_json(output, begin, end))
    path = goldens.GOLDEN / f"{args.kind}-{args.profile}.json"
    ok = goldens.check_or_update(path, text, args.update)
    if args.update:
        goldens.write_manifest()
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
