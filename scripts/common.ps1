Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ProjectDotNet {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)

    $localDotNet = Join-Path $ProjectRoot '.tools\dotnet\dotnet.exe'
    $candidates = [System.Collections.Generic.List[string]]::new()
    if (Test-Path -LiteralPath $localDotNet -PathType Leaf) {
        $candidates.Add($localDotNet)
    }
    $installed = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($installed) { $candidates.Add($installed.Source) }
    foreach ($candidate in $candidates) {
        $sdkList = @(& $candidate --list-sdks 2>&1)
        if ($LASTEXITCODE -eq 0 -and ($sdkList | Where-Object { "$_" -match '^10\.0\.\d+\s' })) {
            return $candidate
        }
    }
    throw 'A stable .NET 10 SDK is required. Install it, or put it under .tools\dotnet. The runtime alone cannot build this project. See README.md.'
}

function Invoke-ProjectDotNet {
    param(
        [Parameter(Mandatory = $true)][string]$DotNet,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )
    Write-Host ('> dotnet ' + ($Arguments -join ' '))
    & $DotNet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw ('dotnet failed with exit code {0}: {1}' -f $LASTEXITCODE, ($Arguments -join ' '))
    }
}
