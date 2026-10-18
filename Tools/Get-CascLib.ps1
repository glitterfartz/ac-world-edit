<#
.SYNOPSIS
Places CascLib.dll in native/win-x64 so the Retail client profile can read CASC storage.

.DESCRIPTION
The repo does not ship binaries. Build CascLib yourself (MIT, https://github.com/ladislav-zezula/CascLib,
commit 38a3466, cmake -DCASC_BUILD_SHARED_LIB=ON for x64) or reuse an existing build, then pass its path
here. The DLL is copied into the gitignored native/win-x64 folder; the game project copies it next to the
assembly on build. Only the Retail profile needs it. Nothing is written to the WoW install.

.PARAMETER Source
Path to a 64-bit CascLib.dll.
#>
param(
    [Parameter(Mandatory = $true)][string]$Source
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
    throw "CascLib.dll not found at '$Source'."
}

$destinationDirectory = Join-Path $PSScriptRoot '..\native\win-x64'
New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
$destination = Join-Path $destinationDirectory 'CascLib.dll'
Copy-Item -LiteralPath $Source -Destination $destination -Force
$hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
Write-Host "Copied to $destination"
Write-Host "SHA-256 $hash"
