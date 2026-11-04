"""Shared helpers for the viewer golden tools.

Goldens are opt-in regression captures of the viewer's own machine-readable output. They are
never written by CI: ``--update`` is a human action, and ``Tests/golden/.sha256`` proves the
files did not change without one.
"""
from __future__ import annotations

import difflib
import hashlib
import json
import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
GOLDEN = ROOT / "Tests" / "golden"

ANSI = re.compile(r"\x1b\[[0-9;]*m")
TAG = re.compile(r"^[A-Z][A-Z0-9_]{2,}")
STAMP = re.compile(r"generated \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} UTC")
# The Scene explorer reports how many rows the world database returned near the camera. That is
# live data, not UI structure, and it changes when someone edits the server.
DB_COUNT = re.compile(r"\d+ SQL matches")
# Client/build messages carry a live status string and a non-ASCII separator; neither is a control.
MESSAGE = re.compile(r'message="[^"]*"')


def godot_binary() -> str:
    exe = os.environ.get("GODOT_BIN")
    if not exe:
        sys.exit("GODOT_BIN is not set; point it at the Godot 4.7 .NET executable.")
    console = Path(exe.replace(".exe", "_console.exe"))
    return str(console if console.exists() else Path(exe))


def run_viewer(args: list[str], profile: str | None, timeout: int = 600) -> tuple[int, str]:
    env = os.environ.copy()
    if profile:
        env["ACWORLD_CLIENT_PROFILE"] = profile
        variable = "ACWORLD_RETAIL_DIR" if profile == "retail" else "ACWORLD_DATA_DIR"
        if not env.get(variable):
            sys.exit(f"{variable} is not set; the {profile} golden needs client data.")
    command = [godot_binary(), "--headless", "--path", str(ROOT), "--", *args]
    print(">", " ".join(command), flush=True)
    try:
        result = subprocess.run(
            command,
            cwd=ROOT,
            env=env,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout,
        )
    except subprocess.TimeoutExpired:
        sys.exit(f"timed out after {timeout}s: {' '.join(args)}")
    return result.returncode, result.stdout + result.stderr


def normalize_text(output: str, code: int) -> str:
    """Keep only smoke tags and the exit code; drop warnings, backtraces and Godot noise."""
    lines = []
    for line in ANSI.sub("", output).splitlines():
        line = line.rstrip()
        if TAG.match(line) and not line.startswith(("ERROR:", "WARNING:", "SCRIPT ERROR:")):
            lines.append(MESSAGE.sub("message=<text>", line))
    lines.append(f"EXIT {code}")
    return "\n".join(lines) + "\n"


def extract_json(output: str, begin: str, end: str) -> str:
    lines = ANSI.sub("", output).splitlines()
    try:
        start = lines.index(begin)
        stop = lines.index(end, start + 1)
    except ValueError:
        sys.exit(f"output has no {begin} / {end} block; the run failed before it dumped")
    return "\n".join(lines[start + 1 : stop])


def canonical_json(payload: str) -> str:
    payload = STAMP.sub("generated <stamp>", payload)
    payload = DB_COUNT.sub("<n> SQL matches", payload)
    try:
        value = json.loads(payload)
    except json.JSONDecodeError as error:
        sys.exit(f"dump is not valid JSON: {error}")
    return json.dumps(value, indent=2, ensure_ascii=False) + "\n"


def check_or_update(path: Path, text: str, update: bool) -> bool:
    path.parent.mkdir(parents=True, exist_ok=True)
    if update:
        path.write_text(text, encoding="utf-8", newline="\n")
        print(f"wrote {path.relative_to(ROOT)}", flush=True)
        return True
    if not path.exists():
        print(f"missing golden {path.relative_to(ROOT)}; run with --update", file=sys.stderr)
        return False
    golden = path.read_text(encoding="utf-8")
    if golden == text:
        print(f"ok {path.relative_to(ROOT)}", flush=True)
        return True
    sys.stdout.writelines(
        difflib.unified_diff(
            golden.splitlines(True),
            text.splitlines(True),
            fromfile=str(path.relative_to(ROOT)),
            tofile="actual",
        )
    )
    return False


def write_manifest() -> None:
    """Hash every golden so a refactor cannot silently edit one to pass the gate."""
    rows = []
    for path in sorted(GOLDEN.glob("**/*")):
        if path.is_file() and path.name != ".sha256":
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            rows.append(f"{digest}  {path.relative_to(GOLDEN).as_posix()}")
    (GOLDEN / ".sha256").write_text("\n".join(rows) + "\n", encoding="utf-8", newline="\n")
    print(f"wrote {GOLDEN.relative_to(ROOT)}/.sha256")


def verify_manifest() -> bool:
    manifest = GOLDEN / ".sha256"
    if not manifest.exists():
        print("Tests/golden/.sha256 is missing; run Tools/refactor_gate.py --update-goldens", file=sys.stderr)
        return False
    expected = {}
    for line in manifest.read_text(encoding="utf-8").splitlines():
        if line.strip():
            digest, name = line.split("  ", 1)
            expected[name] = digest
    ok = True
    for name, digest in expected.items():
        path = GOLDEN / name
        if not path.exists():
            print(f"golden missing: {name}", file=sys.stderr)
            ok = False
        elif hashlib.sha256(path.read_bytes()).hexdigest() != digest:
            print(f"golden changed without --update: {name}", file=sys.stderr)
            ok = False
    for path in sorted(GOLDEN.glob("**/*")):
        if path.is_file() and path.name != ".sha256" and path.relative_to(GOLDEN).as_posix() not in expected:
            print(f"golden not in manifest: {path.relative_to(GOLDEN)}", file=sys.stderr)
            ok = False
    return ok
