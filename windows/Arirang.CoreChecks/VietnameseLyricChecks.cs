using System.Text.Json;
using Arirang.Core;

internal static class VietnameseLyricChecks
{
    private sealed record Expected(int Code, int MusicOffset, int Channels, int Blocks,
        int Notes, long MusicEnd, int PrimaryBytes, int SecondaryBytes, int Staff,
        long LyricEnd, int FirstVoice, int SecondVoice, int Phrases, long LastGlyph,
        long FirstGlyph, double Duration);

    internal static void ExportOriginal(string input, string output)
    {
        var cases = new Expected[] {
            new(50001, 4736, 7, 25, 5426, 9363, 1583, 1440, 3163, 9412, 620, 574, 70, 9260, 562, 234.07083333333333),
            new(50350, 2791, 8, 31, 7150, 5389, 1003, 961, 2119, 5443, 388, 379, 49, 5347, 858, 134.72083333333333)
        };
        var folder = Path.GetDirectoryName(Path.GetFullPath(input))!;
        var reports = cases.Select(expected => Verify(expected.Code == 50001 ? input :
            Path.Combine(folder, $"complete-{expected.Code}.bin"), expected)).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            originalVietnameseRecords = reports.Length, catalogueLanguageId = 7,
            allPhraseStartsMatchGuide = true, vietnameseGlyphsDecoded = true,
            lyricsRetainedInPlayback = true, nativeDeviceTimingVerified = false,
            originalInstrumentsVerified = false, audiblePlaybackVerified = false,
            records = reports
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static object Verify(string input, Expected expected)
    {
        if (new FileInfo(input).Length != 14336)
            throw new InvalidDataException("Unexpected bounded Vietnamese verification record.");
        var raw = File.ReadAllBytes(input);
        var layout = MultakSongLayout.Parse(raw);
        var streams = MultakSongStreams.Parse(raw);
        var channels = streams.Channels.Select(MultakCompactNotes.Parse).ToArray();
        var lyrics = MultakLyrics.Parse(raw, 7);
        var starts = lyrics.Glyphs.Where(g => g.NewLine).ToArray();
        var guideTicks = channels[0].Events.Where(e => (e.Status >> 4) == 9 && e.Data2 > 0)
            .Select(e => e.Tick).ToHashSet();
        if (layout.MusicOffset != expected.MusicOffset || channels.Length != expected.Channels ||
            streams.BlocksRead != expected.Blocks || channels.Sum(c => c.Events.Count) != expected.Notes ||
            channels.Max(c => c.EndTick) != expected.MusicEnd || lyrics.PrimaryBytes != expected.PrimaryBytes ||
            lyrics.SecondaryBytes != expected.SecondaryBytes || lyrics.StaffOffset != expected.Staff ||
            lyrics.EndTick != expected.LyricEnd || lyrics.Glyphs.Count != expected.FirstVoice + expected.SecondVoice ||
            starts.Length != expected.Phrases || lyrics.Glyphs.Count(g => g.Voice == 1) != expected.FirstVoice ||
            lyrics.Glyphs.Count(g => g.Voice == 2) != expected.SecondVoice ||
            lyrics.Glyphs.Max(g => g.Tick) != expected.LastGlyph || starts.Any(g => !guideTicks.Contains(g.Tick)))
            throw new InvalidDataException($"Vietnamese record {expected.Code} differs: blocks={streams.BlocksRead}, " +
                $"events={channels.Sum(c => c.Events.Count)}, end={channels.Max(c => c.EndTick)}, " +
                $"glyphs={lyrics.Glyphs.Count}, phrases={starts.Length}, " +
                $"unmatched={starts.Count(g => !guideTicks.Contains(g.Tick))}.");
        if (lyrics.Glyphs.Min(g => g.Tick) != expected.FirstGlyph ||
            !lyrics.Glyphs.Any(g => g.Text > 127))
            throw new InvalidDataException("Vietnamese original accents or initial timing were lost.");
        var song = MultakPlaybackSong.Parse(raw, "Original Vietnamese verification", 7);
        double firstSeconds = 2.5 / 120 + (expected.FirstGlyph - 1) * 2.5 / 100;
        if (song.Notice is not null || song.Lyrics.Count != expected.FirstVoice + expected.SecondVoice ||
            Math.Abs(song.Duration - expected.Duration) > .000001 ||
            song.Lyrics.Count(l => l.Voice == 1) != expected.FirstVoice ||
            song.Lyrics.Count(l => l.Voice == 2) != expected.SecondVoice ||
            Math.Abs(song.Lyrics.First().Seconds - firstSeconds) > .000001)
            throw new InvalidDataException("Vietnamese full-record conversion discarded voices or timing.");
        return new {
            songCode = expected.Code, originalRecordBytes = raw.Length, musicChannels = channels.Length,
            noteEvents = channels.Sum(c => c.Events.Count), blocksRead = streams.BlocksRead,
            musicEndTick = channels.Max(c => c.EndTick), primaryLyricGlyphs = expected.FirstVoice,
            secondaryLyricGlyphs = expected.SecondVoice, lyricPhraseStarts = starts.Length,
            lyricEndTick = lyrics.EndTick, lastVisibleGlyphTick = lyrics.Glyphs.Max(g => g.Tick),
            calculatedDurationSeconds = song.Duration
        };
    }
}
