# Original singer directory

The native home singer tile (fragment 1) and singer-song category now open a directory translated from `SingerNameForRecyclerFragment`, `SingerTypeManager`, `SingerDAO`, `view_singer_recycler`, `fragment_singer_recycler_item`, and `RecyclerViewPagerAdapter` in the owner's decoded firmware.

Implemented:

- The APK's four Vietnamese country tabs and four sex options. Filters use `SongsterTypeID` groups, including China's combined mainland/Taiwan groups and Vietnam/Western contiguous type triplets. The all-country group excludes type 53 as in the source.
- Eight 180×212 singer cards in four columns and two rows. The horizontal Android grid fills each column first. Portraits occupy 180×180; labels occupy 180×32.
- Ranked spelling search, English-name substring matches, 80-row SQL batches, and prefetch two visible pages before the loaded boundary. Count/list empty-spelling behavior remains distinct, as in the source. Core also preserves name prefix/substring query branches for subsequent input-mode work.
- The existing shared Vietnamese keyboard and live TV preview. Country/sex changes reset search. Each directory card opens its exact singer ID, preserving identity even when names repeat. Back restores the same directory view, filters, page and input.
- Home/back navigation, previous/next controls, horizontal drag and mouse wheel page selection, and the original empty-result text.

Verification uses synthetic SQLite rows and real native routed controls. It checks country/sex selection, eight-card geometry and column ordering, 80-row pagination/prefetch, duplicate-name identity, shared keyboard and retained Back state. Reports/screenshots contain synthetic artists only.

Portrait loading now follows the original non-linked storage branch: search the translated active music-storage root's `kmbox/picture/<singer name>.jpg`, then request the default `SingerImageManager` URL using the singer's picture resource ID. Validated image bytes persist in the Windows cache; failures retain the original placeholder. Four requests may run concurrently, and recycled cards cannot receive a previous singer's result. This loader sends no device identifiers. The original signed `bs_picture_url_head` discovery request, linked-remote GBK-name branch, additional mounted-volume discovery and live vendor image availability remain unverified/unported.

A separate credential-free HTTP probe from the owner's PC returned status 200 and decoded one 140×140 JPEG from the original default picture endpoint. This establishes availability of that one resource only; it does not verify all singer IDs, the signed URL-discovery path or live Windows rendering. The probe and image remain private under `.reference/singer-picture-probe`. GitHub's native checks use synthetic image responses and do not contact that vendor endpoint.

The owner's private supplemental bundle now includes the original popup background/selection nine-patch PNGs and pager icons. `OriginalNinePatch` reads the compiled PNG `npTc` divisions and preserves fixed corner/edge regions while stretching the indicated spans. These recovered images remain private; CI verifies the renderer against synthetic corner/edge/center pixels. Builds without the private bundle retain explicitly provisional popup/pager fallbacks. Native tests cover local image precedence, exact default URL, persistent cache, invalid-image placeholder retention and stale asynchronous results; they do not contact the vendor image service.

Singer cards now use the original 0.97 press scale; pager arrows use 1.2. Both animate over 25ms using Android's default cosine interpolator. Release, drag cancellation and focus loss restore the card; Windows pointer-leave/unload handling prevents retained pressed visuals. Zero results display 0/0, matching `ChangePageView.initChangePageState` and `PageChangeManager.initPageChangeViewState`.

The previous page-number jump-dialog note was incorrect: `ChangePageView` installs listeners only on previous/next arrows; its current/total labels have no click handler. `SingerNameForRecyclerFragment.startAnim` declares entry/fade helpers but has no call site in the inspected class, so those helpers are not treated as evidence of active entry behavior. Remaining fidelity work includes server-configured portrait URL discovery, linked-remote image loading, Android pager motion and alternate input modes. The owner-local recovered `defaultsmall.png` is used when available. Missing portraits are not replaced with invented singer photographs. This remains a partial port.

No build download to the owner's PC is required for GitHub verification.
