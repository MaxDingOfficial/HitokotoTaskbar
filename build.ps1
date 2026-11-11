[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\common.ps1')
$projectDotNet = Get-ProjectDotNet -ProjectRoot $PSScriptRoot
$solutionPath = Join-Path $PSScriptRoot 'HitokotoTaskbar.slnx'
$resultsPath = Join-Path $PSScriptRoot 'artifacts\test-results'
New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null

Push-Location $PSScriptRoot
try {
    Invoke-ProjectDotNet $projectDotNet @('restore', $solutionPath)
    Invoke-ProjectDotNet $projectDotNet @('build', $solutionPath, '-c', $Configuration, '--no-restore', '--nologo')
    if (-not $SkipTests) {
        # WPF and Core can both be reported as .NETCoreApp v10.0. Give every
        # project an explicit filename instead of relying on timestamps.
        $testProjects = Get-ChildItem -Path (Join-Path $PSScriptRoot 'tests\*\*.csproj') -File
        foreach ($testProject in $testProjects) {
            $loggerArgument = 'trx;LogFileName=' + $testProject.BaseName + '.trx'
            Invoke-ProjectDotNet $projectDotNet @('test', $testProject.FullName, '-c', $Configuration, '--no-build', '--nologo', '--logger', $loggerArgument, '--results-directory', $resultsPath)
        }
    }
    Write-Host ('Build completed. Configuration: ' + $Configuration)
    if ($SkipTests) { Write-Host 'Tests were explicitly skipped.' }
}
finally { Pop-Location }
