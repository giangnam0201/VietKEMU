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
Verified cloud run: https://github.com/giangnam0201/VietKEMU/actions/runs/37394096536
passed core checks, original grid image packaging, Windows compilation,
catalogue/media import verification and the native grid capture. The original
catalogue remains byte-identical after the import checks.

`native-smali.yml` collects selected song/playlist bytecode with the SHA-256 of
its original DEX and the decoded smali. Run
https://github.com/giangnam0201/VietKEMU/actions/runs/37394384859 succeeded.
The bytecode confirms that `SongDAO.getSongById` returns the constructed song
after applying its default singer; JADX's Java output drops that return and
appears to return null. Future ordering/lookup ports must follow the bytecode
instead of reproducing that decompiler error.

`LocalSongDatabase.GetSongById` now follows that bytecode return and reconstructs
all 26 local fields, including the four singer/type/language IDs and the original
`Song` defaults. Null singers and the original literal `unknow` use the
Vietnamese anonymous-singer label; empty strings and `unknown` remain unchanged.
Search results apply the same default-singer rule.

`OriginalOrderPolicy` translates the bytecode's gates and local/download routing:
storage, linked-service pause/connection, region/cloud lock, duplicate handling,
normal-song repeat ordering and the 300-item capacity boundary. Existing Top
operations remain allowed at capacity. This policy is a prerequisite for the
queue backend; it does not itself enqueue, download or start playback. The
decompiled Java duplicates and inverts some capacity/duplicate branches, so the
policy follows the original smali control flow.
Verified cloud run: https://github.com/giangnam0201/VietKEMU/actions/runs/37394952347
passed the gate boundary checks and the real local lookup of song 101000,
including all singer/type/language IDs, metadata, constructor defaults and
missing-ID behavior, plus Windows build/capture checks. The selected-list store,
download queue and UI handlers still need translation. Original
`SelectedLocalListManager.topItemBySerial` preserves index zero (current song)
and moves eligible items to index one (next song); it does not insert ahead of
the current song.

`SelectedListStore` translates the selected-list schema additions and database
append, Top, sort, delete, identity/count and clear operations. It keeps the
legacy numeric `playType` separate from text `type`, saves all queue metadata,
normalizes a null customer ID to empty, and preserves the original count-based
sequence numbering. Delete-by-song-ID removes repeats and shifts once, as the
original SQL does; the store does not silently repair resulting gaps. Core
checks exercise file reopen, metadata with Vietnamese/apostrophes, ordering,
repeat deletion and failed-insert results. The Windows capture exercises the
actual seed schema in an isolated verification database.

Verified cloud run: https://github.com/giangnam0201/VietKEMU/actions/runs/37395825408
passed the selected-list persistence, ordering, repeat-deletion and failed-insert
checks, original asset packaging, native Windows compilation and capture. The
downloaded verification report confirms `originalSelectedListStorageVerified`
and `androidRuntimeUsed: false`. These checks establish storage behavior only;
they do not establish working panel queue actions or playback.

`SelectedPlaylistItem.Restore` now reconstructs runtime item metadata from these
rows: catalogue-backed normal/mdream/kmtrain/photomv types skip absent songs;
other types use the original synthetic Song defaults. Saved display metadata
remains distinct from catalogue metadata, and null getters normalize as in
KmPlayListItem. Scoring is enabled when Song's raw CanScore is zero, while the
selected table writes booleans as 1/0.
Scoring availability additionally requires a resolved subtitle/ERC path and
an existing file; DAO dispatch uses that runtime result, not just the raw flag.
Media selection prefers the first path
resolved by storage discovery, otherwise the first metadata entry. A filename
or a download URL alone never establishes a local file.

`OriginalSelectedQueue` translates append, existing/new/repeat Top, drag sort,
delete, identity and clear behavior, with explicit original DAO messages and
separate list/playback notification callbacks. Top and sorting protect current
index zero in memory. Original drag sort forwards zero-based indices to the
one-based DAO; this mismatch is preserved rather than silently repaired. The
manager defaults to clearing persisted entries on initialization. Even when
that flag is disabled, the bytecode's restore loop runs before IsInitialized,
so its Add method does not repopulate the active list on first initialization.
The store itself never clears data merely by opening the database.

`SelectedQueueDispatcher` now runs original DAO messages on one PlayListHandler
worker with its own SQLite connection, separate from panel reads/search. Messages
remain ordered and retain their runtime item references. Unknown messages,
including the original unused constant 31, are ignored. Explicit barriers allow
verification to wait for storage; quit rejects further posts and discards pending
messages as Android Looper.quit does. Worker failure reaches pending barriers
and prevents later posts instead of hiding lost operations.

The native host now initializes the selected queue/worker and connects confirmed
queue count and selected-song IDs to the bottom badge and grid colors. Start-play
requests remain diagnostic events until the playback backend is translated.
The Windows capture has an explicitly isolated backend fixture that adds/removes
an actual catalogue entry directly, bypassing admission to check persistence and
panel observers. It does not establish order-click admission or available music.

`OriginalOrderExecutor` now translates gate effects and backend routing, preserving
handled-versus-backend-success results. It constructs normal/MIDI items from the
resolved media path (case-sensitive .mid), applies positive singer IDs, builds
InfoId after early gates, dispatches local/download append or Top, and requests
rate synchronization only on successful eligible types. Order statistics are
requested even when an admitted backend operation returns false. These effects
follow DEX control flow rather than duplicated JADX branches.

The packager now inventories activity/service intent actions from all 23 original
APK manifests, with decoded and original manifest hashes, and preserves original
Vietnamese order feedback strings. Native grid order/Top requests now go through
report-table plugin routing, the original first-media NAS UUID check and the
order executor. Plugin selection retains the selected song/Top flag and launch
mode 2. The declaration inventory is firmware-default evidence; runtime plugin
installation/enabling and execution remain separate unfinished dependencies.

No karaoke volumes are registered until original storage discovery is ported,
so normal clicks fail the original storage gate. Feedback currently retains the
original text as diagnostic events: Android system-toast styling and order
animation still need translation. Report plugin execution, linked-service state,
download backend, demand-rate updater/stat observers and playback remain pending.
The host does not insert remote entries directly into the local queue or claim a
download occurred. An isolated Windows fixture checks admission against a real
catalogue song and the packaged plugin inventory without starting a download.

Verified cloud run: https://github.com/giangnam0201/VietKEMU/actions/runs/37398160720
passed the worker/FIFO/failure checks and native Windows build/capture. Its
isolated queue fixture verifies append/delete persistence, start-play request
counts and badge changes. The rendered MDH page includes song 101000 and visually
shows its orange selected title with badge 1; deletion verifies badge/store 0.
LocalFlag remains zero and scoring remains unavailable, so the fixture does not
claim downloaded media or successful playback. Report:
`artifacts/native-verification-v16/verification.json`; capture:
`artifacts/native-verification-v16/native-queue-observer-fixture.png`.

Verified cloud run: https://github.com/giangnam0201/VietKEMU/actions/runs/37397029606
passed selected-item reconstruction, score polarity/ERC availability, preferred
media selection, copy defaults, initialization semantics, append/Top/sort/delete
messages, duplicate identity, flow IDs and notification-order fixtures. Windows
compilation/capture also passed reconstruction of actual song 101000 and its
101000.MPG metadata from the imported firmware catalogue. The downloaded report
sets `originalSelectedItemReconstructionVerified` and retains playback, TV,
servers and full fidelity as pending/unverified.

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
