#!/usr/bin/env python3
"""Build the viewer and launch it in Godot (or run the corpus tests / terrain smoke)."""
import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def shortcut_target(lnk):
    """Resolve a Windows .lnk through WScript.Shell; empty string when unavailable."""
    try:
        script = f"(New-Object -ComObject WScript.Shell).CreateShortcut('{lnk}').TargetPath"
        out = subprocess.run(['powershell', '-NoProfile', '-Command', script], capture_output=True, text=True)
        return out.stdout.strip()
    except OSError:
        return ''


def find_godot():
    if os.environ.get('GODOT_BIN'):
        return os.environ['GODOT_BIN']
    lnk = ROOT / 'Godot 4.7.lnk'
    if lnk.is_file():
        target = shortcut_target(lnk)
        if target and Path(target).is_file():
            return target
    local = Path(r'X:\Godot-4.7\Godot_v4.7.1-stable_mono_win64.exe')
    if local.is_file():
        return str(local)
    found = shutil.which('godot')
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
    env.setdefault('ProgramFiles', r'C:\Program Files')
    env.setdefault('ProgramFiles(x86)', r'C:\Program Files (x86)')
    env.setdefault('ProgramData', r'C:\ProgramData')
    env.setdefault('ALLUSERSPROFILE', env['ProgramData'])
    env['MSBuildEnableWorkloadResolver'] = 'false'
    if not env.get('NUGET_PACKAGES'):
        env['NUGET_PACKAGES'] = str(Path(env.get('USERPROFILE', Path.home())) / '.nuget' / 'packages')

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
