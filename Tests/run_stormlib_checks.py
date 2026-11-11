#!/usr/bin/env python3
"""Run the WorldCorpusTests StormLib differential check against StormLib built from the pinned submodule.

Run `python Tools/setup.py --with-tests` first; it builds ThirdParty/StormLib (v9.40) to
native/win-x64/StormLib.dll or native/linux-x64/libStormLib.so.
"""
import argparse
import os
import platform
import subprocess
import sys
from pathlib import Path


def stormlib_path(root):
    """The library Tools/setup.py installs for this OS and architecture."""
    if platform.system() == 'Windows':
        return root / 'native' / 'win-x64' / 'StormLib.dll'
    folder = 'linux-arm64' if platform.machine().lower() in ('aarch64', 'arm64') else 'linux-x64'
    return root / 'native' / folder / 'libStormLib.so'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data-path', default=os.environ.get('ACWORLD_DATA_DIR', 'wow-data'))
    args = parser.parse_args()

    root = Path(__file__).resolve().parent.parent
    os.chdir(root)
    dll_path = stormlib_path(root)
    if not dll_path.is_file():
        sys.exit(f'{dll_path.relative_to(root)} is missing. Run python Tools/setup.py --with-tests first.')

    env = os.environ.copy()
    env['ACWORLD_DATA_DIR'] = str(Path(args.data_path).resolve(strict=True))
    env['STORMLIB_DLL'] = str(dll_path)
    code = subprocess.call(['dotnet', 'test', 'Tests/ACWorldEdit.Tests', '--settings',
                            'Tests/ACWorldEdit.Tests/corpus.runsettings', '-p:NuGetAudit=false',
                            '--filter', 'FullyQualifiedName~WorldCorpusTests'], env=env)
    if code != 0:
        sys.exit('StormLib differential check failed')


main()
