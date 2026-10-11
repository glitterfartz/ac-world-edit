param(
    [string]$GodotPath = "",
    [switch]$CheckAssets,
    [switch]$Smoke
)
$ErrorActionPreference = "Stop"
Set-Location -LiteralPath $PSScriptRoot
# Keep CLI caches inside the project, including in restricted development sessions.
$buildEnvironment = Join-Path $PSScriptRoot '.build-env'
$env:APPDATA = Join-Path $buildEnvironment 'roaming'
$env:LOCALAPPDATA = Join-Path $buildEnvironment 'local'
New-Item -ItemType Directory -Force -Path $env:APPDATA, $env:LOCALAPPDATA | Out-Null
if (-not $env:ProgramFiles) { $env:ProgramFiles = 'C:\Program Files' }
if (-not (Test-Path -LiteralPath 'Env:ProgramFiles(x86)')) { Set-Item -LiteralPath 'Env:ProgramFiles(x86)' -Value 'C:\Program Files (x86)' }
if (-not $env:ProgramData) { $env:ProgramData = 'C:\ProgramData' }
if (-not $env:ALLUSERSPROFILE) { $env:ALLUSERSPROFILE = $env:ProgramData }
$env:MSBuildEnableWorkloadResolver = 'false'
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $env:USERPROFILE '.nuget\packages' }
if ($CheckAssets) {
    & dotnet run --project Tests/AssetChecks.csproj -p:NuGetAudit=false -- wow-data
    exit $LASTEXITCODE
}
if (-not $GodotPath) {
    $shortcutPath = Join-Path $PSScriptRoot 'Godot 4.7.lnk'
    if (Test-Path -LiteralPath $shortcutPath) {
        try { $GodotPath = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath).TargetPath } catch { }
    }
    if (-not $GodotPath -or -not (Test-Path -LiteralPath $GodotPath)) {
        $localGodot = 'X:\Godot-4.7\Godot_v4.7.1-stable_mono_win64.exe'
        if (Test-Path -LiteralPath $localGodot) { $GodotPath = $localGodot }
        else { $GodotPath = (Get-Command godot -ErrorAction Stop).Source }
    }
}
& dotnet build ACWorldEdit.csproj -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Smoke) { & $GodotPath --headless --path $PSScriptRoot -- --terrain-smoke }
else { & $GodotPath --path $PSScriptRoot }
exit $LASTEXITCODE
