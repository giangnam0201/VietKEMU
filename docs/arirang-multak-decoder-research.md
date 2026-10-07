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
- The apparent difference in channel descriptors was subsequently traced to
  the independent reader starting one byte early (see the correction below).
  Do not infer full compatibility merely from function names or make the
  unknown header field into a tempo.

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

## Legacy timing and pitch-width experiments

These earlier timing/width hypotheses are superseded by the corrected channel
descriptor below. They are retained to explain why they must not be applied.

The initial command experiment produced three implausible 17,605-tick delays
in device 30655, channel 4. Each arose at a repeated sequence where a `C0`
note-off was followed by `C4 C5 C6`. The newer reference's signed-byte time
rule consumed `C4 C5` as one delay, losing another note-off command.

A legacy candidate instead reads `80..BF` as a two-byte delay prefix and
`C0..FF` as whole-byte delays. With unchanged channel bytes and pitch width,
the end moves from 55,204 to 2,977 ticks, close to the bass/percussion experiment
at 2,987/2,999 ticks. This is evidence for a format difference, not yet proof
of accurate timing; do not replace the player clock or the general MIDI parser
with this hypothesis. The original MIDI attachment inspected as another
possible comparison has 22 tracks and lacks verified correspondence to either
Happy Birthday record, so it is not used as a timing oracle.

The descriptor's first byte also cannot simply select pitch width everywhere.
For device 30655, channel 6, using four bits gives 5,611 ticks and pitch range
37..52; six bits gives 2,993 ticks and range 37..73. The guide melody requires
five bits, while the other arrangement requires four to retain its octave
leaps. These observed differences require locating the actual width-selection
rule, not choosing widths merely to make durations match. Candidate note
events, width sweeps and original compact streams remain private and are not
offered as finished disc playback.

Two additional public Volume 40 companion-file prefixes were inspected with
64-KiB range reads each: `MASECOF3.BIN` (8,649,868 declared bytes) and
`MASECOR5.DAT` (1,572,864 bytes). Neither prefix has ELF or SMF magic.
`MASECOF3.BIN` contains `Multak MID10` at byte 2,000, followed by a
`multak3.2006-7-13` identifier and structured header fields. This identifies
another original structured resource for inspection; its name or signature
does not establish that it contains executable decoder code. No downloaded
companion data is executed, committed or packaged.

## Channel descriptor correction and independent note expansion

Tracing the reference's title-terminator loop established that the channel
table begins at `titleEnd + 9`, not `titleEnd + 8`. The extra byte is outside
the table. Each seven-byte descriptor contains channel, an unclassified
format byte, a three-byte relative offset, base pitch and pitch width. Starting
one byte early associated the preceding channel's pitch width with the next
channel and omitted the final channel's width. Channel numbers, offsets and
base pitches happened to remain at the correctly interpreted byte locations.

The corrected Volume 40 pitch widths are `5,5,4,6,6` for device 30655 and
`4,4,5,5,4,4,5,5` for device 32153. No inferred guide width or duration-based
width choice is now needed. With those header values, the original reference's
15-bit two-byte delay rule works across both complete records; the proposed
`C0..FF` whole-byte delay exception is not used. The high-note retention
experiment likewise did not repair the header problem and is not used.

`MultakCompactNotes` independently expands bounded channel streams into note,
controller, program, pressure and pitch-bend events with ticks. It preserves
opaque commands rather than inventing their interpretation. Four recent
pitches supply compact note-offs; percussion emits an immediate zero-velocity
release. Exact synthetic checks cover controller/program handling, pitch,
velocity, embedded delays, recent note-offs, percussion, two-byte time and
malformed input. Complete original-record checks compare 1,692/1,001 expanded
events, known opening melody pitches including octave leaps, and end ticks
3,005/2,073 against independent byte expansion.

Ticks are not yet verified wall-clock time. Tempo commands, lyric timing,
instrument fidelity and audible original playback remain unfinished. The ISO
inspection can report recovered event counts and header pitch widths but does
not offer them as finished original song playback.
