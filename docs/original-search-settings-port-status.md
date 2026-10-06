# Clear search after ordering

The port now persists the original `key_clear_search_text` preference, defaulting to false as in `GeneralView`. Its original Vietnamese label is exposed in the existing YouTube controls menu. This is a provisional access point; the original complete settings dialog is still pending.

`BaseSongForGridViewFragment.addSong` checks the preference and nonempty shared search at order time, then posts a 500ms callback before playlist admission. Native regular/priority song orders follow that rule. The callback resolves the current search screen, so it clears new edits and a newly displayed keyboard rather than a retained hidden view. Disabling the preference after scheduling does not cancel an existing callback, matching the source.

`YouTubeFragment.addYoutube` schedules the same clear after its playlist addition. Native YouTube additions use this rule, including additions through the paired phone route. Clearing the YouTube search refreshes its current results through the selected music provider. Preview, favorite and singer-navigation actions do not schedule a clear. Directory keyboards participate as current shared input targets without treating singer navigation as a song order.

Core checks verify default, persistence, the empty-input guard, exact delay and changed-text/preference behavior. Native checks exercise actual order and priority controls, edited input, navigation to another search screen, preservation of hidden input, YouTube/phone addition and refreshed search. Search responses in native checks are synthetic; no live YouTube search or complete settings-screen parity is claimed.
