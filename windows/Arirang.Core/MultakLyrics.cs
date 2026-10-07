namespace Arirang.Core;

public sealed record MultakLyricGlyph(long Tick, char Text, bool NewLine, byte Voice);
public sealed record MultakLyrics(IReadOnlyList<MultakLyricGlyph> Glyphs,
    int PrimaryBytes, int SecondaryBytes, int StaffOffset, long EndTick)
{
    public static MultakLyrics Parse(byte[] raw)
    {
        var layout = MultakSongLayout.Parse(raw);
        int titleEnd = 42 + layout.TitleBytes.Length;
        int links = titleEnd + 9 + layout.Tracks.Count * 7;
        int start = links + 7;
        if (start >= layout.MusicOffset) throw new InvalidDataException("Truncated MULTAK lyric header.");
        byte mask = raw[38];
        byte At(int offset) => (byte)(raw[offset] ^ mask);
        int relativeMusic = At(links) << 8 | At(links + 1);
        int relativeStaff = At(links + 3) << 8 | At(links + 4);
        if (At(links + 2) != 0 || At(links + 5) != 0 || At(links + 6) is not (2 or 3) ||
            titleEnd + 1 + relativeMusic != layout.MusicOffset)
            throw new InvalidDataException("Unsupported MULTAK lyric-link layout.");
        long primaryEnd = (long)start + layout.UninterpretedHeaderValue + 1;
        int staff = titleEnd + 1 + relativeStaff + 8;
        if (primaryEnd <= start || primaryEnd >= staff || staff >= layout.MusicOffset)
            throw new InvalidDataException("Invalid MULTAK lyric stream extents.");
        var glyphs = new List<MultakLyricGlyph>();
        long ReadStream(int begin, int limit, bool absolutePrefix)
        {
            int position = begin;
            byte Read()
            {
                if (position >= limit) throw new InvalidDataException("Truncated MULTAK lyric command.");
                return At(position++);
            }
            int ReadTime()
            {
                byte first = Read();
                return first < 128 ? first : (first & 127) << 8 | Read();
            }
            // The compact scheduler initializes its clock at one.
            long tick = 1 + (absolutePrefix ? ReadTime() : 0);
            byte voice = 0;
            bool newLine = false;
            int lineLength = 0;
            if (!absolutePrefix && At(begin) != 0x29)
                throw new InvalidDataException("Unsupported MULTAK primary lyric prefix.");
            for (int operations = 0; operations < 500_000; operations++)
            {
                byte op = Read();
                if (op == 0x1a)
                {
                    if (Read() != 255 || position != limit)
                        throw new InvalidDataException("Invalid MULTAK lyric ending.");
                    return tick;
                }
                if (op is 0x26 or 0x5e)
                {
                    byte lane = Read();
                    if (lane is not (1 or 2)) throw new InvalidDataException("Unsupported MULTAK lyric voice.");
                    if (op == 0x26) { voice = lane; newLine = true; lineLength = 0; }
                    else if (voice != lane) throw new InvalidDataException("MULTAK lyric voice ending does not match.");
                    continue; // Voice/format parameters are not delays.
                }
                if (op is 0 or 1 or 2 or 4 or 5 or 7 or 9 or 0x29)
                {
                    tick = checked(tick + ReadTime());
                }
                else if (op == 0x5c)
                {
                    newLine = true; lineLength = 0; tick = checked(tick + ReadTime());
                }
                else
                {
                    if (op is < 32 or > 126 || voice == 0)
                        throw new InvalidDataException("This MULTAK lyric encoding is not supported yet.");
                    if (glyphs.Count >= 500_000) throw new InvalidDataException("MULTAK lyric glyph limit exceeded.");
                    if (++lineLength > 1024) throw new InvalidDataException("MULTAK lyric line exceeds display limit.");
                    glyphs.Add(new(tick, (char)op, newLine, voice)); newLine = false;
                    tick = checked(tick + ReadTime());
                }
                if (tick > int.MaxValue) throw new InvalidDataException("MULTAK lyric tick limit exceeded.");
            }
            throw new InvalidDataException("MULTAK lyric command limit exceeded.");
        }
        long firstTick = ReadStream(start, (int)primaryEnd, false);
        long secondTick = ReadStream((int)primaryEnd, staff, true);
        return new(glyphs.OrderBy(g => g.Tick).ToArray(), (int)primaryEnd - start,
            staff - (int)primaryEnd, staff, Math.Max(firstTick, secondTick));
    }
}
