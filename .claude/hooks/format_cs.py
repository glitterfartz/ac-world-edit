# PostToolUse hook: run `dotnet format whitespace` on the single edited .cs file.
# Uses the nearest csproj above the file so Tests/* projects format with their own project.
# Never blocks: problems go to stderr with exit 1 (non-blocking warning).
import json
import subprocess
import sys
from pathlib import Path


def main():
    data = json.load(sys.stdin)
    raw = (data.get('tool_input') or {}).get('file_path')
    if not raw or not raw.lower().endswith('.cs') or not Path(raw).is_file():
        return 0
    path = Path(raw).resolve()
    proj = None
    for directory in path.parents:
        hit = next(iter(sorted(directory.glob('*.csproj'))), None)
        if hit:
            proj = hit
            break
    if proj is None:
        return 0
    result = subprocess.run(['dotnet', 'format', 'whitespace', str(proj), '--no-restore', '--include', str(path)],
                            capture_output=True, text=True)
    if result.returncode != 0:
        print((result.stdout + result.stderr)[-1500:], file=sys.stderr)
        return 1
    return 0


sys.exit(main())
