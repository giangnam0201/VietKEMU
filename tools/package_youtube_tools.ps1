param([Parameter(Mandatory=$true)][string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
$destination = Join-Path (Resolve-Path -LiteralPath $PublishDirectory).Path 'YouTubeTools'
$incoming = Join-Path (Get-Location).Path 'artifacts/youtube-tool-input'
New-Item -ItemType Directory -Force -Path $destination,$incoming | Out-Null
function Invoke-GhChecked([string[]]$Arguments) {
    & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Official tool download failed' }
}
Invoke-GhChecked @('release','download','2026.08.19','--repo','yt-dlp/yt-dlp','--pattern','yt-dlp.exe','--pattern','SHA2-256SUMS','--dir',$incoming)
$checksum = (Get-Content -LiteralPath (Join-Path $incoming 'SHA2-256SUMS') | Where-Object { $_ -match '\s+yt-dlp\.exe$' }).Split()[0]
if ((Get-FileHash -LiteralPath (Join-Path $incoming 'yt-dlp.exe') -Algorithm SHA256).Hash -ne $checksum) { throw 'yt-dlp checksum mismatch' }
Copy-Item -LiteralPath (Join-Path $incoming 'yt-dlp.exe') -Destination $destination
Invoke-GhChecked @('release','download','v2.9.7','--repo','denoland/deno','--pattern','deno-x86_64-pc-windows-msvc.zip','--dir',$incoming)
Expand-Archive -LiteralPath (Join-Path $incoming 'deno-x86_64-pc-windows-msvc.zip') -DestinationPath (Join-Path $incoming 'deno')
Copy-Item -LiteralPath (Join-Path $incoming 'deno/deno.exe') -Destination $destination
Invoke-GhChecked @('release','download','latest','--repo','yt-dlp/FFmpeg-Builds','--pattern','ffmpeg-master-latest-win64-gpl.zip','--pattern','checksums.sha256','--dir',$incoming)
$archive = Join-Path $incoming 'ffmpeg-master-latest-win64-gpl.zip'
$ffmpegChecksum = (Get-Content -LiteralPath (Join-Path $incoming 'checksums.sha256') | Where-Object { $_ -match '\s+ffmpeg-master-latest-win64-gpl\.zip$' }).Split()[0]
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $ffmpegChecksum) { throw 'FFmpeg checksum mismatch' }
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $incoming 'ffmpeg')
foreach ($name in @('ffmpeg.exe','ffprobe.exe')) {
    $matches = @(Get-ChildItem -LiteralPath (Join-Path $incoming 'ffmpeg') -Filter $name -Recurse -File)
    if ($matches.Count -ne 1) { throw "Cannot locate $name" }
    Copy-Item -LiteralPath $matches[0].FullName -Destination $destination
}
foreach ($license in Get-ChildItem -LiteralPath (Join-Path $incoming 'ffmpeg') -Recurse -File | Where-Object { $_.Name -match '^LICENSE|^COPYING' }) {
    Copy-Item -LiteralPath $license.FullName -Destination (Join-Path $destination ('ffmpeg-'+$license.Name))
}
Invoke-WebRequest 'https://raw.githubusercontent.com/yt-dlp/yt-dlp/2026.08.19/LICENSE' -OutFile (Join-Path $destination 'yt-dlp-LICENSE.txt')
Invoke-WebRequest 'https://raw.githubusercontent.com/yt-dlp/yt-dlp/2026.08.19/THIRD_PARTY_LICENSES.txt' -OutFile (Join-Path $destination 'yt-dlp-THIRD_PARTY_LICENSES.txt')
Invoke-WebRequest 'https://raw.githubusercontent.com/denoland/deno/v2.9.7/LICENSE.md' -OutFile (Join-Path $destination 'deno-LICENSE.txt')
$files = @(Get-ChildItem -LiteralPath $destination -Filter '*.exe' | ForEach-Object {
    @{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
@{ytDlp='2026.08.19';deno='v2.9.7';ffmpegRelease='latest';files=$files;
    sources=@('https://github.com/yt-dlp/yt-dlp','https://github.com/denoland/deno','https://github.com/yt-dlp/FFmpeg-Builds')} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'provenance.json')
& (Join-Path $destination 'yt-dlp.exe') --version
if ($LASTEXITCODE -ne 0) { throw 'Bundled yt-dlp cannot run' }
& (Join-Path $destination 'ffmpeg.exe') -version
if ($LASTEXITCODE -ne 0) { throw 'Bundled FFmpeg cannot run' }
