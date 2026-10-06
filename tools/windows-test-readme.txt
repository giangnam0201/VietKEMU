VietK native Windows port - partial test build

Windows 10/11, 64-bit. The .NET runtime is included.
Extract the ENTIRE ZIP into a folder before opening VietK.NativePort.exe.
Keep the DLLs and Original folder beside the executable.
Keep YouTubeTools beside the executable as well.

Main music source: YouTube (yt-dlp + native TV decoder)
- Search a song name, or paste a YouTube video link into the search field.
- Click a result to queue it; right-click and choose Hat ngay to play it first.
- Download progress and errors appear on the panel. Try again with Thu lai.
- Completed video/audio files are cached locally before native TV playback.
- Next, pause/play, replay and volume use the existing bottom controls.
- yt-dlp, Deno, FFmpeg and ffprobe are bundled. No API key is needed for this mode.
- YouTube availability varies; restricted/unavailable videos can fail to fetch.
- If YouTube requires sign-in, click Firefox to use your own local Firefox
  YouTube session, then retry. Sign into YouTube in Firefox first if needed.
  File... selects your own exported Netscape cookie file instead.
  Bo dang nhap disables both options. Login stays on this PC, never in GitHub.
- The downloaded video does not have VietK vocal/accompaniment track metadata.
  Choose a karaoke/instrumental upload for accompaniment.

YouTube cache: %LOCALAPPDATA%\VietKNativePort\youtube-music
YouTube queue: %LOCALAPPDATA%\VietKNativePort\youtube-queue.json
yt-dlp updates: replace YouTubeTools\yt-dlp.exe with the official current release.

Available to test:
- Home and More screens using extracted original resources.
- Song browser, Vietnamese keyboard, song grid and catalogue search.
- Native database and queue logic translated from the original app.
- Independent panel and TV windows with bundled Windows video decoder.
- TV video and overlays are also previewed inside the panel's black video slot.
- Original TV play/pause/replay/volume images and scrolling current/next song.
- An actual Demo.mp4 beside the EXE, or in the app-state folder below, plays
  and loops while the queue is empty. Factory 60003950.mp4 is also accepted.
  The original APK background video is bundled as the default idle fallback.
  Its bytes are authentic; it is not verified identical to the missing HDD
  factory idle clip. Add your device's clip to reproduce that idle broadcast.
- Server login/media-request transport and music file download to playback queue.
- Pause, replay, seek, original/accompaniment and output volume controls.

Still incomplete:
- A live login accepted this PC's real BIOS serial and MAC, but song 101000's
  media request returned no video URL. Production song playback is unverified.
  Firmware alone does not contain an individual device's hardware chip ID.
- Encrypted karaoke, complete TV overlays, subtitles/scoring and microphone DSP.
- Other screens, controls and full original UI/UX parity.
Song catalogue entries are metadata; this package does not include playable music.
Some controls remain unimplemented. This is not yet a complete 1:1 port.

App state: %LOCALAPPDATA%\VietKNativePort
Server configuration: %LOCALAPPDATA%\VietKNativePort\music-server.json
Fill ChipId, Mac and UserAgent with your authorized device information and restart.
LoginUrl defaults to the original firmware's http://viet.duochang.cc/login.
The main YouTube mode does not log in to this server.
MusicDirectory may specify your song cache folder (500 MiB free required).
No fabricated device identity, token or authentication success is supplied.
If startup fails: %TEMP%\vietk-native-startup-error.txt
