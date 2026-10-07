using System.Buffers.Binary;

namespace Arirang.Core;

public sealed record MultakTrackLayout(byte PitchBits, byte Channel, byte UnknownFormat,
    int RelativeOffset, byte BasePitch);

// Structural decoding only: never treat these byte streams as Standard MIDI.
public sealed record MultakSongLayout(byte[] TitleBytes, int MusicOffset,
    uint UninterpretedLengthField, int UninterpretedHeaderValue,
    IReadOnlyList<MultakTrackLayout> Tracks)
{
    public const int MaximumBytes = 4 * 1024 * 1024;

    public static MultakSongLayout Parse(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 43 || raw.Length > MaximumBytes ||
            !raw[..4].SequenceEqual(new byte[] { 0, 0, 0x4f, 0x4b }))
            throw new InvalidDataException("Only bounded simple MULTAK song records are supported; vocal records need a separate reader.");
        byte mask = raw[38];
        int relativeEnd = raw.Slice(42, Math.Min(513, raw.Length - 42)).IndexOf(mask);
        if (relativeEnd <= 0 || relativeEnd > 512)
            throw new InvalidDataException("No bounded MULTAK title terminator.");
        int titleEnd = 42 + relativeEnd;
        var title = raw.Slice(42, relativeEnd).ToArray();
        for (int i = 0; i < title.Length; i++) title[i] ^= mask;
        if (title.Any(value => value < 32)) throw new InvalidDataException("Invalid MULTAK title bytes.");
        if (titleEnd > raw.Length - 8) throw new InvalidDataException("Truncated MULTAK track header.");
        int trackCount = raw[titleEnd + 7] ^ mask;
        int tableStart = titleEnd + 9;
        if (trackCount is < 1 or > 16 || tableStart > raw.Length - trackCount * 7)
            throw new InvalidDataException("Invalid MULTAK channel table.");
        if (Decode24(raw, titleEnd + 1, mask) != 16 + trackCount * 7)
            throw new InvalidDataException("Unsupported MULTAK track-header layout.");
        long music = 34L + BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(38, 4));
        if (music <= tableStart + trackCount * 7 || music >= raw.Length ||
            (raw[(int)music - 2] ^ mask) != 0x1a || (raw[(int)music - 1] ^ mask) != 0xff)
            throw new InvalidDataException("MULTAK lyric/music boundary is invalid or truncated.");
        var tracks = new List<MultakTrackLayout>(trackCount);
        var channels = new HashSet<byte>();
        for (int i = 0; i < trackCount; i++)
        {
            int at = tableStart + i * 7;
            byte channel = (byte)(raw[at] ^ mask), pitchBits = (byte)(raw[at + 6] ^ mask);
            byte basePitch = (byte)(raw[at + 5] ^ mask);
            int offset = Decode24(raw, at + 2, mask);
            if (channel > 15 || pitchBits is < 2 or > 8 || basePitch > 127 || !channels.Add(channel) || offset >= raw.Length - music ||
                (i == 0 ? offset != 0 : offset <= tracks[^1].RelativeOffset))
                throw new InvalidDataException("Invalid MULTAK track channel or music extent.");
            tracks.Add(new(pitchBits, channel, (byte)(raw[at + 1] ^ mask), offset, basePitch));
        }
        return new(title, (int)music, BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(34, 4)),
            Decode24(raw, titleEnd + 4, mask), tracks);
    }

    private static int Decode24(ReadOnlySpan<byte> raw, int at, byte mask) =>
        (raw[at] ^ mask) << 16 | (raw[at + 1] ^ mask) << 8 | (raw[at + 2] ^ mask);
}
