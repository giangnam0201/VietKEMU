# Original singer directory

The native home singer tile (fragment 1) and singer-song category now open a directory translated from `SingerNameForRecyclerFragment`, `SingerTypeManager`, `SingerDAO`, `view_singer_recycler`, `fragment_singer_recycler_item`, and `RecyclerViewPagerAdapter` in the owner's decoded firmware.

Implemented:

- The APK's four Vietnamese country tabs and four sex options. Filters use `SongsterTypeID` groups, including China's combined mainland/Taiwan groups and Vietnam/Western contiguous type triplets. The all-country group excludes type 53 as in the source.
- Eight 180×212 singer cards in four columns and two rows. The horizontal Android grid fills each column first. Portraits occupy 180×180; labels occupy 180×32.
- Ranked spelling search, English-name substring matches, 80-row SQL batches, and prefetch two visible pages before the loaded boundary. Count/list empty-spelling behavior remains distinct, as in the source. Core also preserves name prefix/substring query branches for subsequent input-mode work.
- The existing shared Vietnamese keyboard and live TV preview. Country/sex changes reset search. Each directory card opens its exact singer ID, preserving identity even when names repeat. Back restores the same directory view, filters, page and input.
- Home/back navigation, previous/next controls, horizontal drag and mouse wheel page selection, and the original empty-result text.

Verification uses synthetic SQLite rows and real native routed controls. It checks country/sex selection, eight-card geometry and column ordering, 80-row pagination/prefetch, duplicate-name identity, shared keyboard and retained Back state. Reports/screenshots contain synthetic artists only.

Remaining fidelity work: vendor portrait discovery/download, original nine-patch popup artwork, animated page transitions, page-number jump dialog and alternate input modes. The approved public resource bundle does not contain every directory asset; the owner-local recovered `defaultsmall.png` is used when available. Missing portraits are not replaced with invented singer photographs. Popup colors and pager glyph fallbacks are provisional and must not be presented as a complete 1:1 port.

No build download to the owner's PC is required for GitHub verification.
