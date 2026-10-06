# Original app parity audit — 2026-10-06

This is a native Windows translation in progress, not a complete emulator or a
verified 1:1 app. This audit checks the visible controls and their current call
paths against the decoded firmware; it does not claim every original method was
executed on hardware. Playback/TV is the user's first priority.

| Area | Original implementation | Current Windows behavior / gap |
| --- | --- | --- |
| Header / logo | TopMenuBarView, module_top/index.html, PictureUpdateManager | Header was entirely omitted. Template logo and home-state dynamic icons now rendered. Original transparent VietK logo retained. Server branding overrides come from ui_request_logo_url_list; live override transport pending. |
| Home layout | layout_home_fragment.xml, item_home_new_adapter.xml, activity_main.xml | Original assets and tile order used. Confirmed home_content_view_marginTop is 0px; do not invent an extra header offset. Original firmware Roboto is now packaged and loaded. Android text padding/metrics, phantom preview and pixel comparison remain pending. |
| Home navigation | HomeNewAdapter, FragmentManagerUtil | Song name / More routes work. Singer, app manager, Mixcloud, YouTube and Soundcloud are missing. |
| More | MoreFragment | Source tiles rendered; destination features mostly missing. |
| Song search | SongSearchDAO and input widgets | Local metadata queries and Vietnamese input translated; thumbnails, complete pagination, filters, favourites and original dialogs incomplete. |
| Song ordering / queue | order policy, SelectedLocalListManager, SongDownListManager | SQLite workers and ordering rules implemented. No scanned karaoke storage is registered, so original no-storage gate rejects ordering. Feedback currently only traces; visible original toast missing. Queue dialog missing. |
| Downloads / server | DCDomain, KmDataCenterService, native sign-lib | URL decode, login state, permission/token handling and download queue translated and fixture-tested. Host still lacks live authenticated transport, device identity integration, file transfer and storage registration. No working music download claim. |
| Playback controls | BottomMenuBarView -> KmPlayCtrlUtil -> PlayerCtrlService -> KmPlayerCtrl | Pause/play, original/accompaniment, replay, next and volume lack native backend wiring. Current buttons cannot play music. Static state methods only validated with fixtures. |
| TV output | daulkmboxosdtv, OsdTvViewManager, KmPlayerCtrl, KmDurationPlayer | No second window or actual video renderer yet. This is the largest functional omission. Required port includes actual media paths, audio-track/channel semantics, completion/error events, lyrics and original OSD, not just a black window. |
| Ambience / effects / settings | corresponding dialogs, room/device services | Buttons/resources exist; workflows and hardware interfaces missing. |
| Header services | TopMenuBarView | Language/settings/network/shutdown dialogs and server/USB/AP observers missing. Restore visuals without claiming these handlers work. |

## Playback trace to continue

The controlling app is `dualkmbox`; karaoke playback is in the separate
`daulkmboxosdtv` APK. `hdplayer` is a different multimedia app and is not sufficient
to restore karaoke. `PlayerCtrlService` sends original command types: toggle 0,
play 1, pause 2, track 3, next 4, replay 5, tone 6, volume 7, stop 8, resume 9,
mute 10, microphone volume 11, immediate-next 12 and tone-with-tip 13.
`KmPlayerCtrl` delegates playback to `KmPlayerCtrlImpl` and `KmVideoPlayer`
(with `EvIjkPlayer` below it). `KmDurationPlayer` separately probes duration.
The controller also coordinates scoring,
recording, secondary presentations, resource availability and player events.
Port those call paths before advertising working playback or two-screen parity.

The authoritative player DEX was retained in run 37417466325 and checked against
the Java listing. `KmVideoPlayer.setAudioTrackInfo` reduces both metadata indexes
with Java's `% 2`; `AdaptedEvMediaPlayer.switchTrack` forwards the chosen original
or accompaniment index to `EvMediaPlayer.switchAudioTrack`. For two or more audio
tracks, that helper counts only track type 2 and selects the corresponding index
in the complete interleaved track array. For one audio track, it maps index 0 to
channel 1 and index 1 to channel 0, passing other values through. Therefore a
single-track song needs channel switching, not selection of a nonexistent second
audio stream. The Windows media adapter must implement both paths. Backend
channel-number semantics still need comparison against the original native
decoder; this research does not constitute working playback.

## Evidence retained locally

Original UI templates/layouts/values: `artifacts/original-ui-reference-v1/ui-reference`.
Decoded Java: `artifacts/native-source`. Original bytecode: `artifacts/original-bytecode-v6`.
New header packaging retains the template-entry hashes in `top-provenance.json`.
Windows captures prove that WPF renders the translated components, not that the
result matches a running physical device or that the services function.
