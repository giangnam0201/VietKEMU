# USB idle video copy and deletion

The desktop idle-video editor now follows the original `USBSetBroadcastDialog`
flow: confirmation, asynchronous copy, busy state with the USB-removal warning,
success/failure result, and a second confirmation to return to the editor.
Busy controls prevent closing the dialog during a copy. The USB row displays
`Demo.mp4` with a confirmed delete action; cancel leaves the copy intact.

Original `BroadcastListManager.SetBroadcastFromUsb` copies the source into
`kmbox/video/Demo.mp4`. The Windows port uses that same relative path inside its
state directory, rather than continuing to read the removable file. It checks
removable-drive roots for `Demo.mp4` first. The existing Windows MP4 picker
remains a platform adaptation when that file is unavailable. Copy success makes
the managed file the actual decoder/TV/preview source. Imported video persists
across restarts and does not require the removable source to remain connected.
Deleting removes only the managed copy, never a selected external source file.
The existing restore-original-video action also removes the managed copy.

The copy is staged and published atomically. Unlike the original failure branch,
which deletes the destination on copy failure, Windows preserves the previously
completed copy. Destination links are rejected. The loading indicator is a native
Windows indeterminate progress control; the original loading GIF is not bundled.
The remaining linked/cloud editor and mobile USB upload flow are not claimed
complete by this change.

GitHub verification is pending. File checks cover exact copy, source removal,
replacement, failed-copy preservation, temporary cleanup and delete scope.
Native interaction checks use an actual synthetic MP4 and shared decoded preview
frames after removing its source, copy cancellation/result confirmation,
delete cancellation/confirmation and missing-source failure. Screenshots cover
the confirmation, result, USB row, delete prompt and failure states.
