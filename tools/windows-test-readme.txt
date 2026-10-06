VietK native Windows port - partial test build

Windows 10/11, 64-bit. The .NET runtime is included.
Extract the ENTIRE ZIP into a folder before opening VietK.NativePort.exe.
Keep the DLLs and Original folder beside the executable.

Available to test:
- Home and More screens using extracted original resources.
- Song browser, Vietnamese keyboard, song grid and catalogue search.
- Native database and queue logic translated from the original app.

Still incomplete:
- Karaoke playback and the second TV/output screen.
- Music downloads and live server integration.
- Other screens, controls and full original UI/UX parity.
Song catalogue entries are metadata; this package does not include playable music.
Some controls remain unimplemented. This is not yet a complete 1:1 port.

App state: %LOCALAPPDATA%\VietKNativePort
If startup fails: %TEMP%\vietk-native-startup-error.txt
