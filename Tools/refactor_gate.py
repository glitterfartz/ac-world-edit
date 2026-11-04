"""The viewer refactor gate.

Usage:
    python Tools/refactor_gate.py              # T0 + T1 + golden checks (the refactor gate)
    python Tools/refactor_gate.py --fast       # T0 + T1, no client data needed
    python Tools/refactor_gate.py --structural # T0 only, under one second
    python Tools/refactor_gate.py --ui-goldens # rendered editor dumps, needs a database
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
from dataclasses import dataclass
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import goldens

TOOLS = goldens.ROOT / "Tools"


@dataclass(frozen=True)
class Layer:
    """One source tree with its own size and long-method budgets."""

    name: str
    root: Path
    budget: Path
    long_methods: Path
    shell: str | None = None


LAYERS = [
    Layer("Scripts/Viewer", goldens.ROOT / "Scripts" / "Viewer", TOOLS / "viewer-budget.json", TOOLS / "viewer-long-methods.json", "ViewerShell.cs"),
    Layer("Scripts/Ui", goldens.ROOT / "Scripts" / "Ui", TOOLS / "ui-budget.json", TOOLS / "ui-long-methods.json"),
]
VIEWER = LAYERS[0].root
GODOT_FREE = ["Foundation", "Scripts/Assets", "Scripts/Data"]
MAX_METHOD = 60
CONTROL = {"if", "for", "foreach", "while", "switch", "catch", "using", "lock", "fixed", "when", "try", "else", "do"}
NON_METHOD = re.compile(r"\b(class|record|struct|interface|enum|delegate)\b")


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
    # Both are documented Godot halves of partials whose pure half is in Assets.
    allowed = {"AreaTable.cs", "ClientLightingView.cs"}
    problems = []
    for path in csharp_files(VIEWER):
        text = code_only(path.read_text(encoding="utf-8", errors="replace"))
        if "namespace ACWorldEdit.Assets" in text and path.name not in allowed:
            problems.append(f"{path.relative_to(goldens.ROOT)} declares ACWorldEdit.Assets")
    return problems


def code_lines(path: Path) -> int:
    return sum(1 for line in path.read_text(encoding="utf-8", errors="replace").splitlines() if line.strip())


def check_budgets() -> list[str]:
    problems = []
    for layer in LAYERS:
        if not layer.budget.exists():
            problems.append(f"missing {layer.budget.relative_to(goldens.ROOT)}")
            continue
        budget = json.loads(layer.budget.read_text(encoding="utf-8"))
        counts = []
        for path in csharp_files(layer.root):
            count = code_lines(path)
            counts.append(count)
            if count > budget["file"]:
                problems.append(f"{path.relative_to(goldens.ROOT).as_posix()}: {count} lines exceeds the {budget['file']} file budget")
        if layer.shell:
            shell = layer.root / layer.shell
            if not shell.exists():
                shell = layer.root / "TerrainViewer.cs"
            if shell.exists() and code_lines(shell) > budget["shell"]:
                problems.append(f"{shell.name}: {code_lines(shell)} lines exceeds the {budget['shell']} shell budget")
        total = sum(counts)
        if total > budget["folder"]:
            problems.append(f"{layer.name}: {total} lines exceeds the {budget['folder']} folder budget")
        print(f"{layer.name}: {total} lines, {len(counts)} files")
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
        # A primary constructor reads like a call; class/record/etc. declarations are not methods.
        if NON_METHOD.search(line):
            continue
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


def check_long_methods() -> tuple[list[str], dict[str, dict]]:
    problems = []
    results = {}
    for layer in LAYERS:
        allowed = set()
        if layer.long_methods.exists():
            allowed = set(json.loads(layer.long_methods.read_text(encoding="utf-8"))["allowed"])
        seen = set()
        for path in csharp_files(layer.root):
            for name, length in long_methods(path):
                key = f"{path.relative_to(goldens.ROOT).as_posix()}::{name}"
                seen.add(key)
                if key not in allowed:
                    problems.append(f"{key} is {length} lines (limit {MAX_METHOD})")
        for stale in sorted(allowed - seen):
            problems.append(f"{stale} is allowlisted but no longer exceeds {MAX_METHOD} lines; remove it")
        results[layer.name] = {"allowed": sorted(seen)}
    return problems, results


def structural(update: bool = False) -> int:
    problems = []
    problems += check_godot_free()
    problems += check_viewer_namespace()
    problems += check_budgets()
    method_problems, methods = check_long_methods()
    problems += method_problems
    if update:
        for layer in LAYERS:
            layer.long_methods.write_text(json.dumps(methods[layer.name], indent=2) + "\n", encoding="utf-8", newline="\n")
            print(f"wrote {layer.long_methods.relative_to(goldens.ROOT)}")
            counts = [code_lines(p) for p in csharp_files(layer.root)]
            budget = {"file": max(counts) if counts else 0, "folder": sum(counts)}
            if layer.shell:
                shell = layer.root / layer.shell
                if not shell.exists():
                    shell = layer.root / "TerrainViewer.cs"
                budget = {"file": budget["file"], "shell": code_lines(shell), "folder": budget["folder"]}
            layer.budget.write_text(json.dumps(budget, indent=2) + "\n", encoding="utf-8", newline="\n")
            print(f"wrote {layer.budget.relative_to(goldens.ROOT)}: {budget}")
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


def ui_goldens_check() -> int:
    python = [sys.executable]
    steps = [
        ["Tools/ui_golden.py", "--target", "behavior:295", "--profile", "wrath"],
        ["Tools/ui_golden.py", "--target", "creature:15192,gossip", "--profile", "wrath"],
        ["Tools/ui_golden.py", "--target", "quest:1,overview", "--profile", "wrath"],
    ]
    for step in steps:
        if run(python + step) != 0:
            return 1
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fast", action="store_true", help="structural + build + tests only")
    parser.add_argument("--structural", action="store_true", help="T0 only")
    parser.add_argument("--ui-goldens", action="store_true", help="rendered editor dumps (needs a database)")
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
    if args.ui_goldens:
        return ui_goldens_check()
    if fast() != 0:
        return 1
    if args.fast:
        return 0
    return goldens_check()


if __name__ == "__main__":
    raise SystemExit(main())
