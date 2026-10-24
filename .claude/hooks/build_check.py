# Stop hook: build the Godot project once when the turn ends with uncommitted .cs changes.
# A failed build exits 2 so Claude sees the errors and fixes them; stop_hook_active prevents a loop.
import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def main():
    data = json.load(sys.stdin)
    if data.get('stop_hook_active'):
        return 0
    status = subprocess.run(['git', 'status', '--porcelain', '--', '*.cs'], cwd=ROOT, capture_output=True, text=True)
    if not status.stdout.strip():
        return 0
    env = os.environ.copy()
    env['MSBuildEnableWorkloadResolver'] = 'false'
    result = subprocess.run(['dotnet', 'build', 'ACWorldEdit.csproj', '-p:NuGetAudit=false', '-v:q', '-nologo', '-clp:ErrorsOnly'],
                            cwd=ROOT, env=env, capture_output=True, text=True)
    if result.returncode != 0:
        print('dotnet build failed:\n' + (result.stdout + result.stderr)[-2500:], file=sys.stderr)
        return 2
    return 0


sys.exit(main())
