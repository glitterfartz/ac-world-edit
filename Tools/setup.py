#!/usr/bin/env python3
"""Sync the pinned third-party submodules and build the native libraries ACWorldEdit loads.

  python Tools/setup.py                 # sync submodules, build CascLib (needed by the Retail profile)
  python Tools/setup.py --with-tests    # also build StormLib (only the StormLib differential test uses it)
  python Tools/setup.py --check         # report what is present, change nothing
  python Tools/setup.py --force         # rebuild even when the stamp says the output is current

Submodules (all pinned by their commit in this repository):
  Data/WoWDBDefs        retail table layouts, copied to the build output as definitions/
  ThirdParty/CascLib    native CASC reader -> native/win-x64/CascLib.dll
  ThirdParty/StormLib   MPQ reference implementation for tests -> native/win-x64/StormLib.dll

Builds happen out of tree under .build-env/cmake/ (a short temp folder when the checkout path is
long) so the submodule working trees stay clean.
NuGet packages are restored by dotnet and are not handled here. Nothing is written to a WoW install.
"""
import argparse
import hashlib
import os
import platform
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
NATIVE = ROOT / 'native' / 'win-x64'
BUILD = ROOT / '.build-env' / 'cmake'
# MSVC's file tracker cannot write its .tlog files past the 260-character path limit, which a deep
# checkout path reaches inside CMake's own folders. Longer build roots move to the temp folder.
MAX_BUILD_ROOT = 80

# name -> (submodule path, CMake options, built DLL name, installed DLL name)
LIBRARIES = {
    'CascLib': ('ThirdParty/CascLib',
                ['-DCASC_BUILD_SHARED_LIB=ON', '-DCASC_BUILD_STATIC_LIB=OFF', '-DCASC_BUILD_TESTS=OFF', '-DCASC_UNICODE=OFF',
                '-DCMAKE_DISABLE_FIND_PACKAGE_ZLIB=ON'],  # always use the bundled zlib; a system one would add a zlib1.dll import
                'CascLib.dll', 'CascLib.dll'),
    'StormLib': ('ThirdParty/StormLib',
                 ['-DBUILD_SHARED_LIBS=ON', '-DSTORM_BUILD_TESTS=OFF', '-DSTORM_SKIP_INSTALL=ON', '-DSTORM_UNICODE=ON',  # the test oracle passes LPWStr paths
                  '-DSTORM_USE_BUNDLED_LIBRARIES=ON', '-DCMAKE_DISABLE_FIND_PACKAGE_ZLIB=ON', '-DCMAKE_DISABLE_FIND_PACKAGE_BZip2=ON'],
                 'StormLib.dll', 'StormLib.dll'),
}
# Common to every build: static MSVC runtime so the DLLs need no VC++ redistributable, and the CMake 4
# compatibility switch (both projects declare a cmake_minimum_required older than CMake 4 accepts).
COMMON_OPTIONS = ['-DCMAKE_POLICY_VERSION_MINIMUM=3.5', '-DCMAKE_POLICY_DEFAULT_CMP0091=NEW',
                  '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded']


def build_root():
    """ACWORLD_CMAKE_BUILD_DIR, else .build-env/cmake, else a short folder under the temp directory."""
    override = os.environ.get('ACWORLD_CMAKE_BUILD_DIR')
    if override:
        return Path(override)
    if len(str(BUILD)) <= MAX_BUILD_ROOT:
        return BUILD
    short = Path(tempfile.gettempdir()) / 'acw-cmake' / hashlib.sha1(str(ROOT).encode()).hexdigest()[:8]
    print(f'Checkout path is long; building under {short} (set ACWORLD_CMAKE_BUILD_DIR to choose).')
    return short


def run(command, cwd=ROOT, check=True):
    print('>', ' '.join(str(part) for part in command))
    result = subprocess.run(command, cwd=cwd)
    if check and result.returncode != 0:
        sys.exit(f'Command failed with exit code {result.returncode}.')
    return result.returncode


def capture(command, cwd=ROOT):
    try:
        return subprocess.run(command, cwd=cwd, capture_output=True, text=True).stdout.strip()
    except OSError:
        return ''


def submodule_commit(path):
    """The commit the repository pins for a submodule (the recorded gitlink in the index, not the working tree)."""
    parts = capture(['git', 'ls-files', '-s', path]).split()
    return parts[1] if len(parts) >= 2 else ''


def sync_submodules():
    run(['git', 'submodule', 'sync', '--recursive'])
    run(['git', 'submodule', 'update', '--init', '--recursive'])


def stamp_for(name, options):
    path, _, _, _ = LIBRARIES[name]
    payload = '|'.join([submodule_commit(path), *COMMON_OPTIONS, *options])
    return hashlib.sha256(payload.encode()).hexdigest()


