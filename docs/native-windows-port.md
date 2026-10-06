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
artifact. The original bottom bar now has its template positions, Vietnamese
labels, original converted icon images, paired pause/vocal-state visibility and
500 ms click guard. Playback commands remain requests without a backend;
the host handles the Home request only. Queue count is still the initial zero.
`FragmentHistory` translates the original history and back rules, including
clearing history on Home and disabled online-service fallback.

The `MoreFragment` screen is translated from `layout_more_fragment.xml`,
including the relative positions of all nine tiles, Vietnamese labels, original
artwork and the Back button's gradient/layer drawable. The component host
connects Home -> More -> Back/Home. More's feature actions (password guards,
room/QR services, catalogue management and the individual destination screens)
remain pending; their presence is not a claim that those features work.

The host does not yet include the top bar, complete screen navigation,
Phantom video, television UI, activation, playback or server operations.

Home's song-name tile now opens the native `SongNameFragment` empty-state path
and returns through Back or its category title. The browser uses a separate
persistent local database, not the whole catalogue as available songs. Its
Vietnamese keyboard translates the original four rows, symbol-page toggle,
pressed outline, space/backspace/clear behavior and delayed spelling queries.
The original empty-message and YouTube button are present; YouTube navigation,
Thai/handwriting mode actions, song item handlers and media-index
import remain pending. Phantom remains an empty original-sized video region.
This is still a development component, not a working karaoke release.
Verified cloud run: https://github.com/giangnam0201/VietKEMU/actions/runs/37392895683
renders both initial and typed-search states and checks that three rapid key
inputs emit one delayed query against the original empty local seed. Character
arrays and their referenced constants are resolved from the decoded APK source.
Fonts, inherited Android button metrics and TileButton depth animation still
need fidelity work; a rendered capture alone does not prove pixel equivalence.

`WholeCatalogue` translates read-only `WholeSongDAO` count, existence, ID lookup
and remote-metadata checks. It reads the unchanged original database. An online
metadata flag does not establish a live server or available song file.

`SongSearch` translates `SongDAO.getSongsOrderBySpellLocal` and
`getSongsOrderByNameLocal`: spelling/name matching, word-length and language
filters, PSL exclusion, local/connected visibility, prefix and language ranking,
and SQL pagination followed by the original MIDI exclusion. It requires the
local-state database with `IsLocalExist`; it is not yet connected to the panel's
song screen. The firmware's seed local database needs its original schema
upgrade and media-index import before it can supply that state. The schema
upgrade is now translated by `LocalSongDatabase`: it copies the original
`kmbox.jpg` to separate writable state once, adds the original missing columns
and preserves that state on later starts. The supplied seed has zero songs;
media-index import and the panel connection remain pending. The original seed
is packaged with its APK hash and never upgraded in place.

`LocalSongDatabase.ImportOnlineCatalogue` and `ImportOnlineMedia` translate
`SongIdDAO.saveOnlineToSong`/`saveOnlineToMedia`: insert eligible remote entries
without replacing existing rows, set their local flag to zero and volume UUID
to empty, and retain the original media metadata. `GetMedia` translates all
17 `MediaDAO.getMedia` fields, including volume, original/accompaniment tracks
and the MD5 metadata. The imports are not yet wired to the original storage
traversal/update lifecycle. They never change data-centre connection state or
download music. The capture checks exercise them in a separate verification
database using the supplied catalogue; that state is not the interactive app's
database.

`SongGrid` now renders the default three-column cells from the original layout:
original fallback image, centered green song name with a white 4px stroke,
singer strip and remote/preview/collection/top icons. Source bitmap hashes and
density-derived icon sizes are packaged in `song-grid.json`. Confirmed local
flags control remote/preview visibility; whole-catalogue lookup models carry
no local flag and are rejected as grid state. Scrolling the current page works;
the original seekbar, subsequent page loading, thumbnail URL/cache loading,
singer spans and item action backends remain pending. A verification-only
connected query renders real imported metadata in `native-song-grid-fixture.png`;
it does not set the interactive app's connection or availability state.

Verified cloud run: https://github.com/giangnam0201/VietKEMU/actions/runs/37391148804
passed the search fixture checks, original resource packaging, Windows build,
and component capture checks. Search fixtures are synthetic and verify query
rules, not the availability of music or a working server.

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
