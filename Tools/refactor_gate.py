"""The viewer refactor gate.

Usage:
    python Tools/refactor_gate.py              # T0 + T1 + golden checks (the refactor gate)
    python Tools/refactor_gate.py --fast       # T0 + T1, no client data needed
    python Tools/refactor_gate.py --structural # T0 only, under one second
    python Tools/refactor_gate.py --update-goldens   # human only: rewrite goldens + manifest

T0 is instant and encodes the architecture rules. T1 is build + both test projects. The full gate
adds the wrath/retail state, UI and smoke goldens.
"""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import goldens

VIEWER = goldens.ROOT / "Scripts" / "Viewer"
BUDGET = goldens.ROOT / "Tools" / "viewer-budget.json"
LONG = goldens.ROOT / "Tools" / "viewer-long-methods.json"
GODOT_FREE = ["Foundation", "Scripts/Assets", "Scripts/Data"]
MAX_METHOD = 60
CONTROL = {"if", "for", "foreach", "while", "switch", "catch", "using", "lock", "fixed", "when", "try", "else", "do"}


def csharp_files(root: Path):
    return sorted(p for p in root.rglob("*.cs") if "obj" not in p.parts and "bin" not in p.parts and ".godot" not in p.parts)


def code_only(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.DOTALL)
    return "\n".join(line.split("//", 1)[0] for line in text.splitlines())


def type_name_references(path: Path) -> list[str]:
    text = code_only(path.read_text(encoding="utf-8", errors="replace"))
    hits = []
    if re.search(r"^\s*using\s+Godot\s*;", text, re.MULTILINE):
        hits.append("using Godot")
    if re.search(r"\bGodot\.", text):
        hits.append("Godot.")
    return hits


def check_godot_free() -> list[str]:
    problems = []
    for folder in GODOT_FREE:
        root = goldens.ROOT / folder
        if not root.exists():
            continue
        for path in csharp_files(root):
            for hit in type_name_references(path):
                problems.append(f"{path.relative_to(goldens.ROOT)} is not Godot-free ({hit})")
    return problems


def check_viewer_namespace() -> list[str]:
    # AreaTable is the documented Godot half of AreaLookup. ClientLighting is the last misfiled
    # file and is split in R1.2; the exception is removed in the same commit.
    allowed = {"AreaTable.cs", "ClientLighting.cs"}
    problems = []
    for path in csharp_files(VIEWER):
        text = code_only(path.read_text(encoding="utf-8", errors="replace"))
        if "namespace ACWorldEdit.Assets" in text and path.name not in allowed:
            problems.append(f"{path.relative_to(goldens.ROOT)} declares ACWorldEdit.Assets")
    return problems


def code_lines(path: Path) -> int:
    return sum(1 for line in path.read_text(encoding="utf-8", errors="replace").splitlines() if line.strip())


def check_budgets() -> list[str]:
    if not BUDGET.exists():
        return [f"missing {BUDGET.relative_to(goldens.ROOT)}"]
    budget = json.loads(BUDGET.read_text(encoding="utf-8"))
    problems = []
    total = 0
    for path in csharp_files(VIEWER):
        count = code_lines(path)
        total += count
        if count > budget["file"]:
            problems.append(f"{path.name}: {count} lines exceeds the {budget['file']} file budget")
    shell = VIEWER / "ViewerShell.cs"
    if not shell.exists():
        shell = VIEWER / "TerrainViewer.cs"
    if shell.exists() and code_lines(shell) > budget["shell"]:
        problems.append(f"{shell.name}: {code_lines(shell)} lines exceeds the {budget['shell']} shell budget")
    if total > budget["folder"]:
        problems.append(f"Scripts/Viewer: {total} lines exceeds the {budget['folder']} folder budget")
    print(f"Scripts/Viewer: {total} lines, {len(list(csharp_files(VIEWER)))} files")
    return problems


def strip_literals(line: str) -> str:
    line = re.sub(r'"(?:\\.|[^"\\])*"', '""', line)
    line = re.sub(r"'(?:\\.|[^'\\])*'", "''", line)
    return line.split("//", 1)[0]


SIGNATURE = re.compile(
    r"^\s{0,8}(?:(?:public|private|protected|internal|static|async|override|virtual|sealed|partial|unsafe|extern|new)\s+)*"
    r"(?:[A-Za-z_][\w<>\[\],\.\? ]*\s+)?([A-Za-z_]\w*)\s*\([^;]*\)\s*$"
)


def long_methods(path: Path) -> list[tuple[str, int]]:
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    found = []
    for index, line in enumerate(lines):
        match = SIGNATURE.match(line)
        if not match or match.group(1) in CONTROL:
            continue
        # The opening brace is on this line or on the next non-empty one.
        brace = None
        if line.rstrip().endswith("{"):
            brace = index
        else:
            for probe in range(index + 1, min(index + 3, len(lines))):
                if lines[probe].strip():
                    if lines[probe].strip() == "{":
                        brace = probe
                    break
        if brace is None:
            continue
        depth = 0
        end = None
        for scan in range(brace, len(lines)):
            text = strip_literals(lines[scan])
            depth += text.count("{") - text.count("}")
            if depth == 0 and scan > brace:
                end = scan
                break
        if end is None:
            continue
        length = sum(1 for probe in range(index, end + 1) if lines[probe].strip())
        if length > MAX_METHOD:
            found.append((match.group(1), length))
    return found


