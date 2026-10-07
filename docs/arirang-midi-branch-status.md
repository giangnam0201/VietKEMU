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
