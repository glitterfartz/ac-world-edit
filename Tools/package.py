#!/usr/bin/env python3
"""Export a Windows x64 release build and check that it is complete.

  python Tools/package.py                  # build/win-x64/ACWorldEdit.exe (+ data folder)
  python Tools/package.py --pack-only      # export only the .pck; needs no export templates
  python Tools/package.py --version 0.1.0  # stamp the exe's file/product version (the preset is restored afterwards)

Run python Tools/setup.py first. Needs Godot 4.7.1 .NET with its export templates installed
(Editor > Manage Export Templates; in CI, the matching templates archive). Output goes to the
git-ignored build/ folder.

Godot's .NET export needs ACWorldEdit.sln at the project root, and it reports a missing solution
or a failed publish as a warning with exit code 0, leaving an .exe with no game assembly. This
script therefore checks the data folder (data_ACWorldEdit_windows_x86_64) afterwards. The csproj
copies CascLib.dll, definitions/ and the licences into that folder through publish.

Do not run it from a shell with APPDATA redirected (launch_viewer.py does that for its own
subprocesses): Godot would look for the export templates under the redirected folder.
"""
import argparse
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PRESET = 'Windows Desktop'
OUT = ROOT / 'build' / 'win-x64'
DATA = OUT / 'data_ACWorldEdit_windows_x86_64'
# Files the exported game needs next to its assembly, as the csproj publishes them.
REQUIRED = ['ACWorldEdit.dll', 'ACWorldEdit.Foundation.dll', 'GodotSharp.dll', 'CascLib.dll',
            'definitions/Map.dbd', 'LICENSE', 'THIRD-PARTY.md']


def find_godot():
    """The console build when it sits beside GODOT_BIN, so export errors are printed."""
    found = os.environ.get('GODOT_BIN') or shutil.which('godot')
    if not found:
        sys.exit('Godot not found: set GODOT_BIN or put godot on PATH.')
    console = Path(found).with_name(Path(found).stem.removesuffix('_console') + '_console.exe')
    return str(console) if console.is_file() else found


PRESETS = ROOT / 'export_presets.cfg'


def stamp_version(version):
    """Put the version into the preset's Windows file/product version and enable resource editing.

    Returns the original text so the caller can restore the committed preset. file_version needs
    four numbers, so 1.2.3-rc1 becomes 1.2.3.0 and anything non-numeric becomes 0.0.0.0.
    """
    if not re.fullmatch(r'[0-9A-Za-z][0-9A-Za-z.\-]*', version):
        sys.exit(f'Invalid version "{version}": use letters, digits, dots and hyphens.')
    numbers = re.match(r'(\d+)\.(\d+)\.(\d+)', version)
    file_version = '.'.join(numbers.groups()) + '.0' if numbers else '0.0.0.0'
    original = PRESETS.read_text(encoding='utf-8')
    # A killed run cannot restore the preset; stamping over its leftovers would make them the "original".
    if re.search(r'^application/product_version="[^"]', original, flags=re.MULTILINE):
        sys.exit('export_presets.cfg already has a product version (left by an interrupted run?). '
                 'Restore it with: git checkout export_presets.cfg')
    text = original
    for key, value in (('application/file_version', file_version), ('application/product_version', version),
                       ('application/modify_resources', 'true')):
        literal = value if value == 'true' else f'"{value}"'
        text, count = re.subn(rf'^{re.escape(key)}=.*$', f'{key}={literal}', text, flags=re.MULTILINE)
        if count != 1:
            sys.exit(f'export_presets.cfg has no single "{key}" line to stamp.')
    PRESETS.write_text(text, encoding='utf-8', newline='\n')
    return original


def run(command, env=None):
    print('>', ' '.join(str(part) for part in command))
    code = subprocess.call(command, cwd=ROOT, env=env)
    if code != 0:
        sys.exit(f'Command failed with exit code {code}.')


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--pack-only', action='store_true', help='export only ACWorldEdit.pck (no export templates needed)')
    parser.add_argument('--version', default='', help='stamp this version (for example 0.1.0 or 0.1.0-rc1) into the exe')
    args = parser.parse_args()

    godot = find_godot()
    if not (ROOT / 'ACWorldEdit.sln').is_file():
        sys.exit('ACWorldEdit.sln is missing; Godot cannot export the C# project without it.')
    if not args.pack_only and not (ROOT / 'native' / 'win-x64' / 'CascLib.dll').is_file():
        sys.exit('native/win-x64/CascLib.dll is missing. Run python Tools/setup.py first.')
    if not args.pack_only and not (ROOT / 'Data' / 'WoWDBDefs' / 'definitions' / 'Map.dbd').is_file():
        sys.exit('Data/WoWDBDefs is not initialised. Run python Tools/setup.py first.')

    # MSBuild turns environment variables into properties, so a stray VERSION (for example from a CI
    # step) would become $(Version) and break the NuGet restore without a message. Keep it out.
    env = {key: value for key, value in os.environ.items() if key.upper() != 'VERSION'}
    shutil.rmtree(OUT, ignore_errors=True)
    OUT.mkdir(parents=True)  # Godot refuses to export into a folder that does not exist
    # The binary log records NuGet and MSBuild errors that the console summary can hide.
    run(['dotnet', 'build', 'ACWorldEdit.csproj', '-c', 'Release', '-p:NuGetAudit=false', f'-bl:{ROOT / "build" / "dotnet.binlog"}'], env)
    # .godot/ is git-ignored, so a fresh checkout has no imported resources until this runs.
    run([godot, '--headless', '--path', str(ROOT), '--import'], env)
    if args.pack_only:
        run([godot, '--headless', '--path', str(ROOT), '--export-pack', PRESET, str(OUT / 'ACWorldEdit.pck')], env)
        return 0
    original = stamp_version(args.version) if args.version else None
    try:
        run([godot, '--headless', '--path', str(ROOT), '--export-release', PRESET, str(OUT / 'ACWorldEdit.exe')], env)
    finally:
        if original is not None:
            PRESETS.write_text(original, encoding='utf-8', newline='\n')

    missing = [name for name in REQUIRED if not (DATA / name).is_file()]
    if missing:
        sys.exit(f'Export finished but {DATA} lacks: {", ".join(missing)}. Read the Godot output above.')
    for name in ('LICENSE', 'THIRD-PARTY.md'):
        shutil.copyfile(ROOT / name, OUT / name)
    print(f'Build ready: {OUT / "ACWorldEdit.exe"}')
    return 0


sys.exit(main())
