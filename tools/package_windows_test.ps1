param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$BuildId
)
$ErrorActionPreference = 'Stop'
if ($BuildId -notmatch '^[a-zA-Z0-9._-]+$') { throw 'Invalid build identifier' }
$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $publishPath 'VietK.NativePort.exe'))) { throw 'Windows executable missing' }
if (!(Test-Path -LiteralPath (Join-Path $publishPath 'Original') -PathType Container)) { throw 'Original resources missing' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$outputPath = (Resolve-Path -LiteralPath $OutputDirectory).Path
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'windows-test-readme.txt') -Destination (Join-Path $publishPath 'START-HERE.txt')
Add-Content -LiteralPath (Join-Path $publishPath 'START-HERE.txt') -Value "`r`nBuild: $BuildId"
$zipPath = Join-Path $outputPath "VietK-Windows-$BuildId.zip"
if (Test-Path -LiteralPath $zipPath) { throw "Package already exists: $zipPath" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishPath, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Output $zipPath
