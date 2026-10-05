# Manual Windows port

The current requested target is a native Windows executable with two windows:
the original panel interface and the original television interface. Android
emulator experiments are historical research, not the delivery architecture.

`native-decode.yml` decodes every vendor APK on GitHub. Apktool retains decoded
resources and smali for all DEX files; JADX reconstructs Java where possible.
Per-app reports explicitly list decompiler failures. Decompiled Java is not the
original developer source, and ELF inspection does not recover original C/C++.

Port each screen using its original layouts, drawables, strings, fonts and web
assets, then translate its handlers and state transitions. Map Android services,
IPC, storage and media APIs to Windows implementations. Keep a source mapping
for each feature. Native vendor libraries need separate Windows implementations;
ARM shared libraries cannot simply be renamed to DLLs.

Order: startup and configuration; panel layout and navigation; catalogue/search;
queue and persistence; television UI; actual playback and audio controls;
original server/download protocol; remaining settings and peripheral features.
The original activation and server authentication remain part of the behavior.

Implemented source translations: `HomeNewFragment`/`HomeNewAdapter` home body,
original Vietnamese labels and drawables, default tile ordering, and the original
0.9 scale/25 ms press animation. The Windows component host is a development
artifact: it does not yet include the main top/bottom bars, screen navigation,
Phantom video, television UI, activation, playback or server operations.

`WholeCatalogue` translates read-only `WholeSongDAO` count, existence, ID lookup
and remote-metadata checks. It reads the unchanged original database. An online
metadata flag does not establish a live server or available song file.

`native-windows.yml` checks original image hashes, compiles on Windows and renders
the native home body to PNG. It also checks an actual Vietnamese catalogue entry
and that missing entries are not fabricated. These checks do not establish full
UI or feature fidelity.

Verified cloud run: https://github.com/giangnam0201/VietKEMU/actions/runs/37389039595
(Windows compilation, native PNG rendering, original default tile ordering,
original asset hashes, Vietnamese song ID 101000 lookup, missing-ID handling,
and catalogue count 72,355). The component host contains pending navigation
events; it is not presented as a usable karaoke application.

The firmware contains a song
catalogue, not the complete song media collection. A port must report unavailable
media and server failures truthfully. Decoding success does not establish 1:1
fidelity or a working Windows release.
