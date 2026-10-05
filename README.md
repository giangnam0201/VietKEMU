# VietKEMU

Research workspace for the supplied `KTV-Plus_All_V1.7_RC1_update_20230118.zip`.
The target is a Windows runtime with separate touch-panel and television-output
windows, using the original firmware applications wherever possible.

**Status: original APK compatibility port in development, not a working emulator.** No device boot or
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

Large extraction runs in the private repository's GitHub Actions, not on the
Windows host. The original ZIP is stored as a private release asset; it is not
committed to Git or publicly redistributed.

```powershell
gh workflow run firmware.yml --repo giangnam0201/VietKEMU
gh run list --repo giangnam0201/VietKEMU --workflow firmware.yml
gh run download RUN_ID --repo giangnam0201/VietKEMU -n firmware-report -D artifacts/report
```

The workflow reconstructs the system filesystem, extracts system/vendor files,
inventories applications and native libraries, decodes karaoke resources and
records hardware dependencies. Extracted proprietary files remain in private
artifacts with limited retention.

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
and crash logs. The adapter currently reports unsupported audio/display hardware
operations; it does not yet implement playback, recording, or two Windows windows.
Original activation logic is retained. Song servers and licensed media availability
have not been verified. `port-research.yml` inspects the dependencies in the cloud.

The original MainActivity already starts the original TV service when Android
reports multiple displays. `configure_displays.py` supplies a 1280×800 panel and
1920×1080 external display. `windows/Start-VietK.ps1` hosts those actual guest
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
