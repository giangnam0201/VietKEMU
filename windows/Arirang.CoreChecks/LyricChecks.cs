using Arirang.Core;

internal static class LyricChecks
{
    internal static void Run()
    {
        byte mask = 166;
        var raw = new byte[240];
        raw[2] = 0x4f; raw[3] = 0x4b; raw[38] = mask; // music = 34 + 166
        for (int i = 42; i < raw.Length; i++) raw[i] = mask;
        void Put(int at, byte[] value) { for (int i = 0; i < value.Length; i++) raw[at + i] = (byte)(value[i] ^ mask); }
        Put(42, "TEST/\0"u8.ToArray());
        byte[] first = [0x29, 24, 0x26, 1, 0x5c, 0, (byte)'L', 12, (byte)'a', 12, 0x5e, 1, 0, 24, 0x1a, 255];
        byte[] second = [48, 0x26, 2, (byte)'l', 6, (byte)'a', 6, 0x5e, 2, 0, 24, 0x1a, 255];
        Put(48, [0, 0, 23, 0, 0, (byte)(first.Length - 1), 1, 0, 0, 1, 0, 0, 0, 60, 4]);
        Put(63, [0, 152, 0, 0, 43, 0, 0, 50]);
        Put(70, first); Put(86, second); Put(198, [0x1a, 255]);
        raw[70] = (byte)(50 ^ mask); // Low byte of the third pointer, not a lyric command.
        var lyrics = MultakLyrics.Parse(raw);
        if (lyrics.PrimaryBytes != 15 || lyrics.SecondaryBytes != 13 || lyrics.StaffOffset != 99 || lyrics.EndTick != 85 ||
            !lyrics.Glyphs.SequenceEqual(new MultakLyricGlyph[] {
                new(25, 'L', true, 1), new(37, 'a', false, 1), new(49, 'l', true, 2), new(55, 'a', false, 2) }))
            throw new Exception("Original lyric voices, formatting parameters and absolute secondary time.");
        var song = MultakPlaybackSong.FromChannels([new([new(0, 0xc0, 0, null)], [new(24, 1, [22])], 100)], "Lyric clock", lyrics.Glyphs);
        if (Math.Abs(song.Lyrics[0].Seconds - (0.5 + 2.5 / 60)) > .000001 || song.Lyrics[2].Voice != 2)
            throw new Exception("Lyric ticks must share the changing note tempo map and preserve voices.");
        var vietnamese = raw.ToArray();
        vietnamese[76] = (byte)(0xae ^ mask); vietnamese[78] = (byte)(0xa0 ^ mask);
        vietnamese[82] = (byte)(3 ^ mask); // Replace a primary delay control; same timing.
        var vn = MultakLyrics.Parse(vietnamese, 7);
        if (vn.Glyphs[0] != new MultakLyricGlyph(25, 'Đ', true, 1) ||
            vn.Glyphs[1] != new MultakLyricGlyph(37, 'á', false, 1) || vn.EndTick != lyrics.EndTick)
            throw new Exception("Vietnamese glyphs and intro controls must preserve lyric timing.");
        var vnSong = MultakPlaybackSong.FromChannels([new([new(0, 0xc0, 0, null)], [], 100)], "Synthetic Vietnamese", vn.Glyphs);
        if (vnSong.Lyrics[0].Text != "Đ" || vnSong.Lyrics[1].Text != "á")
            throw new Exception("Vietnamese Unicode glyphs must survive playback conversion.");
        bool unknownLanguageRejected = false;
        try { MultakLyrics.Parse(raw, 1); } catch (InvalidDataException) { unknownLanguageRejected = true; }
        if (!unknownLanguageRejected)
            throw new Exception("Unsupported catalogue languages must not use the Vietnamese map.");
        var wrongStaffPointer = raw.ToArray(); wrongStaffPointer[69] = (byte)(4 ^ mask);
        var wrongVoice = raw.ToArray(); wrongVoice[73] = (byte)(3 ^ mask);
        var wrongLength = raw.ToArray(); wrongLength[53] = (byte)(255 ^ mask);
        var badTerminator = raw.ToArray(); badTerminator[85] = mask;
        var mismatchedEnd = raw.ToArray(); mismatchedEnd[81] = (byte)(2 ^ mask);
        var unsupportedGlyph = raw.ToArray(); unsupportedGlyph[76] = (byte)(128 ^ mask);
        foreach (var invalid in new[] { wrongStaffPointer, wrongVoice, wrongLength, badTerminator, mismatchedEnd, unsupportedGlyph, raw[..199] })
        {
            bool rejected = false;
            try { MultakLyrics.Parse(invalid); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Malformed or unsupported original lyric structures must be rejected.");
        }
    }
}
