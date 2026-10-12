param([string]$DataPath = 'wow-data')
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repositoryRoot
try {
    $toolRoot = Join-Path $repositoryRoot 'artifacts\stormlib-9.40'
    $zipPath = Join-Path $toolRoot 'stormlib_dll.zip'
    $dllPath = Join-Path $toolRoot 'dll\x64\StormLib.dll'
    New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null
    if (-not (Test-Path -LiteralPath $zipPath)) {
        Invoke-WebRequest 'https://github.com/ladislav-zezula/StormLib/releases/download/v9.40/stormlib_dll.zip' -OutFile $zipPath
    }
    if ((Get-FileHash -LiteralPath $zipPath).Hash -ne 'B2C9635E7B63EDEE1BD7C82E7DC180D739F3ACCB2B8994804C7774E464CE89AE') {
        throw 'StormLib 9.40 official release SHA256 mismatch'
    }
    if (-not (Test-Path -LiteralPath $dllPath)) { Expand-Archive -LiteralPath $zipPath -DestinationPath (Join-Path $toolRoot 'dll') }
    if ((Get-FileHash -LiteralPath $dllPath).Hash -ne '93321F6F030BE5D7D79EB4CBF433D6EF7E83CE20D47D8963717F207D54AFBE16') {
        throw 'StormLib x64 DLL does not match the verified release extraction'
    }
    & dotnet run --project Tests/AssetChecks.csproj -p:NuGetAudit=false -- $DataPath $dllPath
    if ($LASTEXITCODE -ne 0) { throw 'StormLib differential check failed' }
}
finally { Pop-Location }
