# YouTube as the primary music source

The user selected YouTube as the main source, then requested yt-dlp and a clean
native TV output rather than an embedded YouTube player. This is an explicit
departure from the original VietK music server and catalogue. It does not restore
VietK's original recordings, entitlement, vocal-track metadata, scoring or complete
UI fidelity.

The app now opens its native YouTube panel. Song-name and original YouTube home
tiles open the same panel. yt-dlp supplies public search results (no Data API key),
and pasted video links work through the same queue. Cards use the original
230-by-195 geometry, the firmware background, original YouTube icon, VietK header
and existing bottom controls. The panel now includes a touch keyboard and a
live shared-frame preview of the TV video with its overlays; all menus and pixel
parity remain unfinished. Results now use the original two rows of three cards per page,
up to 48 results per search, stretched 230x140 thumbnails, centered 220x45
titles, and queued-title color `#ffffe761`. The firmware's Roboto is used.
The verification command checks six-card pagination, partial final pages and
page bounds, and saves a separate clearly labeled layout-fixture screenshot.
Those fixture cards never enter normal app results or the download queue.
Queue IDs and displayed metadata persist in `youtube-queue.json`.

The TV overlay uses the decoded TV APK's play, pause, replay and volume images
and layout dimensions. Transient control feedback lasts six seconds as in the
original source. The VietK logo and current/next-song scrolling text appear on
both the TV and its panel preview. Mobile connection QR and ambience effects
remain pending.

Idle playback prefers the original separate `Demo.mp4` broadcast path. Place
the actual demo clip beside `VietK.NativePort.exe` or at
`%LOCALAPPDATA%\VietKNativePort\Demo.mp4`. It loops on startup and after clearing
or exhausting the queue, and appears in the panel preview through the same
decoder. The original demo bytes have not yet been located in the supplied
exports. The APK identifies its factory idle file as `/kmbox/resource/60003950.mp4`;
that filename is also accepted beside the EXE or in the state folder.
Until either clip is supplied, the original APK `assets/random_bg_default.mp4`
is bundled as an idle background fallback, with its SHA-256 checked against
the original APK manifest. It is not proven identical to the factory idle clip;
the grading animation is not substituted. CI verifies
the idle decoder and loop with an explicitly identified test fixture.

yt-dlp fetches video plus audio at up to 1080p. FFmpeg merges/remuxes with no
requested re-encode; ffprobe checks for both audio and video. Only a successful
process result and a completed verified file create a cache marker. Cancellation
kills the process tree and generation checks prevent an old fetch from starting
after next/clear. Completed files are played by bundled libVLC in the independent
TV window. The main panel now uses yt-dlp's FFmpeg downloader with H.264/AAC
selection and MPEG-TS output. A loopback-only HTTP server serves the growing
cache file, waiting at temporary EOF for more bytes. Playback begins after a
256 KiB initial buffer; it does not wait for the transfer to finish. After a
successful producer exit, ffprobe checks both streams before a completion
marker is saved. Next/clear cancels the producer and closes the stream. Failed
transfers keep the queued song for retry. Completed cache entries avoid another
download. Seeking beyond downloaded data remains unsupported.
No embedded player or WebView is used.

TV and preview share one libVLC decoded frame buffer and one WPF visual tree,
including the same overlays. Frame callbacks replace disk snapshots and the
500 ms timer; the UI coalesces frames when it cannot keep up. Native tests check
playback before a delayed writer finishes and more than ten frame updates in
one second, alongside real decoded video and PCM checks. The public YouTube
probe now exercises the progressive path; a server rejection is reported as
unverified, independently of deterministic local-stream verification.
Browser authentication is enabled only through an explicit Firefox selection.
One queued song is fetched at a time. Next cancels the active
fetch and advances; failures remain visible for retry/removal. Persistent queue
restoration requires the user to press retry to start playback.

The download tool may fail because of YouTube restrictions or extractor changes.
An optional Netscape cookie file explicitly selected in the panel can supply the
user's own YouTube login. The app records its path in `youtube-settings.json`.
The Firefox button instead enables `--cookies-from-browser firefox` for search
and download subprocesses. yt-dlp reads the local Firefox profile session when
making the request; the app stores only the selected mode, not cookie values.
No cookies or credentials are copied to GitHub. Public-only mode is the default,
and the panel can clear either login option. Selecting a file disables Firefox
mode; selecting Firefox clears the file selection. The verification command
`--verify-youtube-firefox URL OUTPUT` uses Firefox only on an explicitly opted-in
local PC; the GitHub live probe continues to use public-only mode.
This integration does not bypass account entitlements, DRM or other access
restrictions. It does not reuse VietK hardware IDs or tokens. No API key
is required because this implementation uses yt-dlp search rather than the
YouTube Data API. This implementation should not be described as an official
YouTube API player.

GitHub bundles official yt-dlp 2026.08.19 (including EJS), Deno v2.9.7, and the
official yt-dlp FFmpeg-Builds Windows archive. Package provenance retains actual
executable hashes; upstream checksums are verified for yt-dlp and FFmpeg. Licenses
and upstream source links are included. The tools can be updated independently
of the native app. See [yt-dlp](https://github.com/yt-dlp/yt-dlp) and its
[EJS requirements](https://github.com/yt-dlp/yt-dlp/wiki/EJS).

Core checks cover URL normalization, exclusion of playlist parameters, invalid
hosts, Unicode metadata and malformed search entries. Existing Windows decoder
checks exercise real decoded video/audio. A separate live CI probe attempts the
public Blender demonstration video used in yt-dlp's own README; its external
service failure is recorded separately and does not block publishing the build.
A green build alone must not be reported as successful YouTube fetching. Inspect
`youtube-verification.json` and its native-frame snapshot for live evidence.

On 2026-10-06 the operator explicitly authorized use of their Firefox session.
Local yt-dlp tests then searched for `Mộng dưới hoa karaoke` and downloaded two
real files: Blender's demonstration video `YE7VzlLtp-4` (21,781,692 bytes) and
the public karaoke upload `BvXgbkHNJ6o` (9,469,509 bytes). Both downloads merged
video and audio into MKV using stream copying. FFmpeg decoded each file's video
and audio, and the existing bundled x64 libVLC decoded 41 and 45 video frames
respectively through video callbacks. Reports and frame captures stay under
ignored local artifacts. This proves local authenticated fetching and decoder
compatibility for those two files, not universal availability, physical speaker
output, complete WPF queue/TV integration or original VietK content parity.
The Firefox-enabled Windows build is run 37435928828, commit cf9f13b; it passed
the core, render and native playback checks and published the Windows test ZIP.
