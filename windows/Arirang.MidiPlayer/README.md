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
It previews English catalogue titles; other title encodings are not yet decoded.
The player does not yet decode ARVNKR/MULTAK proprietary song data. Background video reels
are not offered as selectable music. No full original-disc playback, Arirang
hardware sound equivalence, legacy lyric encoding equivalence, UDF-only disc
support, automatic disc downloads or mobile control is claimed in this build.

Library paths and error logs are stored separately in
`%LOCALAPPDATA%\ArirangMidiPlayer`. VietK settings are not used.
Logo source is recorded in `BRAND-SOURCE.md`. This is an independent development
project, not an official Arirang product.
