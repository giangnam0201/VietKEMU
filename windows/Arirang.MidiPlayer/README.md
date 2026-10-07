# Arirang MIDI development build

Extract the complete ZIP, then open `Arirang.MidiPlayer.exe` on Windows x64.
This independent development app has Arirang branding and two windows: song
selection/control and television output. The panel preview uses the live TV
visual directly. It is not a screenshot poller or a second MIDI/audio player.

Import standard `.mid`, `.midi` or `.kar` files. Double-click a song to play it,
or select songs and press `+ Đã chọn` to queue them. The selected queue supports
play, removal and automatic advance. Pause/resume, replay, key changes, volume,
tempo and seek are available. Embedded MIDI/KAR lyrics appear on the TV with
timed highlighting. Double-click the TV to toggle full-screen mode. A Windows
MIDI synthesizer/audio device is required; it opens only when playback starts.

`synthetic-audio-test.kar` is a generated two-note audio/lyric test, not an Arirang
song. Use it to check this PC's MIDI output. Build verification tests scheduler
messages with a recording output; it does not establish audible sound on your PC.

The Arirang disc tab reads the public Maseco archive catalogue and item file
lists on demand. The ISO checker reads ISO9660 directories without loading a
multi-gigabyte image into memory, and detects mislabeled RAR files. These tools
also support reading the English metadata block in extracted `hk2/INFO.DAT` files;
the reader is checked against 3,795 records in the archived Volume 48 index.
Song numbers and playable music extents are not inferred from title records.
For supported MULTAK/MASECOS4 discs, the ISO checker reads actual device codes
and resolves their music pointers using the container's song-block table. This
is checked against all 22,900 Volume 40 records and three original song headers.
It previews English and Vietnamese catalogue titles. Vietnamese character
identities were checked against the original Volume 40 font bank; verification
decodes all 4,861 Vietnamese entries, including accented characters the historical
converter misidentified. Other language encodings are not yet decoded.
For a supported simple sample record, the inspector also locates its channel
streams and lyric/music boundary. Original five- and eight-channel Happy Birthday
records verify this structural reader. Corrected descriptors supply pitch widths
and base pitches. The inspector can reassemble compact streams and expand
supported events into ticks; complete records verify 1,692/1,001 events and
their opening melody pitches.

Use `Nhập MULTAK ISO (thử nghiệm)` to add mapped English and Vietnamese songs from a local
supported MULTAK/MASECOS4 ISO to the same library and queue. Songs are decoded
on playback rather than copying a whole disc. The experimental clock uses the
reference's tempo commands and 24 ticks per beat. The two complete-record checks
calculate 62.604167 and 43.1875 seconds; timing on original hardware remains
unverified. Unsupported record formats and playback commands produce an error.
This preview uses Windows MIDI instruments. Supported ASCII original lyrics
now appear in two independently highlighted TV rows using the note tempo map.
The two original records verify 264/182 lyric characters and all 34 phrase
starts against their melody notes. Voice and formatting bytes do not add delays.
Unsupported lyric encodings keep audio playback available with a visible notice.
Vietnamese lyric encoding, rendering original fonts, countdown graphics and
the original wave bank remain unfinished.
ARVNKR proprietary songs are not playable. Background video reels
are not offered as selectable music. No full original-disc equivalence, Arirang
hardware sound equivalence, legacy lyric encoding equivalence, UDF-only disc
support, automatic disc downloads or mobile control is claimed in this build.

Library paths and error logs are stored separately in
`%LOCALAPPDATA%\ArirangMidiPlayer`. VietK settings are not used.
Logo source is recorded in `BRAND-SOURCE.md`. This is an independent development
project, not an official Arirang product.
