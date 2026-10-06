# VietKEMU

Research workspace for the supplied `KTV-Plus_All_V1.7_RC1_update_20230118.zip`.
The target is a Windows runtime with separate touch-panel and television-output
windows, using the original firmware applications wherever possible.

**Current target: manual native Windows EXE port, without an Android emulator.**
See [native port approach](docs/native-windows-port.md). `native-decode.yml` decodes
the original APKs on GitHub for a screen-by-screen and feature-by-feature rewrite.
The Android runtime experiments below are historical research.

**Status: partial native Windows port; 1:1 fidelity remains unverified.** The port
uses original UI resources and the 72,355-song catalogue. YouTube is the primary
music source through yt-dlp and a native decoder, with separate panel/TV windows,
shared video frames, queue controls, and playback while downloading. Growing-file
playback and decoded video/audio controls have passed Windows verification.
Live YouTube requests can encounter bot checks; your Firefox session stays local.
Original VietK login alone has not provided working music access.

Run `native-windows-release.yml` (or `native-windows.yml`) for a Windows test ZIP
and verification reports in [Releases](https://github.com/giangnam0201/VietKEMU/releases).
No Android emulator is required. See the included `START-HERE.txt` for controls
and limitations. Microphone DSP, scoring, mobile control, ambience and remaining
original screens still need porting.

No device boot or
1:1 compatibility has been demonstrated. An interface replica would not establish
firmware compatibility.

See [firmware findings](docs/firmware-findings.md) for the original application
stack, catalogue inventory and the vendor interfaces that must be reproduced.

## Confirmed from the supplied archive

- Android 6.0.1, device `KTV-Plus`, eVideo build
  `lvbinhui.20220629.v1.2.b57`.
- Device-tree compatibility includes `Realtek,rtd-1296`.
- The update configuration enables `secure_boot=1`; kernel, recovery and audio
  firmware are supplied as `.aes` files.
- Android system is a full block OTA (`system.new.dat` plus transfer list).
- Vendor filesystem and a separate SquashFS filesystem are included.
- This is an update archive, not an Android Virtual Device image.

## Remote analysis

The repository is public. The original firmware ZIP, raw APK analysis uploads,
old Actions logs and artifacts were removed before publication. Local firmware
and decoded references remain available to the developer but are ignored by Git.
Current Windows builds retrieve a prepared UI resource bundle from a release;
they do not upload browser cookies, device identities or local session files.

The commands below describe historical extraction work. Its private input
release is no longer available, so those workflows need separately supplied
authorized firmware before reuse.

```powershell
gh workflow run firmware.yml --repo giangnam0201/VietKEMU
gh run list --repo giangnam0201/VietKEMU --workflow firmware.yml
gh run download RUN_ID --repo giangnam0201/VietKEMU -n firmware-report -D artifacts/report
```

The workflow reconstructs the system filesystem, extracts system/vendor files,
inventories applications and native libraries, decodes karaoke resources and
records hardware dependencies. The old raw extraction artifacts were deleted.

`runtime-research.yml` inspects the original VDK Java APIs and native dependencies
on GitHub. `compatibility.yml` tests installation and launch on a stock Android
guest with ARM translation, saving screenshots and diagnostics. A successful
workflow means the experiment ran; it does not certify application compatibility.

The completed stock Android experiment rejected the main karaoke/control/TV
applications because their original certificates do not match the guest's
system identity. Other components require the missing original vendor SDK.
Only helper activities launched; karaoke operation is unverified.

`port.yml` builds compatibility copies with the original resources and application
DEX preserved byte for byte. It bundles the original vendor Java SDK, compiles a
portable JNI startup adapter, and signs the copies for a development Android
guest. It then installs and launches the stack on GitHub and records screenshots
and crash logs. The adapter maps UART access to real guest serial ports and
microphone recording to Android AudioRecord through the original application
interfaces. Unsupported vendor display operations return errors. The recorder
compiles on GitHub; Windows microphone capture and playback remain unverified.
Original activation logic is retained. Song servers and licensed media availability
have not been verified. `port-research.yml` inspects the dependencies in the cloud.

The original MainActivity already starts the original TV service when Android
reports multiple displays. `configure_displays.py` supplies a 1280×800 panel and
1280×720 external framebuffer, matching the external size forced by the original
build properties. The guest starts with the original Vietnamese locale.
`windows/Start-VietK.ps1` hosts those actual guest
framebuffers in separate Windows windows using scrcpy, once the adapted runtime
is booted and all original components are installed. It is not a standalone
working release: runtime provisioning, playback and TV startup remain unverified.
A replacement Windows interface was discarded because it would not preserve the
required original UX.

The small OTA reconstruction tests can run locally without extracting firmware:

```powershell
python -m unittest discover -s tests -v
```

## Compatibility gates

1. Identify the actual karaoke package, its launcher and required system services.
2. Test original applications in an ARM-compatible Android runtime on GitHub.
3. Establish separate panel/output display paths using actual application output.
4. Validate song database, local playback, controls and persistence.
5. Compare against real hardware before describing fidelity as 1:1.

CPU emulation alone does not reproduce Realtek video/audio engines, vendor HALs,
external display routing or device provisioning. A generic QEMU ARM board cannot
be assumed to boot this update. Without a physical reference device, pixel and
behavior fidelity cannot be verified.

## Source references

- [QEMU ARM system boards](https://www.qemu.org/docs/master/system/target-arm.html)
- [QEMU generic virt board](https://www.qemu.org/docs/master/system/arm/virt)
- [VietK Plus manual](https://vietk.vn/upload/KTV-Plus.pdf)

These references describe platforms and product behavior. The supplied archive
is the source for the firmware facts above.
