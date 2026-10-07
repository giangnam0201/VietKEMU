# Original MULTAK decoder research

Original Arirang disc playback remains the target. Standard MIDI playback in
the Windows app does not establish compatibility with the disc's compact data.

## Static software reference located on 2026-10-07

The manufacturer's [website](https://www.multak.com/en/index.htm) mentions
CooKara. Its [Google Play listing](https://play.google.com/store/apps/details?id=com.multak.cookaraclient)
describes remote control, making it a weaker decoder candidate. The related
[WalKaraoke listing](https://play.google.com/store/apps/details?id=com.multak.ms20ina)
uses package `com.multak.ms20ina` and Multak support contacts. Its
[Apple listing](https://apps.apple.com/us/app/walkaraoke/id1440685843)
describes local song playback and mentions local MIDI in its version history.
Compatibility with older Arirang discs is not claimed by those listings.

A publicly available mirrored WalKaraoke package was inspected using bounded
HTTP ranges. The outer ZIP is 129,449,712 bytes; its base APK is 118,402,428
bytes and ARM64 split is 10,277,437 bytes. Neither whole package was downloaded.
The base directory includes `assets/Res/wavetable.bin` (4,198,400 bytes).
The ARM64 directory contains `libchaosplayer.so`, `libffmpeg.so` and
`libijkffmpeg.so`. Only the compressed karaoke library was retrieved:
2,310,115 compressed bytes, 4,975,520 expanded bytes, SHA-256
`9032ff7cc16dad2b32267b9e92e17f792183726e2c89bb8d6db433c9d5f940e6`.
The mirror's signing identity has not been independently verified against
Google Play. The artifact was never executed and is private research material,
not a dependency, source attachment or distributable Windows component.

Its exported functions provide a concrete reference beyond pattern guessing:

| Function | ARM64 virtual address | Inspection purpose |
| --- | --- | --- |
| `Midi_Play` | `0x34e4cc` | Initialize channel and block state |
| `LdiData` | `0x34edb0` | Read bytes and cross channel/block boundaries |
| `Read_Time` | `0x34eebc` | Read compact time values |
| `MidP2` | `0x34e9a4` | Expand compact commands and notes |
| `dodeml` | `0x34efb0` | Dispatch commands and end-of-track |
| `Midi_Proc` | `0x34f058` | Schedule active channel streams |
| `GetNextAdr` | `0x34e314` | Determine channel boundaries in a block |

Static observations, not yet validated as a complete disc decoder:

- Time values use one byte below 128; a negative signed first byte causes a
  second byte read and yields `((first & 0x7f) << 8) | second`. This differs
  from Standard MIDI's variable-length integer.
- Command bytes `0x80..0x8f` supply the MIDI status high nibble. The current
  channel supplies its low nibble. This agrees with the disc's repeated
  `8B` controller and `8C` program initialization sequences.
- Compact note expansion combines two bytes, retains four recent pitches per
  channel, and uses per-channel masks and a base pitch. The bottom five bits
  become velocity scaled by four. Timing may be embedded in that word.
- `LdiData` crosses boundaries using a block identifier and offset. One mode
  uses two 12-bit values packed in three bytes; another uses a seven-bit
  identifier and nine-bit offset. Treating these bytes as ordinary note data
  would corrupt subsequent parsing.
- This library initializes an internal channel descriptor layout that differs
  from the seven-byte descriptors currently observed on Volume 40. Establish
  the transformation and block addressing before applying its expansion
  rules to original records. Do not infer compatibility merely from function
  names or make the unknown header field into a tempo.

Next validation should reconstruct the bounded block reader and expand the
two complete original Happy Birthday arrangements (device codes 30655 and
32153). Check the actual melody, channel events, note lifetimes and timing;
successful static extraction alone does not prove correct music playback.
Private scripts, native library and disassembly remain under `.reference`.

## Original block reassembly

Independent range samples now establish 336-byte music blocks for both complete
original arrangements. Initial channel offsets address block zero. At a channel
boundary, three bytes encode a 12-bit next-block number and a 12-bit offset:
`block = a | ((b >> 4) << 8)`, `offset = ((b & 15) << 8) | c`.
Channel starts in the same block determine preceding channel boundaries. The
last channel uses the block's final `FF` marker, accounting for the special
`8F FF 2F FF FF FF` end marker. Reference bytes are removed from the resulting
compact command streams, while `8F FF 2F` remains as end-of-track.

`MultakSongStreams` implements this independently with bounded source reads,
forward references, offset checks, aggregate output limits and exact-byte tests.
Device 30655 occupies nine music blocks and five complete channel streams;
device 32153 occupies six blocks and eight complete streams. Every stream reaches
its end-of-track marker. This proves reassembly for these records, not universal
compatibility with all MULTAK formats or valid event timing.

A private independent command-expansion experiment reproduces the known
opening melody and octave leaps of Happy Birthday from both original guide
streams. It uses candidate pitch widths of five and four bits respectively;
their source in the header still requires validation. Some accompaniment
timestamps are inconsistent, so the experimental note conversion is not added
to the playback path. Opaque commands, timing, guide width selection and
instrument fidelity remain outstanding. No manufacturer library is required
by the independent block reader.

The [Windows test release](https://github.com/giangnam0201/VietKEMU/releases/tag/arirang-midi-test-37579116484)
passed [GitHub run 37579116484](https://github.com/giangnam0201/VietKEMU/actions/runs/37579116484).
The downloaded verification report confirms nine/six music blocks, all 13
channel streams reassembled and all channels reaching end-of-track. Notes,
timing and playback remain marked false. Standard MIDI player and two-window
checks also passed. Only the small verification ZIP was downloaded locally.
