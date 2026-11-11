[CmdletBinding()]
param([switch]$SkipTests)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\common.ps1')
$projectDotNet = Get-ProjectDotNet -ProjectRoot $PSScriptRoot
$projectPath = Join-Path $PSScriptRoot 'src\HitokotoTaskbar\HitokotoTaskbar.csproj'
$publishPath = Join-Path $PSScriptRoot 'artifacts\publish\win-x64'

# All writes stay inside this repository. No recursive cleanup is performed.
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
Push-Location $PSScriptRoot
try {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release -SkipTests:$SkipTests
    Invoke-ProjectDotNet $projectDotNet @(
        'publish', $projectPath, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:PublishTrimmed=false', '-p:DebugType=embedded', '-p:DebugSymbols=false',
        '--nologo', '-o', $publishPath
    )
    & (Join-Path $PSScriptRoot 'scripts\package-release.ps1')
}
finally { Pop-Location }
