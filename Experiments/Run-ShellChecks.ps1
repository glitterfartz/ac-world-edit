param([string]$GodotPath = 'X:\Godot-4.7\Godot_v4.7.1-stable_mono_win64_console.exe')
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repositoryRoot
try {
    & dotnet build ACWorldEdit.csproj -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Viewer build failed' }
    $standaloneOutput = & $GodotPath --headless --path $repositoryRoot -- --foundation-shell --foundation-shell-smoke 2>&1
    $standaloneOutput | Write-Output
    if ($LASTEXITCODE -ne 0 -or -not ($standaloneOutput -match 'standalone shell instantiated and fixture workflow passed')) { throw 'Standalone shell check failed' }

    # Test plugin enablement in a project-owned copy; never rewrite the user's project configuration.
    $trialRoot = Join-Path $repositoryRoot ('artifacts\shell-host\' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $trialRoot,(Join-Path $trialRoot 'Foundation'),(Join-Path $trialRoot 'Scripts\Experiments'),(Join-Path $trialRoot 'addons\foundation_trial') | Out-Null
    foreach ($file in @('ACWorldEdit.csproj','global.json','NuGet.Config')) { Copy-Item -LiteralPath (Join-Path $repositoryRoot $file) -Destination $trialRoot }
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'Foundation') -File | Copy-Item -Destination (Join-Path $trialRoot 'Foundation')
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'Scripts\Experiments') -File | Copy-Item -Destination (Join-Path $trialRoot 'Scripts\Experiments')
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'addons\foundation_trial') -File | Copy-Item -Destination (Join-Path $trialRoot 'addons\foundation_trial')
    @'
config_version=5
[application]
config/name="Foundation shell host"
run/main_scene="res://Trial.tscn"
config/features=PackedStringArray("4.7", "C#", "GL Compatibility")
[dotnet]
project/assembly_name="ACWorldEdit"
[editor_plugins]
enabled=PackedStringArray("res://addons/foundation_trial/plugin.cfg")
[rendering]
renderer/rendering_method="gl_compatibility"
'@ | Set-Content -LiteralPath (Join-Path $trialRoot 'project.godot')
    @'
[gd_scene load_steps=2 format=3]
[ext_resource type="Script" path="res://Scripts/Experiments/FoundationShellTrial.cs" id="1"]
[node name="Trial" type="VBoxContainer"]
script=ExtResource("1")
'@ | Set-Content -LiteralPath (Join-Path $trialRoot 'Trial.tscn')
    & dotnet build (Join-Path $trialRoot 'ACWorldEdit.csproj') -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw 'Isolated plugin host build failed' }
    $pluginOutput = & $GodotPath --headless --editor --path $trialRoot --quit-after 300 -- --foundation-plugin-smoke 2>&1
    $pluginOutput | Write-Output
    if ($LASTEXITCODE -ne 0 -or -not ($pluginOutput -match 'EditorPlugin shell instantiated and fixture workflow passed')) { throw 'EditorPlugin shell check failed' }
    Write-Output 'PASS matched standalone/EditorPlugin fixture smoke (headless; not usability or GPU evidence).'
}
finally { Pop-Location }
