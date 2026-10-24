#!/usr/bin/env python3
"""Run a viewer smoke workflow and print only its result lines.

  driver.py shot [X,Y] [--water]   windowed; saves artifacts/client-shot.png
  driver.py client                 headless; checks the client profile opens
  driver.py <name>                 any other --<name>-smoke (terrain, water, creature, ...)

Needs GODOT_BIN and ACWORLD_DATA_DIR (profile defaults to wrath) in the environment.
"""
import os, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
KEEP = ('SMOKE', 'CLIENT_SHOT', 'ERROR', 'Unhandled', 'Exception')

def main():
    args = sys.argv[1:] or ['client']
    mode, rest = args[0], args[1:]
    godot = os.environ.get('GODOT_BIN') or sys.exit('set GODOT_BIN')
    godot = godot.replace('.exe', '_console.exe') if not godot.endswith('_console.exe') else godot
    env = os.environ.copy()
    env.setdefault('ACWORLD_CLIENT_PROFILE', 'wrath')
    cmd = [godot, '--path', str(ROOT)]
    if mode == 'shot':
        tile = next((a for a in rest if a[0].isdigit()), None)
        flags = [f'--client-shot-tile={tile}'] if tile else ['--client-shot']
        if '--water' in rest:
            flags.append('--client-shot-water')
        cmd += ['--', *flags]
        (ROOT / 'artifacts' / 'client-shot.png').unlink(missing_ok=True)
    elif mode == 'client':
        cmd += ['--headless', '--', '--client-smoke']
    else:
        cmd += ['--headless', '--', f'--{mode}-smoke']
    subprocess.run(['dotnet', 'build', 'ACWorldEdit.csproj', '-p:NuGetAudit=false', '-v:q', '-nologo'],
                   cwd=ROOT, env=env, check=True)
    proc = subprocess.run(cmd, cwd=ROOT, env=env, capture_output=True, text=True, timeout=300)
    for line in (proc.stdout + proc.stderr).splitlines():
        if any(k in line for k in KEEP) and not line.startswith('   '):
            print(line)
    if mode == 'shot':
        print('screenshot:', ROOT / 'artifacts' / 'client-shot.png')
    return proc.returncode

sys.exit(main())
