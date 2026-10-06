VietK native Windows port - partial test build

Windows 10/11, 64-bit. The .NET runtime is included.
Extract the ENTIRE ZIP into a folder before opening VietK.NativePort.exe.
Keep the DLLs and Original folder beside the executable.
Keep YouTubeTools beside the executable as well.

Phone control: connect phone and PC to the same Wi-Fi/LAN, then scan the TV QR.
Allow VietK through Windows Firewall on the private network if prompted.
The phone browser controls the actual PC queue and playback; music stays on TV.
Firefox/options > Ket noi dieu khien bang dien thoai shows LAN addresses.
Ngat dien thoai cu / tao QR moi revokes paired phones and generates a new QR.
This local remote replaces the unavailable cloud route; it is not the original
manufacturer website. Expressions/wishes require the local original supplement.
Phone footer and Firefox/options > Tat / Bat tieng control audio mute.
Mute preserves the volume setting; increasing/decreasing volume clears mute.
The original blinking mute/unmute TV graphics require the local supplement.

Main music source: YouTube (yt-dlp + native TV decoder)
- Search a song name, or paste a YouTube video link into the search field.
- Click a result to queue it; right-click and choose Hat ngay to play it first.
- Download progress and errors appear on the panel. Try again with Thu lai.
- Playback begins while yt-dlp/FFmpeg downloads and merges audio/video into
  a growing local MPEG-TS stream. A short initial buffer is still needed.
- Fully verified transfers are cached for replay. Incomplete files are not
  marked complete. Seeking ahead of downloaded data is not supported.
- Audio coverage is checked against video length. A transfer whose audio ends
  early retries through the regular downloader and resumes near the old position.
  Older caches are checked once; only incomplete audio requires another download.
- TV and panel preview share decoded frames directly; there is no snapshot
  timer or second decoder. Slow PCs can still drop presentation frames.
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
- Vui nhon opens the original expression page when the local supplemental
  resources are available. The eight factory images and paired WAV sounds use
  the original Vietnamese labels. Effects appear on both TV and panel preview,
  loop their sound at half volume, and disappear after six seconds. Karaoke
  playback continues. TV tab > Tat man hinh TV blanks the entire TV picture and
  panel preview while the song continues. Toggle it again to restore output.
  Loi chuc accepts a single-line message (30 characters), then sends a scrolling
  TV message after the original 1.2-second delay. The panel previews the same
  moving message. Original local graphics remain in the supplemental bundle.
  Exact Android font/line layout and peripheral lighting remain unverified.
- Home and More screens using extracted original resources.
- Song browser, Vietnamese keyboard, song grid and catalogue search.
- Native database and queue logic translated from the original app.
- Independent panel and TV windows with bundled Windows video decoder.
- TV video and overlays are also previewed inside the panel's black video slot.
- Original TV play/pause/replay/volume images and scrolling current/next song.
- Confirmed vocal/accompaniment switches show the original TV feedback image.
  Pause uses the original repeating indicator; other control feedback hides it.
- Original idle marquee remains visible when no song is selected. Additional
  scrolling text can be edited using Firefox menu > Chu chay tren TV.
- An actual Demo.mp4 beside the EXE, or in the app-state folder below, plays
  and loops while the queue is empty. Factory 60003950.mp4 is also accepted.
  The original factory dice video was recovered from assets/sdcard.zip inside
  the supplied APK. Supplemental original media remains local: the app finds
  original-ambience-idle.zip beside the EXE, in the app-state folder, or in the
  development workspace's .reference/recovered-original folder and imports it.
  The APK random background remains the fallback when that bundle is absent.
- Firefox menu > Chon video cho selects a local MP4 as the saved idle video.
  Replay keeps idle looping; changing volume preserves your selected video.
- Decoder failures return to idle and keep the selected YouTube song for retry.
- Selected queue: original panel layout, cut/delete, move to next, shuffle waiting
  songs and confirmed clear-all. Clearing while playing keeps the current song.
  Queue icons use the local original-resource supplement. The history tab stays
  empty for the local YouTube path, as in the firmware. Catalogue history remains
  pending. Hold a waiting song for half a second to drag it; the playing head
  stays protected. Download rows show real percentages when totals are known, MiB while
  streaming, and errors for retry, with the original progress-bar resources.
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
