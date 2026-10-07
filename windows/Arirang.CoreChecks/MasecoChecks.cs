using System.Buffers.Binary;
using Arirang.Core;

internal static class MasecoChecks
{
    internal static void Run()
    {
        var bytes = new byte[4106];
        "Multak MID10"u8.CopyTo(bytes.AsSpan(0x7d0));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x800), 21);
        void Record(int at, byte code, byte textOffset)
        {
            bytes[at + 6] = 4; bytes[at + 11] = 3; bytes[at + 13] = code;
            bytes[at + 16] = textOffset; bytes[at + 17] = bytes[at + 18] = 255;
        }
        Record(0x804, 1, 0); Record(0x819, 2, 5);
        bytes[0x82e + 16] = 10; // bounded end record
        "HELLOWORLD"u8.CopyTo(bytes.AsSpan(4096));
        var songs = MasecoIndex.Parse(bytes);
        if (songs.Count != 2 || songs[0].DeviceCode != 30001 || songs[1].EnglishTitle != "WORLD")
            throw new Exception("MASECO BCD device codes and title extents.");
        var badBcd = bytes.ToArray(); badBcd[0x804 + 13] = 0xfa;
        var badTitle = bytes.ToArray(); badTitle[0x819 + 16] = 11;
        var duplicate = bytes.ToArray(); duplicate[0x819 + 13] = 1;
        foreach (var invalid in new[] { bytes[..^1], badBcd, badTitle, duplicate })
        {
            bool rejected = false;
            try { MasecoIndex.Parse(invalid); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Invalid MASECO records must be rejected.");
        }
        var header = new byte[MultakIndex.TableOffset + 12];
        "multak3.3"u8.CopyTo(header.AsSpan(4));
        for (int i = 16; i < 334; i += 2) BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(i), ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16 + 30 * 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(334), 3);
        new byte[] { 255, 0, 255, 255, 0, 0, 1, 0, 0, 0, 2, 0 }.CopyTo(header, MultakIndex.TableOffset);
        var table = MultakIndex.Parse(header, 100_000, 0);
        if (table.FindSong(30001)?.TableIndex != 1 || table.FindSong(30002)?.Offset != 69632 ||
            table.FindSong(30000) is not null || table.FindSong(20001) is not null ||
            table.FindSong(-1) is not null || table.FindSong(int.MaxValue) is not null)
            throw new Exception("Song-block mapping must preserve null, unsupported and out-of-range codes.");
    }
}
