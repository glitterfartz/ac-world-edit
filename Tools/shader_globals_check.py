#!/usr/bin/env python3
"""Check that every `global uniform` a shader declares exists in project.godot.

Godot compiles a shader in the editor only when every global uniform it names is declared under
[shader_globals]; a missing one fails at save/import time. The runtime library registers missing
globals itself (ClientShaderGlobals.Ensure), but the editor project still needs the declarations.

Exit code 0 when they match, 1 when a shader global is missing from project settings (or a declared
one is unused, which is harmless but worth knowing), 2 on a read error.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SHADERS = ROOT / "Shaders"
PROJECT = ROOT / "project.godot"

GLOBAL = re.compile(r"^\s*global\s+uniform\s+(?:lowp\s+|mediump\s+|highp\s+)?[A-Za-z0-9_]+\s+([A-Za-z0-9_]+)", re.M)


def shader_globals():
    names = set()
    for path in SHADERS.rglob("*.gdshader*"):
        names.update(GLOBAL.findall(path.read_text(encoding="utf-8", errors="replace")))
    return names


def declared_globals():
    text = PROJECT.read_text(encoding="utf-8")
    match = re.search(r"^\[shader_globals\]\s*$", text, re.M)
    if not match:
        return set()
    names = set()
    for line in text[match.end():].splitlines():
        line = line.strip()
        if line.startswith("["):
            break
        if "=" in line:
            names.add(line.split("=", 1)[0].strip())
    return names


def main():
    try:
        used = shader_globals()
        declared = declared_globals()
    except OSError as error:
        print(f"shader_globals_check: {error}", file=sys.stderr)
        return 2

    missing = sorted(used - declared)
    unused = sorted(declared - used)
    for name in missing:
        print(f"shader_globals_check: {name} is used by a shader but not declared in project.godot")
    for name in unused:
        print(f"shader_globals_check: note: {name} is declared but no shader uses it")

    if missing:
        return 1
    print(f"shader_globals_check: {len(used)} shader globals all declared")
    return 0


if __name__ == "__main__":
    sys.exit(main())
