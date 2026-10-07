# Arirang MIDI development

This branch (`codex/arirang-midi`) develops an independent Windows Arirang MIDI karaoke player. The VietK application remains on `main`; general VietK improvements are deferred.

The dedicated app is [windows/Arirang.MidiPlayer](windows/Arirang.MidiPlayer). It uses the official Arirang logo, its own settings directory, a native control panel and a live TV/lyrics window. See its [README](windows/Arirang.MidiPlayer/README.md) for supported features and limits.

GitHub Actions workflow **Build Arirang MIDI Windows branch** produces a self-contained Windows x64 test ZIP and verification reports. Extract the ZIP and run `Arirang.MidiPlayer.exe`. No heavy local build is needed.

Current work includes standard MIDI/KAR parsing and playback, embedded lyric timing, queues, seek, tempo, key and volume, public volume metadata, and bounded ISO9660 directory inspection. **Original ARVNKR/MULTAK song decoding is still in development.** Disc/background video files are not represented as playable karaoke songs.

[Source investigation](docs/arirang-maseco-source-investigation.md) records the public archive leads and the incomplete MediaFire multipart set. The older VietK source is retained as development reference but is not built by the Arirang workflow.
