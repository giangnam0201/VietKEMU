# Music server and Windows output port

The Windows host now connects the translated data-center client and media URL
resolver to HTTP file transfer, a persistent completed-file cache, download and
selected queues, and a separate native TV decoder window. This remains a partial
manual port. A live login accepted this PC's actual BIOS serial and physical
adapter MAC and returned a token, validation code and service URL. This does not
prove music-library access: the approved request for song 101000 returned HTTP
200 with `errorcode: "0"`, an empty error message and no `medialist`. A retry
matched the APK's field order and produced the same result without a redirect.
No production song was downloaded. Encrypted karaoke playback remains unverified.

## Original protocol evidence

- `KmDataCenterService` resources specify `http://viet.duochang.cc/login`, signing
  version `1.0` and protocol `1.9.1`.
- `BaseDataCenterCommu` submits URL-encoded form field `body`, containing JSON.
  Its device login is `bs_device_login`; media lookup is `sn_song_media_list`.
  Token, validation code, command filter and returned service URL retain the
  translated original client's rules.
- Original `BoardInfo.getSerialNumber` reads Android `Build.SERIAL`. A firmware
  archive does not establish an individual device's hardware serial or MAC.
  This host supplies no fabricated identity, token, or authentication success.
- The default firmware User-Agent is `KTV-Plus/1.2.b57/1.9.1/android/1.0.0`:
  original build properties, protocol resources and data-center APK version,
  rather than the OTA archive's display name.
- Native signing research established modified UTF-8, `chipId:salt`, a 63-byte
  snprintf limit and lowercase MD5. Android APK certificate admission is a
  separate platform mechanism; reproducing the hash does not establish server
  authorization or complete native-library parity.
- `LocalOnlineSongManager.downloadVideo` uses `<song ID>.ts.tmp`, or `.mp3.tmp`
  when the returned URL contains `.mp3`, and requires 524288000 bytes of free
  cache space. The Windows adapter streams bytes, checks known content length,
  removes failed temporary files and promotes completed files before playback.
  Initial HTTP opening now follows `HttpFile.open(uri, 0, 3)`: three attempts,
  immediate stop on 404, `Range: bytes=0-`, `Accept: */*` and identity encoding.
  Its transfer buffer is the original 32768 bytes. After failed opening, the
  original closes its connection before reading the status, so the resulting
  AppDownItem error is 1004 even on 404. Separate file-write retries, downloader
  User-Agent parity, NAS handling and all error transitions remain incomplete.
- Completed downloads update the local-song flag, move matching download entries
  to the selected queue, and start the next download. Cache metadata contains the
  returned vocal/accompaniment indexes and per-song gain. Windows paths are
  registered after actual completion, never inferred from catalogue filenames.
- Download failures mark every matching queue row and notify observers before
  recovery. Non-interrupting errors retain the active song/state for the original
  one-second delay, then reset and advance one queue index. Storage error 1016
  resets without advancing; network error 1015 stops selection. Song failures no
  longer block the worker behind a Windows modal dialog. The original queue
  screen and its inline error text still need a full UI port.

## Configuration and testing

After first launch, `%LOCALAPPDATA%\VietKNativePort\music-server.json` contains
`LoginUrl`, `ChipId`, `Mac`, `UserAgent` and optional `MusicDirectory`. Supply
device information the original server accepts and restart. Missing identity
prevents login; rejected requests display the real failure. The catalogue is
metadata, and online search visibility requires a successful original login.

The default Windows cache is `%LOCALAPPDATA%\VietKNativePort\music`. Its completed
file registry is `music-cache.json`. This is a Windows storage adapter, not a
claim that Windows disks are original Android scanned volumes.

GitHub Actions runs actual HTTP form/header and byte-transfer tests, including
truncated-file rejection. The Windows verification also exercises a clearly
identified loopback login/media server through the cache and queues into the
real decoder, and checks decoded frames and PCM. Those fixtures are not shipped
as songs and do not prove access to VietK's production music library.

Build run `37422777784` (commit `5243e80`) passed signed loopback login, media
lookup, download, cache and queue promotion through the real Windows decoder.
It also verified decoded video pixels, left/right and multiple-stream PCM,
pause/resume, seek, replay, volume amplitude and decoder completion. The native
test ZIP is available under `artifacts/windows-test-v18` locally. These results
are distinct from the unsuccessful production media lookup described above.

`tools/probe_music_server.py` provides an operator-authorized live diagnostic
using real supplied identity and server-issued authentication. Identity and
session records stay in ignored local artifacts and must not be committed or
uploaded. Its public report records whether a media list and completed download
actually exist; server code zero alone is never reported as successful playback.

For local video testing without server authorization, launch
`VietK.NativePort.exe --play-media "C:\path\video.mp4"`. This is a developer probe,
separate from the original song browser.

## Output limitations

The decoder is bundled Windows libVLC, using the selected original player state
and control rules. Native research maps a one-track song to duplicate-right or
duplicate-left audio, while multiple tracks use the corresponding audio stream.
Per-song gain follows `getMediaVolume` with the default HDD scale of 1; Android
system-volume curves and all broadcast/microphone paths remain incomplete.

The original grading video is from **dualkmbox**, and the original TV layout is
from **daulkmboxosdtv**. A decoded grading frame is evidence of video decoding,
not a downloaded karaoke song or a complete original TV interface. Lyrics,
scoring, encrypted media, all original overlays and full UI/UX parity remain
unfinished.
