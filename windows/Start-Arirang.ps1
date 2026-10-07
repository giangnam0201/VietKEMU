$ErrorActionPreference = 'Stop'
$arirangExecutable = Join-Path $PSScriptRoot 'Arirang.MidiPlayer\bin\Release\net8.0-windows\win-x64\publish\Arirang.MidiPlayer.exe'
if (!(Test-Path -LiteralPath $arirangExecutable)) {
    throw 'Download the Arirang branch Windows release from GitHub, extract it and run Arirang.MidiPlayer.exe. Builds are done in GitHub Actions.'
}
Start-Process -FilePath $arirangExecutable -WindowStyle Hidden