def build_library(name, force):
    path, options, built_name, installed_name = LIBRARIES[name]
    source = ROOT / path
    if not (source / 'CMakeLists.txt').is_file():
        sys.exit(f'{path} is empty; run git submodule update --init.')
    installed = NATIVE / installed_name
    stamp_file = NATIVE / f'{name}.stamp'
    stamp = stamp_for(name, options)
    if not force and installed.is_file() and stamp_file.is_file() and stamp_file.read_text().strip() == stamp:
        print(f'{name}: up to date ({installed})')
        return

    build_dir = build_root() / name
    configure = ['cmake', '-S', str(source), '-B', str(build_dir), '-A', 'x64', *COMMON_OPTIONS, *options]
    generator = os.environ.get('ACWORLD_CMAKE_GENERATOR')
    if generator:
        configure = ['cmake', '-S', str(source), '-B', str(build_dir), '-G', generator, *COMMON_OPTIONS, *options]
    run(configure)
    run(['cmake', '--build', str(build_dir), '--config', 'Release'])

    candidates = sorted(build_dir.rglob(built_name), key=lambda p: (p.parent.name != 'Release', len(p.parts)))
    if not candidates:
        sys.exit(f'{name}: build finished but {built_name} was not produced under {build_dir}.')
    NATIVE.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(candidates[0], installed)
    license_file = next((source / n for n in ('LICENSE', 'LICENSE.md', 'LICENSE.txt') if (source / n).is_file()), None)
    if license_file:
        shutil.copyfile(license_file, NATIVE / f'{name}.LICENSE.txt')
    stamp_file.write_text(stamp + '\n')
    print(f'{name}: {installed}')
    print(f'  SHA-256 {hashlib.sha256(installed.read_bytes()).hexdigest().upper()}')


def tool_version(command):
    out = capture(command)
    return out.splitlines()[0] if out else None


def find_godot():
    return os.environ.get('GODOT_BIN') or shutil.which('godot')


def report(with_tests):
    """Print what is present and return the problems that stop the app from working."""
    problems = []

    def line(ok, label, detail):
        print(f"  [{'ok' if ok else '!!'}] {label}: {detail}")
        return ok

    print('Toolchain')
    dotnet = tool_version(['dotnet', '--version'])
    if not line(dotnet is not None, '.NET SDK', dotnet or 'not found (see global.json for the required version)'):
        problems.append('.NET SDK')
    godot = find_godot()
    if not line(bool(godot and Path(godot).exists()), 'Godot 4.7.1 .NET', godot or 'not found; set GODOT_BIN or put godot on PATH'):
        problems.append('Godot')
    line(shutil.which('git') is not None, 'git', shutil.which('git') or 'not found')

    print('Submodules')
    defs = ROOT / 'Data' / 'WoWDBDefs' / 'definitions' / 'Map.dbd'
    if not line(defs.is_file(), 'Data/WoWDBDefs', 'definitions present' if defs.is_file() else 'missing (Retail needs it)'):
        problems.append('WoWDBDefs')
    for name, (path, _, _, installed_name) in LIBRARIES.items():
        present = (ROOT / path / 'CMakeLists.txt').is_file()
        line(present, path, f'pinned {submodule_commit(path)[:12]}' if present else 'not initialised')

    print('Native libraries (native/win-x64)')
    casc = NATIVE / 'CascLib.dll'
    if not line(casc.is_file(), 'CascLib.dll', 'present' if casc.is_file() else 'missing (Retail profile unavailable)'):
        problems.append('CascLib')
    storm = NATIVE / 'StormLib.dll'
    line(storm.is_file() or not with_tests, 'StormLib.dll', 'present' if storm.is_file() else 'not built (only the StormLib test needs it)')

    print('Native build tools')
    cmake = tool_version(['cmake', '--version'])
    line(cmake is not None, 'cmake', cmake or 'not found (needed to build CascLib)')
    if platform.system() == 'Windows':
        vswhere = Path(os.environ.get('ProgramFiles(x86)', r'C:\Program Files (x86)')) / 'Microsoft Visual Studio' / 'Installer' / 'vswhere.exe'
        vs = capture([str(vswhere), '-latest', '-products', '*', '-requires', 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', '-property', 'installationPath']) if vswhere.is_file() else ''
        line(bool(vs), 'MSVC C++ build tools', vs or 'not found (install "Desktop development with C++")')
    return problems


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--with-tests', action='store_true', help='also build StormLib for the differential test')
    parser.add_argument('--check', action='store_true', help='report what is present and change nothing')
    parser.add_argument('--force', action='store_true', help='rebuild even when the stamp matches')
    args = parser.parse_args()

    if args.check:
        problems = report(args.with_tests)
        return 1 if problems else 0

    if platform.system() != 'Windows':
        print('Native builds are Windows x64 only for now; syncing submodules and checking tools.')
        sync_submodules()
        return 1 if report(args.with_tests) else 0

    sync_submodules()
    build_library('CascLib', args.force)
    if args.with_tests:
        build_library('StormLib', args.force)
    print()
    problems = report(args.with_tests)
    if problems:
        print('\nStill missing: ' + ', '.join(problems))
        return 1
    print('\nSetup complete. Run: python launch_viewer.py')
    return 0


sys.exit(main())
