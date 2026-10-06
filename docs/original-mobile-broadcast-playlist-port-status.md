# Phone idle-playlist editing

The paired browser now includes `Video màn hình chờ`, with a local playlist editor and a separate add/search draft. It shares `OriginalBroadcastControl` and the persisted `localbroadcastlist.init` with the native desktop editor. Saves stage until parent confirmation and do not restart the current decoder. Nonlocal entries follow the existing original song-order route, including its real availability gates.

Decoded `SettingAction` commands 102/103 establish the protocol: `publishMusicMode` is a string; local records use integer `songid`, string `songname` and string `singername`. Saved replies resolve names from the database and preserve known duplicate IDs, skipping missing database records. Runtime status generates unique positive known IDs. Request metadata cannot rename database songs. Missing/null `publishMusicOfLocal` is an empty list, matching the original Gson getter; missing `songid` defaults to zero. Malformed objects/types are rejected before any order/save operation.

The paired local transport exposes:

- `GET /api/settings/broadcast-playlist`: original mode and generated configured runtime list fields. A USB path-only item retains the original `id: "-1"`, empty `name` fields; this does not expose a PC file path.
- `GET /api/settings/broadcast-playlist/list`: saved known entries, preserving duplicates for editing.
- `POST /api/settings/broadcast-playlist`: original `publishMusicOfLocal` request/reply fields. Lists use the same original string ID/type config format as desktop saves, plus `is_need_reply`. This route permits bodies up to 1MB instead of the generic 8KB action limit.
- `GET /api/settings/broadcast-playlist/search?q=...&page=...`: the original desktop broadcast name/initial/English prefix query, 50 SQL results per page, with existing visibility/PSL/post-LIMIT MIDI behavior.

Pairing, same-origin enforcement and revocation apply to all routes. The browser displays database text using `textContent`, preserves duplicate selections, stages add-screen confirmation into the parent draft, supports top/delete, cancel/back, empty-list saves and bottom-scroll pagination. Closing the settings discards unsaved work; asynchronous search/load responses are guarded so they cannot restore a dismissed draft. Paused playback and live volume remain independent of editing.

The browser layout is adapted for a phone, not a claim to reproduce an unavailable original mobile APK's visuals or manufacturer cloud/UDP transport. Original USB copy/delete, linked/cloud mode, random cached-song fallback and blank-input rank-file search remain pending. Runtime status describes the ported configured list rather than claiming the original random-fallback branches. Media whose real files/server access are unavailable cannot play simply because its ID is saved.

Verification is pending a GitHub Windows build. Core tests cover request fields, raw/saved/runtime distinctions, canonical database metadata and order callbacks. Real native HTTP tests cover authentication, origin, invalid bodies, search pages, large/empty lists and preserved live source/flow/volume. Phone-sized Chromium tests edit drafts through the actual native API, exercise scroll pagination and duplicate selections, verify cancel/back and saved persistence, reject HTML interpretation of metadata, and capture search/add/main/empty states. Physical Wi-Fi and manufacturer service compatibility are not covered by those tests.
