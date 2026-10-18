#!/usr/bin/env python3
"""Build the viewer and launch it in Godot (or run the corpus tests / terrain smoke)."""
import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def find_godot():
    """GODOT_BIN, else `godot` on PATH."""
    found = os.environ.get('GODOT_BIN') or shutil.which('godot')
    if not found:
        sys.exit('Godot not found: pass --godot-path, set GODOT_BIN, or put godot on PATH.')
    return found


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot-path', default='')
    parser.add_argument('--check-assets', action='store_true', help='run the corpus test set')
    parser.add_argument('--smoke', action='store_true', help='build, then run Godot headless with --terrain-smoke')
    args = parser.parse_args()

    os.chdir(ROOT)
    # Keep CLI caches inside the project, including in restricted development sessions.
    build_env = ROOT / '.build-env'
    env = os.environ.copy()
    env['APPDATA'] = str(build_env / 'roaming')
    env['LOCALAPPDATA'] = str(build_env / 'local')
    for key in ('APPDATA', 'LOCALAPPDATA'):
        Path(env[key]).mkdir(parents=True, exist_ok=True)
    env['MSBuildEnableWorkloadResolver'] = 'false'
    if not env.get('NUGET_PACKAGES'):
        env['NUGET_PACKAGES'] = str(Path.home() / '.nuget' / 'packages')

    if args.check_assets:
        return subprocess.call(['dotnet', 'test', 'Tests/ACWorldEdit.Tests', '--settings',
                                'Tests/ACWorldEdit.Tests/corpus.runsettings', '-p:NuGetAudit=false'], env=env)

    godot = args.godot_path or find_godot()
    code = subprocess.call(['dotnet', 'build', 'ACWorldEdit.csproj', '-p:NuGetAudit=false'], env=env)
    if code != 0:
        return code
    cmd = [godot, '--path', str(ROOT)]
    if args.smoke:
        cmd = [godot, '--headless', '--path', str(ROOT), '--', '--terrain-smoke']
    return subprocess.call(cmd, env=env)


sys.exit(main())
