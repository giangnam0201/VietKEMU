# Original ambience port

The Windows `ambience_imv` command now opens the expression page. Original
supplemental pictures and sounds are imported locally, without uploading the
owner's recovered media. The page requires that local supplement.

Implemented from the decoded firmware:

- `AmbienceDialog`: 780 by 450 dialog, horizontal offset -95, close button and
  outside dismissal, purple background and selected-tab gradient.
- `SendExpressionView`: two rows in four columns, 176-wide columns, 120-square
  images, 150-high items, original Vietnamese labels.
- `AnimCommonUtils.scaleAnim`: scale 1 to 1.15 over 500 ms, no repeat, reset at end.
- `KmOSDMessageView.showTftpPic` and `km_msg_osdtv`: centered 318-square image,
  six-pixel frame padding, top margin 65, local default avatar when available.
- `KmAsyPlayer`: independent looping WAV playback at half volume, stopped with
  the picture after the 6000 ms expression timeout. Sending another expression
  replaces the previous picture and sound, resetting that timeout.

The TV and panel use the same overlay. Expressions do not replace the song
source or alter the selected queue.

The TV tab ports `DiscoMaskView` with its original 56 by 35 on/off images,
Vietnamese title/tip and 136-high settings row. `BlackScreen` covers the entire
video and OSD; audio/decoding continue. The same topmost cover appears in the
shared preview. Mask state and the selected expression/TV tab survive closing
and reopening the dialog within the current session.

Windows CI uses a synthetic red image and a one-second 1600 Hz WAV to verify
preview pixels, sound looping, timeout and concurrent karaoke decoding. These
fixtures verify the native path; they do not prove visual equivalence of every
factory asset or audio-device mixing on a physical Windows PC.

Windows CI clicks the actual TV-tab and toggle handlers, checks every pixel of
the TV visual and preview is opaque black, and checks stereo PCM and the song
clock continue. Unmasking preserves the running expression.

The wishes tab ports `SendBarrageView`: single-line 30-character input, empty
input ignored, nonempty input dispatched then cleared, and an empty preset list
as in the supplied APK. TV rendering uses the local ellipse/rocket images,
yellow 30px text at baseline 45, and the original 1280 by 64 message bitmap.

Scrolling duration and float motion are translated from `classes11.dex`:
`BaseDanmakuParser`, `DanmakuFactory`, `Duration`, `R2LDanmaku` and the two-item
collision check in `DanmakuUtils`. Messages start after 1200ms. Native rendering
retains one representative per row using `RLDanmakusRetainer.fix`, applies the
original traversed-line limit and overlap filter, and handles vertical overflow.
Duplicate messages are not merged. A growing message bitmap retains its width,
matching `BarrageManager`.

Core checks cover duration clamps, midpoint/end position and catch-up collisions.
Retainer checks cover different measured heights, row reuse, replacement before
filter rejection, the ten-line threshold, vertical overflow and allowed overwrite.
Windows checks click the wishes tab/send button and verify the delayed moving
message appears in the shared preview. Exact Android `StaticLayout` font metrics,
and screenshot equivalence remain unverified. Windows checks also submit twelve
identical messages and verify nine retained visible rows with the current 74px
line-height model, rather than merging duplicates or drawing overflow.

Still pending: complete Android barrage font/layout equivalence, room-state reset
integration, peripheral lighting, and complete original dialog
navigation. The expression page is a partial port, not proof of full fidelity.

The selected-song overlay now uses the original 563 by 596 right-aligned panel,
65px rows, playing-song highlight, two-digit waiting numbers, cut/delete/top
controls, and the original clear confirmation text and gradients. Clearing while
playing preserves the head; shuffle affects only the tail; top inserts at index
one. Changes persist without restarting the decoder. Windows capture checks click
top/delete/shuffle and both confirmation choices. Queue PNGs are imported from
the owner's local original-resource supplement, not published in the release.
The selected/history header switches reproduce `PlayListDialog`, including the
transparent selected tab and dark unselected tab. `SungListManager.addItem`
excludes YouTube, so that tab remains empty for the native YouTube queue. Native
catalogue recording/history integration remains pending.

Download labels now follow waiting/progress/error states with the original 40x3
bitmap bar. The native YouTube adaptation uses real byte counters; streaming
without a known total displays MiB, never a fabricated percent. Progress updates
keep the same row and scrolling position while the TV continues playing. Failure
keeps the song available for retry; validated completion removes its decoration.
Toolbar buttons use the APK's 25ms scale animation from 1 to .8 and back.
Windows capture checks both tabs and unknown/known/error/completed transfer views.

Queue reordering translates `SelectedLocalListManager.sortItemBySerial`, the
65px insertion calculation and retained target from `SelectedPullListView`, and
the 643x66, .8-opacity drag preview from `DragViewManager`. Index zero is protected.
The one-past-last marker remains allowed by the drawing calculation but rejected
by the sort manager, matching the source. Cancellation, tab changes and lost
capture discard the drag. Edge scrolling advances 20px per 100ms native timer
tick; equivalence to Android's smooth-scroll interpolation is not yet verified.

The native panel enables this path with a 500ms mouse hold. The decompiled APK
contains its long-click handler but does not establish where it is registered;
gesture activation equivalence remains unproven. Windows checks use controlled
drag coordinates, actual release/tab callbacks and a synthetic bitmap to verify
the marker renders above the preview, both move directions, head protection,
cancellation and edge scrolling. Original PNGs remain in the local supplement.
