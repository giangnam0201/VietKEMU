using System.Buffers.Binary;
using System.Text;

namespace Arirang.Core;

// Preserve every original title; decode only established language encodings.
public sealed record MasecoSong(int DeviceCode, byte LanguageId, byte[] TitleBytes)
{
    public string? EnglishTitle => LanguageId == 4 && TitleBytes.All(b => b is >= 32 and < 127)
        ? Encoding.ASCII.GetString(TitleBytes) : null;
    public string? VietnameseTitle => LanguageId == 7 ? MultakVietnameseText.Decode(TitleBytes) : null;
    public string? SupportedTitle => EnglishTitle ?? VietnameseTitle;
}

public static class MasecoIndex
{
    public const int MaximumBytes = 32 * 1024 * 1024;
    private const int RecordStart = 0x804;
    private const int RecordBytes = 21;

    public static IReadOnlyList<MasecoSong> Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaximumBytes || data.Length < RecordStart + RecordBytes ||
            !data.Slice(0x7d0, 12).SequenceEqual("Multak MID10"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0x800, 4)) != RecordBytes)
            throw new InvalidDataException("Unsupported MASECOS4 index layout.");
        var records = new List<(int Code, byte Language, int TextOffset)>();
        var codes = new HashSet<int>();
        int at = RecordStart, textEnd;
        while (true)
        {
            if (at > data.Length - RecordBytes || records.Count > 100_000)
                throw new InvalidDataException("MASECOS4 index has no bounded end record.");
            var record = data.Slice(at, RecordBytes);
            int textOffset = Read24(record.Slice(14, 3));
            if (record[..14].IndexOfAnyExcept((byte)0) < 0)
            {
                if (record[17..].IndexOfAnyExcept((byte)0) >= 0)
                    throw new InvalidDataException("Invalid MASECOS4 end record.");
                textEnd = textOffset; at += RecordBytes; break;
            }
            int code = 0;
            foreach (byte pair in record.Slice(11, 3))
            {
                if (pair >> 4 > 9 || (pair & 15) > 9)
                    throw new InvalidDataException("Invalid BCD device song code.");
                code = code * 100 + (pair >> 4) * 10 + (pair & 15);
            }
            if (code == 0 || !codes.Add(code) || record[6] is 0 or > 29 || record[17] != 255 || record[18] != 255)
                throw new InvalidDataException("Invalid or duplicate MASECOS4 song record.");
            records.Add((code, record[6], textOffset)); at += RecordBytes;
        }
        int textBase = checked((at + 2047) / 2048 * 2048);
        if (records.Count == 0 || records[0].TextOffset != 0 || textEnd > data.Length - textBase)
            throw new InvalidDataException("MASECOS4 text storage falls outside the index.");
        var songs = new List<MasecoSong>(records.Count);
        for (int i = 0; i < records.Count; i++)
        {
            var record = records[i];
            int next = i + 1 < records.Count ? records[i + 1].TextOffset : textEnd;
            int length = next - record.TextOffset;
            if (length <= 0 || length > 512 || next > textEnd)
                throw new InvalidDataException("Invalid MASECOS4 title extent.");
            var title = data.Slice(textBase + record.TextOffset, length);
            songs.Add(new(record.Code, record.Language, title.ToArray()));
        }
        return songs;
    }

    public static IReadOnlyList<MasecoSong> ReadIso(string path, DiscInventory inventory)
    {
        var index = inventory.Files.SingleOrDefault(f => f.Name.Equals("MASECOS4.IDX", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Root MASECOS4.IDX not found.");
        if (index.Bytes > MaximumBytes || index.Bytes < 0)
            throw new InvalidDataException("MASECOS4 index exceeds size limit.");
        using var stream = File.OpenRead(path);
        if (index.Offset < 0 || index.Offset > stream.Length - index.Bytes)
            throw new InvalidDataException("Truncated MASECOS4 extent.");
        stream.Position = index.Offset;
        var bytes = new byte[(int)index.Bytes]; stream.ReadExactly(bytes);
        return Parse(bytes);
    }

    private static int Read24(ReadOnlySpan<byte> bytes) => bytes[0] << 16 | bytes[1] << 8 | bytes[2];
}
