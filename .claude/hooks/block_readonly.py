# PreToolUse hook: protect read-only references (AGENTS.md).
# Blocks file edits under the azerothcore-wotlk clone or the wow-data junction, and shell commands
# that appear to modify/delete/move them. Exit 2 blocks the call and shows stderr to Claude.
import json
import re
import sys

PROTECTED = ('azerothcore-wotlk', 'wow-data')
MUTATING = re.compile(
    r'\b(rm|rmdir|mv|move|move-item|remove-item|del|erase|rd|cp|copy-item|tee|touch|mkdir|new-item|set-content|out-file)\b'
    r'|git\s+(pull|reset|clean|checkout|commit)|sed\s+-i|>>?\s*\S')


def block(why):
    print(f'Blocked: {why} (read-only reference per AGENTS.md).', file=sys.stderr)
    sys.exit(2)


def main():
    data = json.load(sys.stdin)
    tool = data.get('tool_name')
    tool_input = data.get('tool_input') or {}
    if tool in ('Edit', 'Write', 'NotebookEdit'):
        p = f"{tool_input.get('file_path', '')}{tool_input.get('notebook_path', '')}".replace('\\', '/').lower()
        for n in PROTECTED:
            if f'/{n}/' in p or p.endswith(f'/{n}'):
                block(f'{tool} targets {n}')
    elif tool in ('Bash', 'PowerShell'):
        cmd = str(tool_input.get('command', '')).replace('\\', '/').lower()
        if MUTATING.search(cmd):
            for n in PROTECTED:
                if re.search(rf'(^|[\s/"\'=]){re.escape(n)}(/|\s|"|\'|$)', cmd):
                    block(f'shell command may modify {n}')
    sys.exit(0)


main()
