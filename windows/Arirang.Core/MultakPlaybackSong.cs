namespace Arirang.Core;

// Independent compact-disc timing conversion. Audio uses the caller's MIDI output;
// original wave banks and lyric data are not decoded by this reader.
public static class MultakPlaybackSong
{
    public static MidiSong Parse(byte[] record, string title) => FromChannels(
        MultakSongStreams.Parse(record).Channels.Select(MultakCompactNotes.Parse).ToArray(), title);

    public static MidiSong FromChannels(IReadOnlyList<MultakCompactNotes> channels, string title)
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
        return new(title, Seconds(end), messages, []);
    }
}
