# Arirang MIDI branch status

Development lives on `codex/arirang-midi`. `main` retains the VietK app.
The Arirang workflow builds only `Arirang.Core` / `Arirang.MidiPlayer`, with an
independent executable, settings directory and official logo. Its initial
[Windows build](https://github.com/giangnam0201/VietKEMU/releases/tag/arirang-midi-test-37571192831)
passed parser/scheduler/disc checks and native two-window rendering. Panel and
TV screenshots were inspected; the preview displays the TV visual directly.
The build report explicitly does not verify audible hardware output or original
proprietary song playback.

The final [development build](https://github.com/giangnam0201/VietKEMU/releases/tag/arirang-midi-test-37572148588)
passed [run 37572148588](https://github.com/giangnam0201/VietKEMU/actions/runs/37572148588),
including the real 3,795-record index check, selected-queue interaction,
additional timing checks and both native windows. Its final panel screenshot
and JSON reports were inspected. Only verification assets were downloaded
locally; the Windows app ZIP remains available for the user to download.

The standard MIDI engine supports merged format-0/1 tracks, tempo maps, SMPTE
timing, channel events, full system-exclusive packets and embedded KAR lyrics.
Windows WinMM supplies MIDI output, opened on first play rather than startup.
Checks exercise pause, seek/resume, transposition excluding drums, channel gain,
completion, malformed input and running status across metadata. This does not
establish identical instrument sounds to an Arirang hardware synthesizer.

## Actual INFO.DAT metadata

The public Volume 48 `hk2/info.dat` is 1,320,466 bytes. Independently reading its
English record block at `0x89AB3` produced 3,795 title/artist/composer records,
ending at `0xB27AE`. The older researcher's hardcoded `0x8BAFF` start skipped
the first 186 records in this volume; an independent full byte scan confirmed
the complete block. The new C# reader searches for a validated contiguous block
rather than using those offsets as universal constants. The GitHub workflow
fetches this small public input privately, checks the reader and publishes only
a count/status report. It does not redistribute the index or extracted list.

Record-structure research was cross-checked against Phuoc Can HUA's original
[Karaoke Engineering discussion](http://karaoke-engineering.44.s1.nabble.com/Understanding-the-files-on-the-DVD-of-Arirang-Karaoke-hk2-amp-midi0-td224.html)
and its `ListInfoDat` source attachment. That attachment was inspected privately,
not executed or bundled. The implementation is new bounded parsing code. Other
language blocks, legacy character encoding, device song-number mapping and
music-container decoding remain separate requirements. Metadata entries are
never silently turned into fake playable songs.

The next original-format work is to map an actual song number to its music
extent in `ARVNKR` or `MULTAK`, then decode notes, timing, instruments and lyric
cues. A volume/background MP4 is not a replacement for those musical events.

## MULTAK original-sample investigation

The original researcher's [raw song attachment](http://karaoke-engineering.44.s1.nabble.com/file/n297/30093_raw.zip)
contains a 4,148-byte raw/unmasked pair. A new independent, bounded research
tool (`tools/arirang_multak_research.py`) recovered its 33-byte title field at
header offset `0x30`. The mask byte is `0xE2`; applying the historical XOR
operation after the four-byte prefix reproduces the supplied comparison file
exactly. Three local checks passed, including malformed boundaries and a
reference mismatch. Inputs and decoded text remain in ignored private storage;
the tool's report contains only counts and verification flags.

This confirms the title-header transformation for that sample only. Neither
member contains a Standard MIDI `MThd` header. The remaining header bytes,
compact music event structure, timing, instruments and actual song-number
mapping are not established. In particular the attachment filename must not
be assumed to be a playable device song ID: the researcher's cutting routine
selects sequential records. The title mask must not silently be treated as a
verified music mask. This research tool is separate from the Windows player
and cannot import the proprietary song as playable music yet.

## Volume 40 music storage pointers

Bounded HTTP range reads of the public [Volume 40 ISO](https://archive.org/details/midi-vision-karaoke-vol-40)
identified both original music containers in its ISO9660 directory. The music
header declares 22,910 table slots: 10 null, 17,111 pointing into MULTAK.DAT and
5,789 into MULTAK.DA1. All non-null offsets fit the declared container sizes.
The independently written `MultakIndex` reader validates the `multak3.3` header,
complete table, sector fields, storage selector and extent bounds. It preserves
uninterpreted flags and explicitly names table positions rather than song IDs.
The Windows ISO inspector now reports these counts for supported local discs.

The [original author's pointer description](http://karaoke-engineering.44.s1.nabble.com/Arirang-MIDI-Karaoke-DVD-storage-file-struct-and-MP3-Extraction-td467.html)
was checked against five small original record samples. Four begin with the
expected `00 00 OK` song header; the second table slot's sample does not, so
valid pointer arithmetic alone is not treated as proof of a playable song.
Music decoding and song-number mapping remain unverified. GitHub checks fetch
only a 128 KiB public TOC range and publish aggregate verification results;
original music bytes remain excluded from releases.

This change passed the [Windows build and real-data checks in run 37573331056](https://github.com/giangnam0201/VietKEMU/actions/runs/37573331056).
The [updated Windows test build](https://github.com/giangnam0201/VietKEMU/releases/tag/arirang-midi-test-37573331056)
includes the supported MULTAK ISO inspection. Its downloaded verification report
matches all four expected table counts and the independently observed first
and last storage offsets. Standard MIDI parsing, selected queue and two native
windows also passed; hardware audio and proprietary music playback remain
explicitly unverified.

A separate six-sample comparison of historical unmasked records found a
consistent candidate music boundary: `44 + littleEndianUInt32(sample[48..52])`
is preceded by `1A FF` and followed nearby by a repeated `8B 07` command pattern
in all six. This is a research lead, not a universal container offset or a
decoded MIDI event stream. It requires validation against complete original
records before being used for playback.

## Device codes now connect to the original music records

The MULTAK header's 16-bit table at byte 16 gives a base slot for each group
of 1,000 device codes. The music-table slot is `base[code / 1000] + code % 1000`;
`FFFF` bases and null song pointers remain unsupported. Cross-checking the real
Volume 40 MASECOS4.IDX gave 22,900 unique device codes and 22,900 unique slots,
covering every non-null music pointer exactly. Its 21-byte records store a BCD
device code, language and relative title pointer. A zero end record and sector
alignment locate the text storage without hardcoding the older researcher's
volume-specific text offset.

The new bounded `MasecoIndex` reader preserves title bytes and decodes the 4,140
ASCII English titles. Other languages retain original bytes until their exact
font encoding is established. In particular the older Vietnamese conversion
table does not match this disc's bytes and is not silently applied. The Windows
ISO inspection lists counts, mapped codes and the first 100 English entries.

Independent public range reads verified catalogue titles against actual music
headers for English codes 30001 and 30093 and Vietnamese code 50001 (raw bytes,
without pretending its font encoding is solved). Volume 40 code 30093 is a
different song from the historical `30093_raw.dat` attachment, confirming that
old sample filenames must not be used as device song codes. The lyric/music
boundary also matched eight Volume 40 samples. Notes, timing, instrument
parameters and proprietary playback still remain unfinished.

The [updated Windows release](https://github.com/giangnam0201/VietKEMU/releases/tag/arirang-midi-test-37574666200)
passed [run 37574666200](https://github.com/giangnam0201/VietKEMU/actions/runs/37574666200).
Its inspected mapping report confirms 22,900 catalogue entries, 4,140 English
titles, one-to-one coverage of all 22,900 music pointers and three matching
original song headers. The separate player report confirms two native windows,
queue selection and standard MIDI/KAR parsing; original music playback and
hardware audio remain unverified. Only verification assets were downloaded;
the application ZIP remains on GitHub for the user to download.
