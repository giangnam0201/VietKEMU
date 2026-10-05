param(
    [string]$AdbPath = 'adb',
    [string]$ScrcpyPath = 'scrcpy',
    [string]$Serial = 'emulator-5554',
    [ValidateRange(1, 20)][int]$TvDisplayId = 1
)
$ErrorActionPreference = 'Stop'
$adbCommand = (Get-Command $AdbPath -ErrorAction Stop).Source
$scrcpyCommand = (Get-Command $ScrcpyPath -ErrorAction Stop).Source
$bootState = & $adbCommand -s $Serial shell getprop sys.boot_completed
if ($LASTEXITCODE -ne 0 -or $bootState.Trim() -ne '1') {
    throw 'The adapted Android runtime must be running first. No substitute interface is provided.'
}
foreach ($package in @('com.evideo.kmbox','com.evideo.daulkmbox_osdtv','com.evideostb.cbb.cbbship','com.evideo.dcservice')) {
    $installed = & $adbCommand -s $Serial shell pm path $package
    if ($LASTEXITCODE -ne 0 -or $installed -notmatch 'package:') {
        throw "Original runtime component missing: $package"
    }
}
$displays = & $scrcpyCommand --serial=$Serial --list-displays 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $displays -notmatch "--display-id=$TvDisplayId\b") {
    throw "The actual Android TV display $TvDisplayId is unavailable. Runtime display adaptation is incomplete."
}
$startup = & $adbCommand -s $Serial shell am start -W -n com.evideo.kmbox/com.evideo.kmbox.activity.MicroServiceActivity
if ($LASTEXITCODE -ne 0 -or ($startup | Out-String) -match 'Error:|Exception') {
    throw "Original panel startup failed: $startup"
}
# These windows display the original guest framebuffers; no HTML/UI replica.
Start-Process -FilePath $scrcpyCommand -ArgumentList @("--serial=$Serial", '--display-id=0', '--window-title=VietK-Panel', '--no-audio', '--stay-awake') -WindowStyle Hidden
Start-Process -FilePath $scrcpyCommand -ArgumentList @("--serial=$Serial", "--display-id=$TvDisplayId", '--window-title=VietK-TV', '--no-control') -WindowStyle Hidden
