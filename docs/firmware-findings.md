# Findings from the supplied VietK Plus firmware

This document records observed evidence. It does not certify a working emulator.

## Firmware identity

The supplied OTA targets Android 6.0.1 / API 23, 32-bit ARM (`armeabi-v7a`), on
the `kylin` board platform. Its device tree identifies `Realtek,rtd-1296`.
The update configuration declares secure boot and supplies encrypted boot/audio
images. No original kernel has been booted in this project.

Supplied ZIP SHA-256:
`3ed7908845677be3840211133a53f2b6607cd0247f17040ca427e80eb22d5388`.

## Application and service stack

| Component | Original package | Role observed in firmware |
| --- | --- | --- |
| Main application | `com.evideo.kmbox` | Launcher, song selection and initialization |
| Control service | `com.evideostb.cbb.cbbship` | Native control, database and peripheral libraries |
| TV overlay | `com.evideo.daulkmbox_osdtv` | Separate display application |
| Data/display service | `com.evideo.dcservice` | Vendor service used by main app |
| HD player | `com.evideo.kmbox.hdplayer` | Original media-player application |
| VietK service | `vn.vietkmedia.vietkservice` | VietK-specific integration |
| Vendor SDK | `com.evideostb.vdk` | Shared Java framework and JNI hardware layer |

The main launcher is
`com.evideo.kmbox.activity.MicroServiceActivity`. Both the main app and CBB
declare `android:sharedUserId="android.uid.system"` in their original manifests.
The main app requires the `com.evideostb.vdk` shared library.

`VDK.init()` loads `libvdk.so`. That ARM library depends on Android internal
native libraries including `libnativehelper`, `libcutils`, `libutils`,
`libbinder` and `libtinyalsa`. It cannot be treated as an ordinary Windows DLL.

The original `HDMIManager` talks to Realtek Binder services such as
`RtkHDMIService`, `RtkVoutUtilService` and `RtkAoutUtilService`. `VGAManager`
also calls native display control, whose strings reference `/dev/dptx`.

During startup the main activity opens `/dev/ttyS1` and `/dev/ttyS2`, writes
the bytes `12`, and checks for the bytes `34` in the response. It then starts
CBB and initializes its other modules. These are concrete interfaces that a
custom runtime must reproduce; displaying the APK in a stock Android guest
does not implement them.

## Catalogue and media

Some assets named `.jpg` are actually SQLite databases:

| Asset | Content |
| --- | --- |
| `assets/kmbox.jpg` | Empty initial application database |
| `assets/mobiledb.jpg` | Empty initial mobile database |
| `assets/wholekmbox.jpg` | 72,355 song records, 72,355 media records and 6,149 singer records |

These counts are metadata. They do not mean that the update contains 72,355
playable song files. The APK contains several background, grading and
advertisement media assets and an `sdcard.zip` initialization bundle.

## Reproducible cloud evidence

- [Successful firmware reconstruction and resource decoding](https://github.com/giangnam0201/VietKEMU/actions/runs/37324921816)
- [Successful vendor Java/native inspection](https://github.com/giangnam0201/VietKEMU/actions/runs/37326004058)

Private artifacts hold the report, original applications, decoded resources and
compact vendor API evidence. Local copies are under ignored `artifacts/`.
Artifacts expire; the original ZIP remains available in the private
`firmware-input` release and can be used to regenerate them.

An initial cloud Android 11 guest booted with ARM translation enabled, but the
first application probe stopped on a network-checker launch timeout. That run
is not evidence of karaoke compatibility. The probe was corrected to save
every install result and continue after individual timeouts.

The stabilized [stock Android experiment](https://github.com/giangnam0201/VietKEMU/actions/runs/37327399418)
completed and recorded these results:

- Main app, TV overlay, CBB, HD player and several other platform apps were
  rejected with `INSTALL_FAILED_SHARED_USER_INCOMPATIBLE`. Their original
  signing certificates do not match the stock guest's `android.uid.system`.
- DCService and YouTube player were rejected with
  `INSTALL_FAILED_MISSING_SHARED_LIBRARY` for `com.evideostb.vdk`.
- EvSdk, sound-effect and touch-calibration activities launched. This does not
  demonstrate song selection, karaoke playback or separate panel/TV output;
  the latter helper apps reported unavailable CBB services.
- NetworkChecker timed out during launch. Guest timeouts are recorded without
  aborting the remaining application observations.

The original-app files and signatures were preserved in this experiment. A
separate optional experiment provisions the original VDK into a writable cloud
guest. That is a userspace compatibility test, not a Realtek board emulator.

The [first VDK provisioning experiment](https://github.com/giangnam0201/VietKEMU/actions/runs/37327866205)
failed to reconnect to its cloud guest after disabling verity and rebooting,
before copying the framework. It therefore did not test the provisioned VDK
and must not be interpreted as evidence that VDK provisioning cannot work.
The optional provisioning script remains experimental.

## Delivery status

The [development-guest port run](https://github.com/giangnam0201/VietKEMU/actions/runs/37339459780)
now boots after writable-system provisioning, loads the ARM translation layer,
and installs all 11 core application copies. The guest platform certificate
matches the public AOSP platform key, so the apps retain `android.uid.system`.
DisplayManager reports a primary display and a separate emulator external display.
The original main app and HD player still fail before Java startup: the ARM
`app_process` attempts to load the guest's x86 `libc++.so`. The next runtime
build includes the SDK's ARM linker configuration and execution setup.

The port builder now covers all 23 vendor APKs and verifies the original payload
files byte for byte, replacing only signing metadata and the documented library
manifest dependency. Optional interfaces and keyboards are included. The original
OS PackageInstaller still requires separate integration because it conflicts
with the guest's existing system package. The recorder JNI adapter compiles and
calls Android AudioRecord for actual PCM; microphone operation is unverified.
UART access uses actual guest device files and does not fabricate factory replies.

Original build properties specify Vietnamese locale, density 160, and a forced
1280×720 external framebuffer. These defaults are applied in the runtime setup.

There is no working Windows emulator executable, original firmware boot,
validated song playback or working panel/TV window pair in this repository.
The implemented deliverables include firmware extraction, original resource/API
inspection, compatibility APK builds, guest provisioning, portable JNI adapters,
and a Windows launcher for mirroring the real guest framebuffers. They establish
the starting point and blockers for a custom runtime; they do not fulfill the
requested 1:1 emulator yet.

## Requirements for an original-firmware runtime

1. A guest environment that supports the original ARM application code,
   shared framework and platform identity requirements.
2. Realtek display/audio service emulation and a working external-display path.
3. UART/peripheral and native CBB behavior needed by the original startup flow.
4. Working local media and database storage, then end-to-end playback tests.
5. Hardware reference measurements before any 1:1 fidelity claim.

Opening a support app, booting stock Android or creating two blank desktop
windows does not pass these requirements.
