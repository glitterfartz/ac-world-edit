# PreToolUse hook: protect read-only references (AGENTS.md).
# Blocks file edits under the azerothcore-wotlk clone or the wow-data junction, and shell commands
# that appear to modify/delete/move them. Exit 2 blocks the call and shows stderr to Claude.
$data = [Console]::In.ReadToEnd() | ConvertFrom-Json
$tool = $data.tool_name
$protected = @('azerothcore-wotlk', 'wow-data')

function Block($why) {
    [Console]::Error.WriteLine("Blocked: $why (read-only reference per AGENTS.md).")
    exit 2
}

if ($tool -in 'Edit', 'Write', 'NotebookEdit') {
    $p = ("$($data.tool_input.file_path)$($data.tool_input.notebook_path)").Replace('\', '/').ToLower()
    foreach ($n in $protected) {
        if ($p.Contains("/$n/") -or $p.EndsWith("/$n")) { Block "$tool targets $n" }
    }
}
elseif ($tool -in 'Bash', 'PowerShell') {
    $cmd = "$($data.tool_input.command)".Replace('\', '/').ToLower()
    $mutating = '\b(rm|rmdir|mv|move|move-item|remove-item|del|erase|rd|cp|copy-item|tee|touch|mkdir|new-item|set-content|out-file)\b|git\s+(pull|reset|clean|checkout|commit)|sed\s+-i|>>?\s*\S'
    if ($cmd -match $mutating) {
        foreach ($n in $protected) {
            if ($cmd -match "(^|[\s/`"'=])$([regex]::Escape($n))(/|\s|`"|'|$)") { Block "shell command may modify $n" }
        }
    }
}
exit 0
