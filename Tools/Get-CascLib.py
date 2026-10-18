#!/usr/bin/env python3
"""Place CascLib.dll in native/win-x64 so the Retail client profile can read CASC storage.

The repo does not ship binaries. Build CascLib yourself (MIT, https://github.com/ladislav-zezula/CascLib,
commit 38a3466, cmake -DCASC_BUILD_SHARED_LIB=ON for x64) or reuse an existing build, then pass its path
here. The DLL is copied into the gitignored native/win-x64 folder; the game project copies it next to the
assembly on build. Only the Retail profile needs it. Nothing is written to the WoW install.
"""
import argparse
import hashlib
import shutil
import sys
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('source', help='path to a 64-bit CascLib.dll')
    args = parser.parse_args()

    source = Path(args.source)
    if not source.is_file():
        sys.exit(f"CascLib.dll not found at '{source}'.")
    destination_dir = Path(__file__).resolve().parent.parent / 'native' / 'win-x64'
    destination_dir.mkdir(parents=True, exist_ok=True)
    destination = destination_dir / 'CascLib.dll'
    shutil.copyfile(source, destination)
    print(f'Copied to {destination}')
    print(f'SHA-256 {hashlib.sha256(destination.read_bytes()).hexdigest().upper()}')


main()
