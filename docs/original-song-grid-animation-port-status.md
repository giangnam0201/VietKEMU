# Song grid order feedback

`BaseSongForGridViewFragment.addSong` inflates a separate `fragment_song_recycler_gridview_item` before calling `PlayListManager.addSong`. The native song browser and singer-song browser now follow that path for ordinary order and priority actions.

The copy uses the current grid-item artwork and confirmed title/favorite state. It is attached to the full panel animation surface with hit testing disabled. `AnimOrderSongUtil.SongItemAnim` subtracts the original 30/56 base-middle offsets, moves toward (1000,550), shrinks to 0.2 and fades to 0.4 over 500ms. Horizontal motion uses acceleration; vertical motion, scale and alpha use the default cosine timing. The completed copy is removed. Concurrent taps retain independent copies.

The action still goes through the existing admission handler exactly once. Feedback is not evidence of queue acceptance; rejected requests do not gain confirmed queue membership. Preview, favorite and singer-name actions do not trigger order feedback. Optional clear-search-after-order preferences and other original settings remain pending.

Native Windows verification uses synthetic songs in a scrolled grid, checks source coordinates, rendered movement/scale/fade, independent copies, order/priority callbacks, original source retention, completion cleanup and favorite isolation. Builds run on GitHub; the app ZIP is not downloaded automatically.
