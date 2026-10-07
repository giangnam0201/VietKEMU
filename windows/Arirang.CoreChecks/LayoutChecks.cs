using System.Buffers.Binary;
using System.Text.Json;
using Arirang.Core;

internal static class LayoutChecks
{
    internal static void Run()
    {
        var raw = new byte[140]; byte mask = 66;
        raw[2] = 0x4f; raw[3] = 0x4b; raw[38] = mask;
        "TEST/\0"u8.CopyTo(raw.AsSpan(42));
        for (int i = 42; i < 48; i++) raw[i] ^= mask;
        byte[] metadata = [0, 0, 23, 0, 0, 17, 1, 0, 0, 1, 0, 0, 0, 60, 4];
        for (int i = 0; i < metadata.Length; i++) raw[48 + i] = (byte)(metadata[i] ^ mask);
        raw[98] = (byte)(0x1a ^ mask); raw[99] = (byte)(0xff ^ mask);
        var layout = MultakSongLayout.Parse(raw);
        if (layout.MusicOffset != 100 || layout.Tracks.Count != 1 || layout.Tracks[0].Channel != 0 ||
            layout.Tracks[0].PitchBits != 4 || layout.Tracks[0].BasePitch != 60 ||
            !layout.TitleBytes.SequenceEqual("TEST/"u8.ToArray())) throw new Exception("MULTAK title, track table and boundary.");
        var badChannel = raw.ToArray(); badChannel[56] = (byte)(16 ^ mask);
        var badLayout = raw.ToArray(); badLayout[50] = (byte)(24 ^ mask);
        var badTrack = raw.ToArray(); badTrack[60] = (byte)(1 ^ mask);
        var badBoundary = raw.ToArray(); badBoundary[98] = mask;
        var badWidth = raw.ToArray(); badWidth[62] = mask;
        foreach (var invalid in new[] { raw[..99], badChannel, badLayout, badTrack, badBoundary, badWidth })
        {
            bool rejected = false;
            try { MultakSongLayout.Parse(invalid); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Malformed original song layout must be rejected.");
        }
        var blocks = new byte[100 + 672]; raw.CopyTo(blocks, 0);
        for (int i = 100; i < blocks.Length; i++) blocks[i] = mask;
        byte[] firstBlock = [0x12, 0x34, 1, 0, 0, 0xff];
        byte[] lastBlock = [0x56, 0x8f, 0xff, 0x2f, 0xff, 0xff, 0xff];
        for (int i = 0; i < firstBlock.Length; i++) blocks[100 + i] = (byte)(firstBlock[i] ^ mask);
        for (int i = 0; i < lastBlock.Length; i++) blocks[436 + i] = (byte)(lastBlock[i] ^ mask);
        var streams = MultakSongStreams.Parse(blocks);
        if (streams.BlocksRead != 2 || streams.Channels[0].Segments != 2 ||
            !streams.Channels[0].Bytes.SequenceEqual(new byte[] { 0x12, 0x34, 0x56, 0x8f, 0xff, 0x2f }))
            throw new Exception("MULTAK block links must preserve exact compact stream bytes.");
        var backward = blocks.ToArray(); backward[102] = mask;
        var badOffset = blocks.ToArray(); badOffset[103] = (byte)(1 ^ mask); badOffset[104] = (byte)(80 ^ mask);
        foreach (var invalid in new[] { backward, badOffset, blocks[..^1] })
        {
            bool rejected = false;
            try { MultakSongStreams.Parse(invalid); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Invalid MULTAK block references must be rejected.");
        }
        byte[] compact = [0, 0x8b, 7, 0xe4, 0x8c, 0x80, 8, 0x99, 0xc4, 0x8f, 0xff, 0x2f];
        var noteStream = new MultakCompactStream(new(4, 0, 1, 0, 60), compact, 1);
        var notes = MultakCompactNotes.Parse(noteStream);
        if (notes.Events.Count != 4 || notes.Events[0] != new MultakNoteEvent(0, 0xb0, 7, 100) ||
            notes.Events[1] != new MultakNoteEvent(0, 0xc0, 0, null) ||
            notes.Events[2] != new MultakNoteEvent(0, 0x90, 64, 100) ||
            notes.Events[3] != new MultakNoteEvent(3, 0x80, 64, 64) || notes.EndTick != 3)
            throw new Exception("Compact controller, program, packed pitch, velocity, delay and recent-pitch note-off.");
        var extendedTime = compact.Skip(1).Prepend((byte)0xc5).Prepend((byte)0xc4).ToArray();
        if (MultakCompactNotes.Parse(noteStream with { Bytes = extendedTime }).Events[0].Tick != 17605)
            throw new Exception("Compact time preserves the reference's 15-bit two-byte delay.");
        var drums = MultakCompactNotes.Parse(noteStream with { Track = new(4, 9, 1, 0, 36) });
        if (!drums.Events.Any(e => e.Status == 0x99 && e.Data1 == 40 && e.Data2 == 0))
            throw new Exception("Percussion must release the packed note immediately.");
        var timed = MultakPlaybackSong.FromChannels([new(
            [new(0, 0x90, 60, 100), new(24, 0x80, 60, 64), new(48, 0xc0, 0, null)],
            [new(0, 1, [82]), new(24, 1, [22])], 48)], "Synthetic MULTAK tempo changes");
        if (Math.Abs(timed.Duration - 1.5) > .000001 || Math.Abs(timed.Messages[1].Seconds - .5) > .000001 ||
            Math.Abs(timed.Messages[2].Seconds - 1.5) > .000001)
            throw new Exception("MULTAK tempo changes must integrate 24 ticks per beat with the previous tempo up to the change.");
        var defaultClock = MultakPlaybackSong.FromChannels([new([new(0, 0xc0, 0, null)], [], 48)], "Default clock");
        var wrappedClock = MultakPlaybackSong.FromChannels([new([new(0, 0xc0, 0, null)], [new(0, 1, [218])], 24)], "Clamped clock");
        if (Math.Abs(defaultClock.Duration - 1) > .000001 || Math.Abs(wrappedClock.Duration - 6) > .000001)
            throw new Exception("MULTAK default tempo and byte-wrapped minimum tempo must follow the packet consumer.");
        foreach (var invalid in new MultakCompactNotes[] {
            new([new(0, 0xc0, 0, null)], [new(0, 2, [1, 2])], 48),
            new([new(0, 0xc0, 0, null)], [new(0, 1, [82]), new(0, 1, [22])], 48),
            new([new(49, 0xc0, 0, null)], [], 48) })
        {
            bool rejected = false;
            try { MultakPlaybackSong.FromChannels([invalid], "Invalid clock"); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Unsupported, conflicting or out-of-bounds MULTAK timelines must be rejected.");
        }
        foreach (var invalid in new[] { compact[..^1], compact.Concat(new byte[] { 0 }).ToArray(), new byte[] { 0, 0x8b, 255, 128 } })
        {
            bool rejected = false;
            try { MultakCompactNotes.Parse(noteStream with { Bytes = invalid }); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Invalid compact channel data must be rejected.");
        }
        var header = new byte[MultakIndex.TableOffset + 8];
        "multak3.3"u8.CopyTo(header.AsSpan(4));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(334), 2);
        header[MultakIndex.TableOffset] = header[MultakIndex.TableOffset + 2] = header[MultakIndex.TableOffset + 3] = 255;
        var table = MultakIndex.Parse(header, 65536 + raw.Length, 0);
        string path = Path.Combine(Path.GetTempPath(), "arirang-record-" + Guid.NewGuid().ToString("N") + ".iso");
        try
        {
            var iso = new byte[512 + 65536 + raw.Length]; raw.CopyTo(iso, 512 + 65536); File.WriteAllBytes(path, iso);
            var inventory = new DiscInventory("ISO9660", [new DiscFile("MULTAK.DAT", 65536 + raw.Length, 512)]);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16 + 30 * 2), 0);
            table = MultakIndex.Parse(header, 65536 + raw.Length, 0);
            if (!table.ReadSongRecordIso(path, inventory, 30001).SequenceEqual(raw))
                throw new Exception("Original code mapping must read the exact bounded ISO record.");
            File.WriteAllBytes(path, iso[..^1]);
            bool rejected = false;
            try { table.ReadSongRecordIso(path, inventory, 30001); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Truncated ISO song extent must be rejected.");
        }
        finally { File.Delete(path); }
    }

    internal static void ExportOriginal(string first, string second, string output)
    {
        MultakSongLayout Read(string path)
        {
            if (new FileInfo(path).Length > MultakSongLayout.MaximumBytes) throw new InvalidDataException("Song sample exceeds limit.");
            return MultakSongLayout.Parse(File.ReadAllBytes(path));
        }
        var a = Read(first); var b = Read(second);
        var streamsA = MultakSongStreams.Parse(File.ReadAllBytes(first));
        var streamsB = MultakSongStreams.Parse(File.ReadAllBytes(second));
        if (a.Tracks.Count != 5 || b.Tracks.Count != 8 || a.MusicOffset != 1195 || b.MusicOffset != 772 ||
            !a.Tracks.Any(t => t.Channel == 9) || !b.Tracks.Any(t => t.Channel == 9))
            throw new InvalidDataException("Original Happy Birthday layout does not match independent inspection.");
        if (streamsA.BlocksRead != 9 || streamsB.BlocksRead != 6 ||
            streamsA.Channels.Count != 5 || streamsB.Channels.Count != 8 ||
            streamsA.Channels.Concat(streamsB.Channels).Any(c => !c.Bytes.AsSpan().EndsWith(new byte[] { 0x8f, 0xff, 0x2f })))
            throw new InvalidDataException("Original compact channel reassembly does not match independent block inspection.");
        var notesA = streamsA.Channels.Select(MultakCompactNotes.Parse).ToArray();
        var notesB = streamsB.Channels.Select(MultakCompactNotes.Parse).ToArray();
        var playbackA = MultakPlaybackSong.FromChannels(notesA, "Original timing check A");
        var playbackB = MultakPlaybackSong.FromChannels(notesB, "Original timing check B");
        var lyricsA = MultakLyrics.Parse(File.ReadAllBytes(first));
        var lyricsB = MultakLyrics.Parse(File.ReadAllBytes(second));
        bool PhrasesMatch(MultakLyrics lyrics, MultakCompactNotes guide) =>
            lyrics.Glyphs.Where(g => g.NewLine).All(g => guide.Events.Any(e => e.Tick == g.Tick && (e.Status >> 4) == 9 && e.Data2 > 0));
        if (lyricsA.Glyphs.Count != 264 || lyricsB.Glyphs.Count != 182 ||
            lyricsA.Glyphs.Count(g => g.NewLine) != 24 || lyricsB.Glyphs.Count(g => g.NewLine) != 10 ||
            lyricsA.PrimaryBytes != 457 || lyricsB.PrimaryBytes != 250 ||
            lyricsA.SecondaryBytes != 299 || lyricsB.SecondaryBytes != 232 ||
            lyricsA.StaffOffset != 883 || lyricsB.StaffOffset != 630 ||
            !PhrasesMatch(lyricsA, notesA[0]) || !PhrasesMatch(lyricsB, notesB[0]))
            throw new InvalidDataException("Original lyric phrase ticks must match the independent guide-note stream.");
        var completeA = MultakPlaybackSong.Parse(File.ReadAllBytes(first), "Original lyric timing A");
        var completeB = MultakPlaybackSong.Parse(File.ReadAllBytes(second), "Original lyric timing B");
        if (completeA.Lyrics.Count != 264 || completeB.Lyrics.Count != 182 || completeA.Notice is not null || completeB.Notice is not null)
            throw new InvalidDataException("Original record playback must retain its decoded lyrics.");
        if (Math.Abs(playbackA.Duration - 62.6041666666667) > .000001 || Math.Abs(playbackB.Duration - 43.1875) > .000001 ||
            playbackA.Messages.Count != 1692 || playbackB.Messages.Count != 1001)
            throw new InvalidDataException("Original tempo conversion must match independent clock calculation.");
        byte[] openingA = [80, 80, 82, 80, 85, 84, 80, 80, 82, 80, 87, 85, 80, 80, 92, 89];
        byte[] openingB = [71, 71, 73, 71, 76, 75, 71, 71, 73, 71, 78, 76, 71, 71, 83, 80];
        byte[] FirstNotes(MultakCompactNotes channel) => channel.Events.Where(e => (e.Status >> 4) == 9 && e.Data2 > 0)
            .Take(16).Select(e => e.Data1).ToArray();
        if (!FirstNotes(notesA[0]).SequenceEqual(openingA) || !FirstNotes(notesB[0]).SequenceEqual(openingB) ||
            notesA.Sum(n => n.Events.Count) != 1692 || notesB.Sum(n => n.Events.Count) != 1001 ||
            notesA.Max(n => n.EndTick) != 3005 || notesB.Max(n => n.EndTick) != 2073)
            throw new InvalidDataException("Original compact notes do not match independent event expansion.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            originalSongRecords = 2, firstSongCode = 30655, secondSongCode = 32153,
            firstRecordBytes = new FileInfo(first).Length, secondRecordBytes = new FileInfo(second).Length,
            firstChannelStreams = a.Tracks.Count, secondChannelStreams = b.Tracks.Count,
            firstMusicOffset = a.MusicOffset, secondMusicOffset = b.MusicOffset,
            percussionChannelsLocated = true, trackOffsetsVerified = true,
            firstBlocksRead = streamsA.BlocksRead, secondBlocksRead = streamsB.BlocksRead,
            compactStreamsReassembled = true, allChannelsReachedEndOfTrack = true,
            firstNoteEvents = notesA.Sum(n => n.Events.Count), secondNoteEvents = notesB.Sum(n => n.Events.Count),
            firstEndTick = notesA.Max(n => n.EndTick), secondEndTick = notesB.Max(n => n.EndTick),
            pitchWidthsReadFromHeader = true, melodyPrefixesVerified = true,
            tempoClockConverted = true, firstCalculatedSeconds = playbackA.Duration, secondCalculatedSeconds = playbackB.Duration,
            firstLyricGlyphs = lyricsA.Glyphs.Count, secondLyricGlyphs = lyricsB.Glyphs.Count,
            firstLyricPhrases = 24, secondLyricPhrases = 10, lyricPhraseTicksMatchGuide = true, twoOriginalLyricVoices = true,
            nativeTimingVerified = false, originalLyricsDecoded = true, originalInstrumentsDecoded = false,
            notesDecoded = true, ticksParsed = true, timingDecoded = false, playbackVerified = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
