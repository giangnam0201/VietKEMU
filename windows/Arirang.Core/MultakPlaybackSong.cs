namespace Arirang.Core;

// Independent compact-disc timing conversion. Audio uses the caller's MIDI output;
// Original wave banks remain unimplemented; supported ASCII lyric lanes retain
// their original ticks and are converted with the same tempo map as the notes.
public static class MultakPlaybackSong
{
    public static MidiSong Parse(byte[] record, string title)
    {
        var channels = MultakSongStreams.Parse(record).Channels.Select(MultakCompactNotes.Parse).ToArray();
        try { return FromChannels(channels, title, MultakLyrics.Parse(record).Glyphs); }
        catch (InvalidDataException error)
        {
            return FromChannels(channels, title) with { Notice = "Lời gốc chưa hỗ trợ: " + error.Message };
        }
    }

    public static MidiSong FromChannels(IReadOnlyList<MultakCompactNotes> channels, string title,
        IReadOnlyList<MultakLyricGlyph>? glyphs = null)
    {
        if (channels.Count is < 1 or > 16 || channels.Sum(c => (long)c.Events.Count + c.Commands.Count) > 2_000_000)
            throw new InvalidDataException("MULTAK playback event limit exceeded.");
        long end = channels.Max(c => c.EndTick);
        if (end < 0 || end > int.MaxValue || channels.Any(c => c.EndTick < 0 ||
            c.Events.Any(e => e.Tick < 0 || e.Tick > c.EndTick || e.Status is < 0x80 or >= 0xf0 ||
                e.Data1 > 127 || e.Data2 > 127 || ((e.Status >> 4) is 12 or 13) != (e.Data2 is null)) ||
            c.Commands.Any(e => e.Tick < 0 || e.Tick > c.EndTick)))
            throw new InvalidDataException("Invalid MULTAK playback timeline.");

        var tempos = new SortedDictionary<long, int>();
        foreach (var command in channels.SelectMany(c => c.Commands))
        {
            if (command.Command is 0 or 255 && command.Parameters.Length == 0) continue;
            if (command.Command != 1 || command.Parameters.Length != 1)
                throw new InvalidDataException("This song uses an unsupported MULTAK playback command.");
            // Cmd_Zhu adds 38; its packet consumer takes the low byte and clamps to 10.
            int tempo = Math.Max(10, (command.Parameters[0] + 38) & 255);
            if (tempos.TryGetValue(command.Tick, out int previous) && previous != tempo)
                throw new InvalidDataException("Conflicting simultaneous MULTAK tempo commands.");
            tempos[command.Tick] = tempo;
        }
        var changes = tempos.ToArray();
        int nextTempo = 0, bpm = 120;
        long anchorTick = 0;
        double anchorSeconds = 0;
        double Seconds(long tick)
        {
            while (nextTempo < changes.Length && changes[nextTempo].Key <= tick)
            {
                var change = changes[nextTempo++];
                anchorSeconds += (change.Key - anchorTick) * 2.5 / bpm;
                anchorTick = change.Key; bpm = change.Value;
            }
            double seconds = anchorSeconds + (tick - anchorTick) * 2.5 / bpm;
            if (seconds > 86_400) throw new InvalidDataException("MULTAK playback duration exceeds one day.");
            return seconds;
        }
        var messages = channels.SelectMany(c => c.Events).OrderBy(e => e.Tick)
            .Select(e => new MidiMessage(Seconds(e.Tick),
                (uint)(e.Status | e.Data1 << 8 | (e.Data2 ?? 0) << 16))).ToArray();
        double duration = Seconds(end);
        if (glyphs is not null && (glyphs.Count > 500_000 || glyphs.Any(g => g.Tick < 0 || g.Tick > end ||
            g.Text is < (char)32 or > (char)126 || g.Voice is not (1 or 2))))
            throw new InvalidDataException("Invalid MULTAK lyric timeline.");
        nextTempo = 0; bpm = 120; anchorTick = 0; anchorSeconds = 0;
        var lyrics = (glyphs ?? []).OrderBy(g => g.Tick)
            .Select(g => new LyricCue(Seconds(g.Tick), g.Text.ToString(), g.NewLine, g.Voice)).ToArray();
        return new(title, duration, messages, lyrics);
    }
}
