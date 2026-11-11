[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$publishPath = Join-Path $projectRoot 'artifacts\publish\win-x64'
$executablePath = Join-Path $publishPath 'HitokotoTaskbar.exe'
$packagePath = Join-Path $projectRoot 'artifacts\HitokotoTaskbar-win-x64.zip'
$hashPath = Join-Path $projectRoot 'artifacts\SHA256SUMS.txt'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw 'No published executable exists. Run publish.ps1 first.'
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $publishPath 'README.md') -Force
$docsPath = Join-Path $publishPath 'docs'
New-Item -ItemType Directory -Path $docsPath -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs') -File -Filter '*.md' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $docsPath $_.Name) -Force
}
# Select only delivery files, so stale compiler outputs cannot enter the package.
Compress-Archive -LiteralPath @($executablePath, (Join-Path $publishPath 'README.md'), $docsPath) -DestinationPath $packagePath -Force
$hashLines = @($executablePath, $packagePath) | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), [System.IO.Path]::GetFileName($_)
}
[System.IO.File]::WriteAllLines($hashPath, $hashLines, [System.Text.UTF8Encoding]::new($false))
Write-Host ('Executable: ' + $executablePath)
Write-Host ('Portable package: ' + $packagePath)
Write-Host ('Checksums: ' + $hashPath)
