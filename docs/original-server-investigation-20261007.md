# Original server investigation: 7 October 2026

App feature upgrades are on hold while original music access is investigated.
The server experiments remain outside the Windows application and release build.
No device identities, session tokens, cookies, embedded application keys or raw
service configuration responses were published in this investigation.

The approved original host accepted device login but returned
`sn not in devices_table` for cloud status. Seven actual catalogue songs returned
no media list or video URL. Song 101001's metadata request still worked. This does
not establish that all music has been deleted; accepted login also does not
establish music entitlement.

Tracing the original downloader confirmed that it consumes the first URL from
`sn_song_media_list`. Its separate micro-server playback path uses a configured
server IP, HTTP port 9167 and UDP song-URL messages. It is a local server path,
not a recovered public cloud fallback. The original read-only
`bs_client_custom_config` query for `micro_server` returned HTTP 200 with an empty
body and supplied no server configuration.

Native constant extraction from VietkService recovered the actual secondary
service base `http://api.vktv.vn:8540/v2/`. These routes handle service settings and
YouTube/SoundCloud/Mixcloud. They do not establish an alternative native karaoke
catalogue-download API.

The manually triggered [public diagnostic run](https://github.com/giangnam0201/VietKEMU/actions/runs/37547799896)
tested the same source-backed public routes from GitHub without credentials:

- Settings returned HTTP 200, with YouTube downloads, offline playback and CDN
  disabled in the returned configuration.
- Both original YouTube link cache modes returned application code 500 with
  source Worker and an upstream 403 Forbidden, matching this PC's result.
- The original CDN link route returned HTTP 500 without a usable URL.
- The separate `https://ktvapi.duochang.cc/ktv_compatible/` service responded to
  GET from GitHub, unlike the local TLS timeout. Its original client uses a
  distinct login/session and routes remote configuration and public-playlist
  commands there; the karaoke media lookup still uses the ordinary data center.
  Reachability is not evidence of an authorized song-download path.

Three report tests passed locally and on GitHub. They check that private settings,
arbitrary server errors and signed URL values cannot enter the published report,
and that a returned URL or HTTP success is never reported as verified playback.
Only a small redacted diagnostic JSON was downloaded; no Windows build was
downloaded. No music file was downloaded or verified as playable.
