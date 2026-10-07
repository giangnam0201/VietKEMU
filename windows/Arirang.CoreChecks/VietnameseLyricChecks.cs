using System.Text.Json;
using Arirang.Core;

internal static class VietnameseLyricChecks
{
    internal static void ExportOriginal(string input, string output)
    {
        if (new FileInfo(input).Length != 14336)
            throw new InvalidDataException("Unexpected bounded Vietnamese verification record.");
        var raw = File.ReadAllBytes(input);
        var layout = MultakSongLayout.Parse(raw);
        var streams = MultakSongStreams.Parse(raw);
        var channels = streams.Channels.Select(MultakCompactNotes.Parse).ToArray();
        var lyrics = MultakLyrics.Parse(raw);
        var starts = lyrics.Glyphs.Where(g => g.NewLine).ToArray();
        var guideTicks = channels[0].Events.Where(e => (e.Status >> 4) == 9 && e.Data2 > 0)
            .Select(e => e.Tick).ToHashSet();
        if (layout.MusicOffset != 4736 || channels.Length != 7 || streams.BlocksRead != 25 ||
            channels.Sum(c => c.Events.Count) != 5426 || channels.Max(c => c.EndTick) != 9363 ||
            lyrics.PrimaryBytes != 1584 || lyrics.SecondaryBytes != 1440 || lyrics.StaffOffset != 3163 ||
            lyrics.EndTick != 9412 || lyrics.Glyphs.Count != 1194 || starts.Length != 70 ||
            lyrics.Glyphs.Count(g => g.Voice == 1) != 620 || lyrics.Glyphs.Count(g => g.Voice == 2) != 574 ||
            lyrics.Glyphs.Max(g => g.Tick) != 9260 || starts.Any(g => !guideTicks.Contains(g.Tick)))
            throw new InvalidDataException($"Vietnamese original lyric timing differs: blocks={streams.BlocksRead}, " +
                $"events={channels.Sum(c => c.Events.Count)}, end={channels.Max(c => c.EndTick)}, " +
                $"glyphs={lyrics.Glyphs.Count}, phrases={starts.Length}, " +
                $"unmatched={starts.Count(g => !guideTicks.Contains(g.Tick))}.");
        if (lyrics.Glyphs.First(g => g.Voice == 1).Tick != 562 ||
            !lyrics.Glyphs.Any(g => g.Text == 'ỏ') || !lyrics.Glyphs.Any(g => g.Text == 'đ'))
            throw new InvalidDataException("Vietnamese original accents or initial timing were lost.");
        var song = MultakPlaybackSong.Parse(raw, "Original Vietnamese verification");
        if (song.Notice is not null || song.Lyrics.Count != 1194 ||
            Math.Abs(song.Duration - 234.07083333333333) > .000001 ||
            song.Lyrics.Count(l => l.Voice == 1) != 620 || song.Lyrics.Count(l => l.Voice == 2) != 574 ||
            Math.Abs(song.Lyrics.First().Seconds - 14.045833333333333) > .000001)
            throw new InvalidDataException("Vietnamese full-record playback conversion discarded lyric voices or timing.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            songCode = 50001, originalRecordBytes = raw.Length,
            musicChannels = channels.Length, noteEvents = channels.Sum(c => c.Events.Count),
            blocksRead = streams.BlocksRead, musicEndTick = channels.Max(c => c.EndTick),
            lyricFormat = 12, primaryLyricGlyphs = 620, secondaryLyricGlyphs = 574,
            lyricPhraseStarts = starts.Length, allPhraseStartsMatchGuide = true,
            lyricEndTick = lyrics.EndTick, lastVisibleGlyphTick = lyrics.Glyphs.Max(g => g.Tick),
            vietnameseGlyphsDecoded = true, lyricsRetainedInPlayback = true,
            calculatedDurationSeconds = song.Duration, nativeDeviceTimingVerified = false,
            originalInstrumentsVerified = false, audiblePlaybackVerified = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
