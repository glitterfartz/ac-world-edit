# PostToolUse hook: run `dotnet format whitespace` on the single edited .cs file.
# Uses the nearest csproj above the file so Tests/* projects format with their own project.
# Never blocks: problems go to stderr with exit 1 (non-blocking warning).
$data = [Console]::In.ReadToEnd() | ConvertFrom-Json
$path = $data.tool_input.file_path
if (-not $path -or -not $path.ToLower().EndsWith('.cs') -or -not (Test-Path -LiteralPath $path)) { exit 0 }
$path = (Resolve-Path -LiteralPath $path).Path
$dir = Split-Path $path -Parent
$proj = $null
while ($dir) {
    $hit = Get-ChildItem -LiteralPath $dir -Filter *.csproj -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($hit) { $proj = $hit.FullName; break }
    $dir = Split-Path $dir -Parent
}
if (-not $proj) { exit 0 }
$out = & dotnet format whitespace $proj --no-restore --include $path 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine($out.Substring([Math]::Max(0, $out.Length - 1500)))
    exit 1
}
exit 0
