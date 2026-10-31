#!/usr/bin/env python3
"""Export a Windows x64 release build and make sure its native and data dependencies are present.

  python Tools/package.py                  # build/win-x64/ACWorldEdit.exe (+ data folder)
  python Tools/package.py --pack-only      # export only the .pck; needs no export templates

Run python Tools/setup.py first. After the export, CascLib.dll, the retail table definitions and the licences are copied into the
build's data_ACWorldEdit_windows_x86_64 folder, which is where the loader looks (AppContext.BaseDirectory).
A full export has not been run yet (it needs the export templates); --pack-only has.
Needs Godot 4.7.1 .NET with its export templates installed (Editor > Manage Export Templates; in CI,
the matching templates archive). Output goes to the git-ignored build/ folder.
"""
import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PRESET = 'Windows Desktop'
OUT = ROOT / 'build' / 'win-x64'
DATA = OUT / 'data_ACWorldEdit_windows_x86_64'


def find_godot():
    found = os.environ.get('GODOT_BIN') or shutil.which('godot')
    if not found:
        sys.exit('Godot not found: set GODOT_BIN or put godot on PATH.')
    return found


def run(command, env=None):
    print('>', ' '.join(str(part) for part in command))
    code = subprocess.call(command, cwd=ROOT, env=env)
    if code != 0:
        sys.exit(f'Command failed with exit code {code}.')


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--pack-only', action='store_true', help='export only ACWorldEdit.pck (no export templates needed)')
    args = parser.parse_args()

    godot = find_godot()
    casclib = ROOT / 'native' / 'win-x64' / 'CascLib.dll'
    definitions = ROOT / 'Data' / 'WoWDBDefs' / 'definitions'
    if not args.pack_only and not casclib.is_file():
        sys.exit('native/win-x64/CascLib.dll is missing. Run python Tools/setup.py first.')
    if not args.pack_only and not (definitions / 'Map.dbd').is_file():
        sys.exit('Data/WoWDBDefs is not initialised. Run python Tools/setup.py first.')

    env = os.environ.copy()
    env['MSBuildEnableWorkloadResolver'] = 'false'
    OUT.mkdir(parents=True, exist_ok=True)
    run(['dotnet', 'build', 'ACWorldEdit.csproj', '-c', 'Release', '-p:NuGetAudit=false'], env)
    # .godot/ is git-ignored, so a fresh checkout has no imported resources until this runs.
    run([godot, '--headless', '--path', str(ROOT), '--import'], env)
    if args.pack_only:
        run([godot, '--headless', '--path', str(ROOT), '--export-pack', PRESET, str(OUT / 'ACWorldEdit.pck')], env)
        return 0
    run([godot, '--headless', '--path', str(ROOT), '--export-release', PRESET, str(OUT / 'ACWorldEdit.exe')], env)

    DATA.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(casclib, DATA / 'CascLib.dll')
    for name in ('CascLib.LICENSE.txt',):
        source = ROOT / 'native' / 'win-x64' / name
        if source.is_file():
            shutil.copyfile(source, DATA / name)
    shutil.copytree(definitions, DATA / 'definitions', dirs_exist_ok=True)
    shutil.copyfile(ROOT / 'Data' / 'WoWDBDefs' / 'LICENSE.md', DATA / 'definitions' / 'LICENSE.md')
    for name in ('LICENSE', 'THIRD-PARTY.md'):
        shutil.copyfile(ROOT / name, OUT / name)
    print(f'Build ready: {OUT / "ACWorldEdit.exe"}')
    return 0


sys.exit(main())