def check_long_methods() -> tuple[list[str], dict]:
    allowed = set()
    if LONG.exists():
        allowed = set(json.loads(LONG.read_text(encoding="utf-8"))["allowed"])
    problems = []
    seen = set()
    for path in csharp_files(VIEWER):
        for name, length in long_methods(path):
            key = f"{path.relative_to(goldens.ROOT).as_posix()}::{name}"
            seen.add(key)
            if key not in allowed:
                problems.append(f"{key} is {length} lines (limit {MAX_METHOD})")
    for stale in sorted(allowed - seen):
        problems.append(f"{stale} is allowlisted but no longer exceeds {MAX_METHOD} lines; remove it")
    return problems, {"allowed": sorted(seen)}


def structural(update: bool = False) -> int:
    problems = []
    problems += check_godot_free()
    problems += check_viewer_namespace()
    problems += check_budgets()
    method_problems, methods = check_long_methods()
    problems += method_problems
    if update:
        LONG.write_text(json.dumps(methods, indent=2) + "\n", encoding="utf-8", newline="\n")
        print(f"wrote {LONG.relative_to(goldens.ROOT)}")
        budget = json.loads(BUDGET.read_text(encoding="utf-8")) if BUDGET.exists() else {"file": 0, "shell": 0, "folder": 0}
        counts = [code_lines(p) for p in csharp_files(VIEWER)]
        shell = VIEWER / "ViewerShell.cs"
        if not shell.exists():
            shell = VIEWER / "TerrainViewer.cs"
        budget = {"file": max(counts), "shell": code_lines(shell), "folder": sum(counts)}
        BUDGET.write_text(json.dumps(budget, indent=2) + "\n", encoding="utf-8", newline="\n")
        print(f"wrote {BUDGET.relative_to(goldens.ROOT)}: {budget}")
        return 0
    if not goldens.verify_manifest():
        problems.append("Tests/golden/.sha256 does not match the committed goldens")
    for problem in problems:
        print(f"FAIL {problem}", file=sys.stderr)
    if problems:
        print(f"{len(problems)} structural problem(s)", file=sys.stderr)
        return 1
    print("structural checks passed")
    return 0


def run(command: list[str], timeout: int = 1800) -> int:
    print(">", " ".join(command), flush=True)
    result = subprocess.run(command, cwd=goldens.ROOT, timeout=timeout)
    return result.returncode


def fast() -> int:
    dotnet = ["dotnet"]
    if run(dotnet + ["build", "ACWorldEdit.csproj", "-p:NuGetAudit=false", "--nologo"]) != 0:
        return 1
    if run(dotnet + ["test", "Tests/ACWorldEdit.Tests", "-p:NuGetAudit=false", "--nologo"] + ["--logger", "console;verbosity=minimal"]) != 0:
        return 1
    if run(dotnet + ["test", "Tests/ACWorldEdit.GdTests", "-p:NuGetAudit=false", "--nologo"] + ["--logger", "console;verbosity=minimal"]) != 0:
        return 1
    return 0


def goldens_check() -> int:
    python = [sys.executable]
    steps = [
        ["Tools/smoke_golden.py", "--smoke", "terrain-smoke", "--profile", "wrath"],
        ["Tools/smoke_golden.py", "--smoke", "client-smoke", "--profile", "retail"],
        ["Tools/state_golden.py", "--kind", "state", "--profile", "wrath"],
        ["Tools/state_golden.py", "--kind", "state", "--profile", "retail"],
        ["Tools/state_golden.py", "--kind", "ui", "--profile", "wrath"],
        ["Tools/state_golden.py", "--kind", "ui", "--profile", "retail"],
    ]
    for step in steps:
        if run(python + step) != 0:
            return 1
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fast", action="store_true", help="structural + build + tests only")
    parser.add_argument("--structural", action="store_true", help="T0 only")
    parser.add_argument("--update-goldens", action="store_true", help="rewrite goldens, manifest and ratchets (human only)")
    args = parser.parse_args()

    if args.update_goldens:
        if structural(update=True) != 0:
            return 1
        python = [sys.executable]
        for step in (
            ["Tools/smoke_golden.py", "--smoke", "terrain-smoke", "--profile", "wrath", "--update"],
            ["Tools/smoke_golden.py", "--smoke", "client-smoke", "--profile", "retail", "--update"],
            ["Tools/state_golden.py", "--kind", "state", "--profile", "wrath", "--update"],
            ["Tools/state_golden.py", "--kind", "state", "--profile", "retail", "--update"],
            ["Tools/state_golden.py", "--kind", "ui", "--profile", "wrath", "--update"],
            ["Tools/state_golden.py", "--kind", "ui", "--profile", "retail", "--update"],
        ):
            if run(python + step) != 0:
                return 1
        goldens.write_manifest()
        return 0

    if not goldens.verify_manifest() and not args.structural and not args.fast:
        print("golden manifest mismatch; run --update-goldens deliberately", file=sys.stderr)
        return 1
    if structural() != 0:
        return 1
    if args.structural:
        return 0
    if fast() != 0:
        return 1
    if args.fast:
        return 0
    return goldens_check()


if __name__ == "__main__":
    raise SystemExit(main())
